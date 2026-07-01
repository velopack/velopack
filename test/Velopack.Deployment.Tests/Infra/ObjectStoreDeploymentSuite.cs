using System.Text;
using Neovolve.Logging.Xunit;
using Velopack.Core;
using Velopack.Deployment;
using Velopack.Sources;
using Velopack.Util;
using Xunit;

namespace Velopack.Deployment.Tests;

/// <summary>
/// A best-effort async cleanup wrapper, so concrete suites can return a bucket/container/directory scope
/// as an <see cref="IAsyncDisposable"/> without a bespoke type each.
/// </summary>
internal sealed class AsyncCleanup(Func<ValueTask> onDispose) : IAsyncDisposable
{
    public ValueTask DisposeAsync() => onDispose();
}

/// <summary>
/// Shared destination-test suite for every object-store style vpk deployment target (S3, Azure, local dir).
/// Concrete classes are tiny: they only implement the hooks that create/delete the backing store, run the
/// in-process upload/download runners against the current scope, and build an <see cref="IObjectStoreClient"/>
/// for feed/object verification. The four inherited [Fact]s exercise the full retention/idempotency lifecycle.
/// </summary>
public abstract class ObjectStoreDeploymentSuite
{
    /// <summary> The single explicit channel used across the whole suite (never OS-defaulted, for determinism). </summary>
    protected const string Channel = "objstore";

    protected readonly ITestOutputHelper Output;

    protected ObjectStoreDeploymentSuite(ITestOutputHelper output)
    {
        Output = output;
    }

    /// <summary> The packed app id. Overridable if a provider needs an isolated cache key. </summary>
    protected virtual string AppId => "ObjStoreApp";

    /// <summary> Skips the test (never fails) when the backing service/toolchain is unavailable. </summary>
    protected abstract Task SkipUnlessReadyAsync();

    /// <summary> Creates a fresh, empty backing store (bucket/container/dir) and returns a disposable that deletes it. </summary>
    protected abstract Task<IAsyncDisposable> CreateScopeAsync(ILogger log);

    /// <summary> Runs the provider's upload command runner in-process against the current scope. </summary>
    protected abstract Task UploadAsync(string releaseDir, string channel, int keepMaxReleases, ILogger log);

    /// <summary> Runs the provider's download command runner in-process against the current scope. </summary>
    protected abstract Task DownloadAsync(string releaseDir, string channel, ILogger log);

    /// <summary> Builds an object-store client pointed at the current scope, used for feed/object assertions. </summary>
    protected abstract IObjectStoreClient CreateClient(ILogger log);

    /// <summary> A publicly-readable base URL for the current scope, or null when the store has no HTTP surface. </summary>
    protected virtual string? GetPublicFeedUrl() => null;

    protected string FullNupkgName(string version) => $"{AppId}-{version}-{Channel}-full.nupkg";

    protected ICacheLogger<ObjectStoreDeploymentSuite> CreateLogger()
        => Output.BuildLoggerFor<ObjectStoreDeploymentSuite>();

    protected async Task<VelopackAssetFeed> ReadRemoteFeedAsync(ILogger log)
    {
        var client = CreateClient(log);
        var bytes = await client.GetObjectBytes($"releases.{Channel}.json").ConfigureAwait(false);
        if (bytes == null || bytes.Length == 0)
            return new VelopackAssetFeed();
        return VelopackAssetFeed.FromJson(Encoding.UTF8.GetString(bytes));
    }

    protected async Task<bool> RemoteObjectExistsAsync(string fileName, ILogger log)
    {
        var client = CreateClient(log);
        var bytes = await client.GetObjectBytes(fileName).ConfigureAwait(false);
        return bytes != null && bytes.Length > 0;
    }

    private static VelopackAsset[] Fulls(VelopackAssetFeed feed)
        => feed.Assets.Where(a => a.Type == VelopackAssetType.Full).OrderBy(a => a.Version).ToArray();

    private static VelopackAsset[] Deltas(VelopackAssetFeed feed)
        => feed.Assets.Where(a => a.Type == VelopackAssetType.Delta).ToArray();

    /// <summary> When a public URL exists, additionally verify the feed is reachable via <see cref="SimpleWebSource"/>. </summary>
    protected async Task VerifyPublicFeedAsync(string expectedLatestVersion, ILogger log)
    {
        var url = GetPublicFeedUrl();
        if (url == null)
            return;

        var source = new SimpleWebSource(url);
        var feed = await source.GetReleaseFeed(log.ToVelopackLogger(), null, Channel).ConfigureAwait(false);
        var latest = feed.Assets.Where(a => a.Type == VelopackAssetType.Full).OrderByDescending(a => a.Version).FirstOrDefault();
        Assert.NotNull(latest);
        Assert.Equal(expectedLatestVersion, latest!.Version.ToString());
    }

    protected static void TryDeleteDir(string dir) => ReleaseFixtures.TryDeleteDir(dir);

    [Fact]
    public async Task InitialUploadCreatesFeedAndAssets()
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var pack = ReleaseFixtures.GetCachedPack(AppId, Channel, log, "1.0.0");
        try {
            await UploadAsync(pack.ReleaseDir, Channel, 0, log);

            var feed = await ReadRemoteFeedAsync(log);
            var fulls = Fulls(feed);
            Assert.Single(fulls);
            Assert.Equal("1.0.0", fulls[0].Version.ToString());
            Assert.True(await RemoteObjectExistsAsync(FullNupkgName("1.0.0"), log), "1.0.0 full package object should exist in the store.");
            await VerifyPublicFeedAsync("1.0.0", log);
        } finally {
            TryDeleteDir(pack.ReleaseDir);
        }
    }

    [Fact]
    public async Task IncrementalUploadAddsDeltaAndPrunes()
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var p1 = ReleaseFixtures.GetCachedPack(AppId, Channel, log, "1.0.0");
        var p2 = ReleaseFixtures.GetCachedPack(AppId, Channel, log, "1.0.0", "2.0.0");
        var p3 = ReleaseFixtures.GetCachedPack(AppId, Channel, log, "1.0.0", "2.0.0", "3.0.0");
        try {
            // 1) baseline full
            await UploadAsync(p1.ReleaseDir, Channel, 0, log);

            // 2) delta added, no pruning yet
            await UploadAsync(p2.ReleaseDir, Channel, 0, log);
            var feed2 = await ReadRemoteFeedAsync(log);
            Assert.Equal(new[] { "1.0.0", "2.0.0" }, Fulls(feed2).Select(a => a.Version.ToString()).ToArray());
            Assert.Contains(Deltas(feed2), a => a.Version.ToString() == "2.0.0");
            Assert.True(await RemoteObjectExistsAsync(FullNupkgName("2.0.0"), log));

            // 3) prune to newest 2 fulls -> 1.0.0 removed from feed AND deleted from store
            await UploadAsync(p3.ReleaseDir, Channel, keepMaxReleases: 2, log);
            var feed3 = await ReadRemoteFeedAsync(log);
            Assert.Equal(new[] { "2.0.0", "3.0.0" }, Fulls(feed3).Select(a => a.Version.ToString()).ToArray());
            Assert.DoesNotContain(feed3.Assets, a => a.Version.ToString() == "1.0.0");
            Assert.False(await RemoteObjectExistsAsync(FullNupkgName("1.0.0"), log), "Pruned 1.0.0 full package object should have been deleted (404).");
            await VerifyPublicFeedAsync("3.0.0", log);
        } finally {
            TryDeleteDir(p1.ReleaseDir);
            TryDeleteDir(p2.ReleaseDir);
            TryDeleteDir(p3.ReleaseDir);
        }
    }

    [Fact]
    public async Task UploadIsIdempotent_Md5SkipAndOverwrite()
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var pack = ReleaseFixtures.GetCachedPack(AppId, Channel, log, "1.0.0");
        try {
            var nupkgName = FullNupkgName("1.0.0");
            await UploadAsync(pack.ReleaseDir, Channel, 0, log);
            var before = Fulls(await ReadRemoteFeedAsync(log));

            // second upload of the identical dir must be a no-op for the feed and must not throw,
            // and the unchanged nupkg must hit the md5-compare skip branch (not be silently re-uploaded)
            await UploadAsync(pack.ReleaseDir, Channel, 0, log);
            var after = Fulls(await ReadRemoteFeedAsync(log));

            Assert.Equal(before.Select(a => a.Version.ToString()), after.Select(a => a.Version.ToString()));
            Assert.Single(after);
            Assert.True(await RemoteObjectExistsAsync(nupkgName, log));
            Assert.Contains(log.Entries, e => e.Message.Contains($"Upload file '{nupkgName}' skipped (already exists in remote)"));

            // mutate the remote nupkg via a separate logger (so the 'replacing' assert below can only be
            // satisfied by the runner), then re-upload: differing md5 + overwrite must replace the object
            using (var mutateLog = CreateLogger()) {
                using var _1 = TempUtil.GetTempFileName(out var mutated);
                File.WriteAllText(mutated, "mutated remote object content");
                await CreateClient(mutateLog).UploadObject(nupkgName, new FileInfo(mutated), overwriteRemote: true, noCache: false);
            }

            await UploadAsync(pack.ReleaseDir, Channel, 0, log);
            Assert.Contains(log.Entries, e => e.Message.Contains($"File '{nupkgName}' exists in remote, replacing"));
            var restored = await CreateClient(log).GetObjectBytes(nupkgName);
            Assert.Equal(new FileInfo(Path.Combine(pack.ReleaseDir, nupkgName)).Length, restored.Length);
        } finally {
            TryDeleteDir(pack.ReleaseDir);
        }
    }

    [Fact]
    public async Task DownloadRoundTrip()
    {
        await SkipUnlessReadyAsync();
        using var log = CreateLogger();
        await using var scope = await CreateScopeAsync(log);

        var pack = ReleaseFixtures.GetCachedPack(AppId, Channel, log, "1.0.0");
        try {
            await UploadAsync(pack.ReleaseDir, Channel, 0, log);

            using var _1 = TempUtil.GetTempDirectory(out var downloadDir);
            await DownloadAsync(downloadDir, Channel, log);
            var downloaded = Path.Combine(downloadDir, FullNupkgName("1.0.0"));
            Assert.True(File.Exists(downloaded), $"Expected downloaded package at {downloaded}");

            var firstWrite = File.GetLastWriteTimeUtc(downloaded);

            // re-running must short-circuit on the verified existing file (no re-download / no error)
            await DownloadAsync(downloadDir, Channel, log);
            Assert.True(File.Exists(downloaded));
            Assert.Equal(firstWrite, File.GetLastWriteTimeUtc(downloaded));
        } finally {
            TryDeleteDir(pack.ReleaseDir);
        }
    }
}
