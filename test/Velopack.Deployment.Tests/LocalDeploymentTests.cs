using Velopack.Core;
using Velopack.Deployment;
using Velopack.Util;
using Xunit;

namespace Velopack.Deployment.Tests;

/// <summary>
/// Object-store deployment lifecycle against a plain local directory (vpk upload/download local).
/// Needs no external service, so it never skips. In addition to the inherited suite it covers the
/// local-only <c>ForceRegenerate</c> flag, the nupkg-scan feed fallback, and TargetPath validation.
/// </summary>
public class LocalDeploymentTests(ITestOutputHelper output) : ObjectStoreDeploymentSuite(output)
{
    private string _targetDir = "";

    protected override Task SkipUnlessReadyAsync() => Task.CompletedTask; // local disk is always available

    protected override Task<IAsyncDisposable> CreateScopeAsync(ILogger log)
    {
        _targetDir = Path.Combine(Path.GetTempPath(), "velopack-localtarget-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_targetDir);
        var dir = _targetDir;
        return Task.FromResult<IAsyncDisposable>(
            new AsyncCleanup(() => {
                try {
                    if (Directory.Exists(dir))
                        Directory.Delete(dir, true);
                } catch { /* best-effort */ }

                return ValueTask.CompletedTask;
            }));
    }

    protected override Task UploadAsync(string releaseDir, string channel, int keepMaxReleases, ILogger log)
        => new LocalUploadCommandRunner(log).Run(new LocalUploadOptions {
            TargetPath = new DirectoryInfo(_targetDir),
            ReleaseDir = new DirectoryInfo(releaseDir),
            Channel = channel,
            KeepMaxReleases = keepMaxReleases,
        });

    protected override Task DownloadAsync(string releaseDir, string channel, ILogger log)
        => new LocalDownloadCommandRunner(log).Run(new LocalDownloadOptions {
            TargetPath = new DirectoryInfo(_targetDir),
            ReleaseDir = new DirectoryInfo(releaseDir),
            Channel = channel,
        });

    protected override IObjectStoreClient CreateClient(ILogger log)
        => new LocalObjectStoreClient(new DirectoryInfo(_targetDir), log);

    [Fact]
    public async Task ForceRegenerateRebuildsReleaseIndexFromPackagesOnDisk()
    {
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var p1 = ReleaseFixtures.GetCachedPack(AppId, Channel, log, "1.0.0");
        var p2 = ReleaseFixtures.GetCachedPack(AppId, Channel, log, "1.0.0", "2.0.0");
        try {
            // seed the target with 1.0.0 and 2.0.0, then corrupt the index so it lists nothing.
            await UploadAsync(p1.ReleaseDir, Channel, 0, log);
            await UploadAsync(p2.ReleaseDir, Channel, 0, log);
            var indexPath = Path.Combine(_targetDir, $"releases.{Channel}.json");
            Assert.True(File.Exists(indexPath));
            File.WriteAllText(indexPath, "{}");

            // without ForceRegenerate this upload would rewrite the feed with only the 1.0.0 entry from
            // the release dir; ForceRegenerate first rebuilds the index from the *.nupkg files on disk.
            await new LocalUploadCommandRunner(log).Run(new LocalUploadOptions {
                TargetPath = new DirectoryInfo(_targetDir),
                ReleaseDir = new DirectoryInfo(p1.ReleaseDir),
                Channel = Channel,
                ForceRegenerate = true,
            });

            var feed = await ReadRemoteFeedAsync(log);
            var fulls = feed.Assets.Where(a => a.Type == VelopackAssetType.Full).Select(a => a.Version.ToString()).OrderBy(v => v).ToArray();
            Assert.Equal(new[] { "1.0.0", "2.0.0" }, fulls);
        } finally {
            TryDeleteDir(p1.ReleaseDir);
            TryDeleteDir(p2.ReleaseDir);
        }
    }

    [Fact]
    public async Task DownloadFallsBackToNupkgScanWhenIndexMissing()
    {
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var pack = ReleaseFixtures.GetCachedPack(AppId, Channel, log, "1.0.0");
        try {
            // target contains ONLY the nupkg — no releases.{channel}.json index at all.
            var nupkgName = FullNupkgName("1.0.0");
            File.Copy(Path.Combine(pack.ReleaseDir, nupkgName), Path.Combine(_targetDir, nupkgName));

            using var _1 = TempUtil.GetTempDirectory(out var downloadDir);
            await DownloadAsync(downloadDir, Channel, log);
            Assert.True(File.Exists(Path.Combine(downloadDir, nupkgName)), "download should succeed via the *.nupkg directory scan fallback");
        } finally {
            TryDeleteDir(pack.ReleaseDir);
        }
    }

    [Fact]
    public async Task UploadRefusesWhenTargetPathIsAFile()
    {
        using var log = CreateLogger();
        var pack = ReleaseFixtures.GetCachedPack(AppId, Channel, log, "1.0.0");
        var filePath = Path.Combine(Path.GetTempPath(), "velopack-localtarget-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(filePath, "not a directory");
        try {
            var ex = await Assert.ThrowsAsync<UserInfoException>(() => new LocalUploadCommandRunner(log).Run(new LocalUploadOptions {
                TargetPath = new DirectoryInfo(filePath),
                ReleaseDir = new DirectoryInfo(pack.ReleaseDir),
                Channel = Channel,
            }));
            Assert.Contains("file already exists", ex.Message);
        } finally {
            try { File.Delete(filePath); } catch { /* best-effort */ }
            TryDeleteDir(pack.ReleaseDir);
        }
    }
}
