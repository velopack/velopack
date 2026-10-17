#nullable enable
using System.Text;
using Velopack.Chunks;
using Velopack.Core;
using Velopack.Sources;
using Velopack.Tests.Chunks;
using Velopack.Util;

namespace Velopack.Tests;

/// <summary>
/// The chunk update path of <see cref="UpdateManager"/> (§9.4) and every fallback row of §11 reachable in-process, with a
/// fake range source and a fake <c>Update.exe assemble</c>. Every fallback asserts the classic outcome (the full .nupkg
/// downloaded) and that reported progress never decreased.
/// </summary>
public partial class UpdateManagerTests
{
    private static readonly bool ChunksSupported = VelopackRuntimeInfo.IsWindows;

    /// <summary>
    /// Runs the download twice: first with the full download failing, so the chunk path's fallback is observable and
    /// the staged directory's fate can be checked, then with it succeeding.
    /// </summary>
    private static async Task AssertFallsBackToFullAsync(ChunkTestEnv env, bool stagedKept)
    {
        env.Source.FullDownloadFails = true;
        await Assert.ThrowsAsync<IOException>(() => env.DownloadAsync());
        env.AssertProgressMonotonic(complete: false);
        Assert.Equal(1, env.Source.FullDownloads);
        Assert.False(File.Exists(env.NupkgPath));
        Assert.Equal(stagedKept, Directory.Exists(env.StagedDir));
        Assert.Null(env.ReadyStaged);

        env.Source.FullDownloadFails = false;
        env.Progress.Clear();
        await env.DownloadAsync();
        Assert.Equal(2, env.Source.FullDownloads);
        Assert.True(File.Exists(env.NupkgPath));
        env.AssertProgressMonotonic(complete: true);
        // a downloaded package supersedes every staged tree
        Assert.False(Directory.Exists(env.StagedRoot) && Directory.GetDirectories(env.StagedRoot).Length > 0);
    }

    [Fact]
    public async Task ChunkPathStagesUpdate()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        await env.DownloadAsync();

        env.AssertProgressMonotonic(complete: true);
        Assert.Equal(0, env.Source.FullDownloads);
        Assert.False(File.Exists(env.NupkgPath));
        var ready = env.ReadyStaged;
        Assert.NotNull(ready);
        Assert.Equal(env.V2.Asset.ChunkIndexSHA256, ready!.Marker.IndexSHA256);
        Assert.Equal(env.V2.Asset.Version, ready.Asset.Version);
        Assert.Equal(env.V2.SectionA, File.ReadAllBytes(Path.Combine(ready.Dir, "current", "sq.chunks")));
        Assert.Equal(env.V2.Files["lib/New.dll"], File.ReadAllBytes(Path.Combine(ready.Dir, "current", "lib", "New.dll")));
        Assert.Equal(env.V2.Files["App.exe"], File.ReadAllBytes(Path.Combine(ready.Dir, "current", "App.exe")));
        // the sidecar updater was installed over UpdateExePath at download time
        Assert.Equal(env.V2.Files["Squirrel.exe"], File.ReadAllBytes(env.UpdateExe));

        // only the changed parts were fetched: the unchanged Core.dll and blob.bin (most of the package) never crossed the wire
        Assert.Equal(1, env.Source.ReadersOpened);
        Assert.InRange(env.Source.Reader!.BytesRead, env.V2.Trailer.Length + 1, env.V2.Bytes.Length / 2);

        // the assemble command line is pinned (§7.1)
        var args = Assert.Single(env.Assembler.Calls);
        Assert.Equal(
            new[] { "--silent", "assemble", "--staged", env.StagedDir, "--parentPid", args[5], "--rootDir", env.Root, "--packageDir", env.Packages },
            args);
        Assert.True(uint.Parse(args[5]) > 0);

        // the pending update is the staged asset, pointer included
        var pending = env.Um.UpdatePendingRestart;
        Assert.NotNull(pending);
        Assert.Equal(env.V2.Asset.ChunkIndexSHA256, pending!.ChunkIndexSHA256);
        Assert.Equal(env.V2.Asset.ChunkIndexOffset, pending.ChunkIndexOffset);

        // a second download finds the ready tree and does nothing
        env.Progress.Clear();
        await env.DownloadAsync();
        Assert.Equal(new[] { 100 }, env.Progress);
        Assert.Single(env.Assembler.Calls);
        Assert.Equal(1, env.Source.ReadersOpened);
    }

    [Fact]
    public async Task ChunkPathMissingRoundFetchesReportedHashes()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        // corrupt a large installed file after planning would trust it by length: the assembler finds the bad chunks
        // and reports them, the manager fetches exactly those and re-runs assembly
        var core = Path.Combine(env.Current, "lib", "Core.dll");
        var bytes = File.ReadAllBytes(core);
        for (int i = 70_000; i < 70_100; i++) bytes[i] ^= 0xFF;
        File.WriteAllBytes(core, bytes);

        await env.DownloadAsync();
        env.AssertProgressMonotonic(complete: true);
        Assert.Equal(2, env.Assembler.Calls.Count);
        Assert.NotNull(env.ReadyStaged);
        Assert.False(File.Exists(Path.Combine(env.StagedDir, "missing.bin")));
        Assert.Equal(env.V2.Files["lib/Core.dll"], File.ReadAllBytes(Path.Combine(env.StagedDir, "current", "lib", "Core.dll")));
        Assert.Equal(0, env.Source.FullDownloads);
    }

    [Fact]
    public async Task ChunkPathFallsBackWhenUpdaterLacksAssemble()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        // an old updater: "No known subcommand was used", exit 1, no VPK: line
        env.Assembler.Script.Enqueue(_ => new UpdateExeRunResult(1, false, null, null));
        env.Assembler.Script.Enqueue(_ => new UpdateExeRunResult(1, false, null, null));

        await env.DownloadAsync();
        env.AssertProgressMonotonic(complete: true);
        Assert.True(File.Exists(env.NupkgPath));
        Assert.Single(env.Assembler.Calls);
        Assert.Equal(1, env.Source.FullDownloads);

        // the same instance never tries the chunk path again
        File.Delete(env.NupkgPath);
        env.Progress.Clear();
        await env.DownloadAsync();
        Assert.Single(env.Assembler.Calls);
        Assert.Equal(1, env.Source.ReadersOpened);
        Assert.Equal(2, env.Source.FullDownloads);
        env.AssertProgressMonotonic(complete: true);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnMissingPointer()
    {
        using var env = new ChunkTestEnv(_output);
        var plain = env.V2.Asset with { ChunkIndexOffset = null, ChunkIndexLength = null, ChunkIndexSHA256 = null };
        env.Source.Feed = new VelopackAssetFeed { Assets = new[] { plain } };

        await env.DownloadAsync();
        env.AssertProgressMonotonic(complete: true);
        Assert.True(File.Exists(env.NupkgPath));
        Assert.Equal(0, env.Source.ReadersOpened);
        Assert.Empty(env.Assembler.Calls);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnDisabledOption()
    {
        using var env = new ChunkTestEnv(_output, new UpdateOptions { DisableChunkedUpdates = true });
        await env.DownloadAsync();
        env.AssertProgressMonotonic(complete: true);
        Assert.True(File.Exists(env.NupkgPath));
        Assert.Equal(0, env.Source.ReadersOpened);
        Assert.Empty(env.Assembler.Calls);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnMissingLocalIndex()
    {
        using var env = new ChunkTestEnv(_output, installLocalIndex: false);
        await env.DownloadAsync();
        env.AssertProgressMonotonic(complete: true);
        Assert.True(File.Exists(env.NupkgPath));
        Assert.Equal(0, env.Source.ReadersOpened);
        Assert.Empty(env.Assembler.Calls);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnSourceWithoutRanges()
    {
        using var env = new ChunkTestEnv(_output, wrapSource: s => new FullOnlySource(s));
        await env.DownloadAsync();
        env.AssertProgressMonotonic(complete: true);
        Assert.True(File.Exists(env.NupkgPath));
        Assert.Equal(0, env.Source.ReadersOpened);
        Assert.Empty(env.Assembler.Calls);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnNullRangeReader()
    {
        using var env = new ChunkTestEnv(_output);
        env.Source.RangesSupported = false;
        await env.DownloadAsync();
        env.AssertProgressMonotonic(complete: true);
        Assert.True(File.Exists(env.NupkgPath));
        Assert.Empty(env.Assembler.Calls);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnRangeUnavailable()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        env.Source.RangeFault = (_, _) => new RangeUnavailableException("server ignored Range (test)");
        await AssertFallsBackToFullAsync(env, stagedKept: true);
        Assert.Empty(env.Assembler.Calls);
        Assert.Empty(env.Sleeps);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnIndexDigestMismatch()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        long trailerOffset = env.V2.TrailerOffset;
        env.Source.RangeMutate = (offset, buffer, bufferOffset, count) => {
            if (offset == trailerOffset) buffer[bufferOffset + 10] ^= 0xFF;
        };
        await AssertFallsBackToFullAsync(env, stagedKept: false);
        Assert.Empty(env.Assembler.Calls);
        // the index was retried once per download, nothing else was fetched
        Assert.Equal(2, env.Source.Reader!.Reads);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnInvalidIndex()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        // a trailer which hashes correctly but is garbage
        var garbage = TestPackageBuilder.Random(123, env.V2.Trailer.Length);
        var feedAsset = env.V2.Asset with { ChunkIndexSHA256 = ChunkInflate.Sha256(garbage).ToHex() };
        env.Source.Feed = new VelopackAssetFeed { Assets = new[] { feedAsset } };
        long trailerOffset = env.V2.TrailerOffset;
        env.Source.RangeMutate = (offset, buffer, bufferOffset, count) => {
            if (offset == trailerOffset) Buffer.BlockCopy(garbage, 0, buffer, bufferOffset, count);
        };
        await AssertFallsBackToFullAsync(env, stagedKept: false);
        Assert.Empty(env.Assembler.Calls);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnTransportCeiling()
    {
        if (!ChunksSupported) return;
        // nothing installed matches the remote: the plan needs (almost) the whole package
        var unrelated = TestPackageBuilder.Build(new Dictionary<string, byte[]> {
            ["Squirrel.exe"] = TestPackageBuilder.Text(31, 300_000),
            ["Other.exe"] = TestPackageBuilder.Random(32, 2_000_000),
            ["sq.version"] = Encoding.UTF8.GetBytes("<package><id>ChunkApp</id><version>1.1.0</version></package>"),
        }, version: "1.1.0");
        using var env = new ChunkTestEnv(_output, remote: unrelated);

        await env.DownloadAsync();
        env.AssertProgressMonotonic(complete: true);
        Assert.True(File.Exists(env.NupkgPath));
        Assert.Empty(env.Assembler.Calls);
        // only the index was read before the plan selected the classic path
        Assert.Equal(1, env.Source.Reader!.Reads);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnFreeSpacePreflight()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        env.Um.FreeSpaceQuery = () => 1024;

        await env.DownloadAsync();
        env.AssertProgressMonotonic(complete: true);
        Assert.True(File.Exists(env.NupkgPath));
        Assert.Empty(env.Assembler.Calls);
        Assert.Equal(1, env.Source.Reader!.Reads);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnFetchBudgetExhausted()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        long trailerOffset = env.V2.TrailerOffset;
        env.Source.RangeFault = (offset, _) => offset == trailerOffset ? null : new IOException("connection reset (test)");
        await AssertFallsBackToFullAsync(env, stagedKept: true);
        Assert.Empty(env.Assembler.Calls);
        Assert.True(env.Sleeps.Count >= RetryPolicy.MaxRetriesPerRequest);
        Assert.True(File.Exists(Path.Combine(env.Packages, env.V2.Asset.FileName)));
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnAssembleInvalid()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        env.Assembler.Script.Enqueue(_ => new UpdateExeRunResult(2, true, null, "bad index"));
        env.Assembler.Script.Enqueue(_ => new UpdateExeRunResult(2, true, null, "bad index"));
        await AssertFallsBackToFullAsync(env, stagedKept: false);
        Assert.Equal(2, env.Assembler.Calls.Count);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnAssembleIoError()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        env.Assembler.Script.Enqueue(_ => new UpdateExeRunResult(4, true, null, "disk full"));
        env.Assembler.Script.Enqueue(_ => new UpdateExeRunResult(4, true, null, "disk full"));
        await AssertFallsBackToFullAsync(env, stagedKept: false);
        Assert.Equal(2, env.Assembler.Calls.Count);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnAssembleFailed()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        env.Assembler.Script.Enqueue(_ => new UpdateExeRunResult(1, true, null, "something else"));
        env.Assembler.Script.Enqueue(_ => new UpdateExeRunResult(1, true, null, "something else"));
        await AssertFallsBackToFullAsync(env, stagedKept: true);
        Assert.Equal(2, env.Assembler.Calls.Count);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnTooManyMissingRounds()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        var someHash = env.V2.Remote.GetChunkHash(0);
        for (int i = 0; i < 6; i++) {
            env.Assembler.Script.Enqueue(stagedDir => {
                File.WriteAllBytes(Path.Combine(stagedDir, "missing.bin"), someHash.ToBytes());
                return new UpdateExeRunResult(3, true, 1, null);
            });
        }
        await AssertFallsBackToFullAsync(env, stagedKept: true);
        // one run plus two extra rounds per download
        Assert.Equal(6, env.Assembler.Calls.Count);
    }

    [Fact]
    public async Task ChunkPathFallsBackToFullOnSidecarMismatch()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        env.Assembler.CorruptSidecar = true;
        await AssertFallsBackToFullAsync(env, stagedKept: false);
        // the stale sidecar was never installed over Update.exe
        Assert.Equal(env.V1.Files["Squirrel.exe"], File.ReadAllBytes(env.UpdateExe));
    }

    [Fact]
    public async Task ChunkPathCancellationPropagatesAndKeepsStaged()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        using var cts = new CancellationTokenSource();
        long trailerOffset = env.V2.TrailerOffset;
        int reads = 0;
        env.Source.RangeFault = (offset, _) => {
            if (offset != trailerOffset && Interlocked.Increment(ref reads) == 2) cts.Cancel();
            return null;
        };

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => env.DownloadAsync(cts.Token));
        Assert.Equal(0, env.Source.FullDownloads);
        Assert.True(Directory.Exists(env.StagedDir));
        Assert.True(File.Exists(Path.Combine(env.StagedDir, "index.bin")));
        Assert.Null(env.ReadyStaged);
        env.AssertProgressMonotonic(complete: false);

        // the next download resumes: the index is reused and the tree completes
        env.Source.RangeFault = null;
        env.Progress.Clear();
        await env.DownloadAsync();
        Assert.NotNull(env.ReadyStaged);
        Assert.Equal(2, env.Source.ReadersOpened);
        Assert.DoesNotContain(trailerOffset, env.Source.Reader!.Offsets);
        env.AssertProgressMonotonic(complete: true);
        Assert.Equal(0, env.Source.FullDownloads);
    }

    [Fact]
    public async Task ReadyStagedMatchedByIdNotVersion()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        await env.DownloadAsync();
        var firstId = env.V2.StagedId;
        Assert.NotNull(env.ReadyStaged);

        // the same version and file name re-published with different content: a different staged id
        var files = new Dictionary<string, byte[]>(env.V2.Files);
        files["lib/New.dll"] = TestPackageBuilder.Text(66, 120_000);
        var republished = TestPackageBuilder.Build(files, version: "1.1.0");
        Assert.Equal(env.V2.Asset.FileName, republished.Asset.FileName);
        Assert.NotEqual(firstId, republished.StagedId);
        env.SetRemote(republished);
        env.Progress.Clear();

        await env.DownloadAsync();
        env.AssertProgressMonotonic(complete: true);
        Assert.Equal(2, env.Assembler.Calls.Count);
        Assert.Equal(new[] { republished.StagedId }, Directory.GetDirectories(env.StagedRoot).Select(Path.GetFileName));
        Assert.Equal(files["lib/New.dll"], File.ReadAllBytes(Path.Combine(env.StagedDir, "current", "lib", "New.dll")));
    }

    /// <summary>A git-style source whose assets carry a release payload, with ranged reads from memory.</summary>
    private sealed class FakeGitSource : GitBase<string>
    {
        private sealed class MemoryDownloader : IFileDownloader, IRangeFileDownloader
        {
            public string FeedJson = "";
            public byte[] Package = Array.Empty<byte>();

            public Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null,
                double timeout = 30, CancellationToken cancelToken = default)
            {
                File.WriteAllBytes(targetFile, Package);
                progress(100);
                return Task.CompletedTask;
            }

            public Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30)
                => Task.FromResult(Encoding.UTF8.GetBytes(FeedJson));

            public Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30)
                => Task.FromResult(FeedJson);

            public IRangeReader OpenRangeReader(string url, IDictionary<string, string>? headers, double timeoutMinutes)
                => new MemoryRangeReader(Package);
        }

        public FakeGitSource(TestPackage remote)
            : base("https://example.invalid/owner/repo", null, false, new MemoryDownloader())
        {
            var d = (MemoryDownloader) Downloader;
            d.Package = remote.Bytes;
            d.FeedJson = SimpleJson.SerializeObject(new VelopackAssetFeed { Assets = new[] { remote.Asset } });
        }

        protected override KeyValuePair<string, string>? Authorization => null;

        protected override Task<string[]> GetReleases(bool includePrereleases) => Task.FromResult(new[] { "release-1" });

        protected override string GetAssetUrlFromName(string release, string assetName) => $"https://example.invalid/{release}/{assetName}";
    }

    [Fact]
    public async Task AssetJsonIsPlainForGitSources()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        var um = new UpdateManager(new FakeGitSource(env.V2), null, env.Locator) {
            AssembleRunner = env.Assembler.RunAsync,
            ChunkRetryPolicy = env.Um.ChunkRetryPolicy,
        };
        var info = um.CheckForUpdates();
        Assert.NotNull(info);
        Assert.IsAssignableFrom<GitBase<string>.GitBaseAsset>(info!.TargetFullRelease);
        await um.DownloadUpdatesAsync(info);

        Assert.NotNull(env.ReadyStaged);
        var assetJson = File.ReadAllText(Path.Combine(env.StagedDir, "asset.json"));
        Assert.DoesNotContain("Release", assetJson);
        var parsed = CompiledJson.DeserializeVelopackAsset(assetJson);
        Assert.NotNull(parsed);
        Assert.Equal(env.V2.Asset.ChunkIndexSHA256, parsed!.ChunkIndexSHA256);
        Assert.Equal(env.V2.Asset.Version, parsed.Version);
        Assert.Equal(env.V2.Asset.FileName, parsed.FileName);
        Assert.DoesNotContain("Release", File.ReadAllText(Path.Combine(env.StagedDir, "ready.json")));
        Assert.NotNull(um.UpdatePendingRestart);
    }

    [Fact]
    public async Task StaleFallbackUpdaterIsRefreshedBeforeAssemble()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output, useFallbackUpdateExe: true);
        var rootUpdater = ChunkInflate.Sha256File(Path.Combine(env.Root, "Update.exe")).ToHex();
        Assert.NotEqual(rootUpdater, ChunkInflate.Sha256File(env.UpdateExe).ToHex());

        await env.DownloadAsync();
        env.AssertProgressMonotonic(complete: true);
        Assert.NotNull(env.ReadyStaged);
        // the assembler ran through the root's (vendored) updater, not the stale fallback copy
        Assert.Equal(new[] { rootUpdater }, env.Assembler.UpdaterHashesAtCall);
        Assert.Single(env.Assembler.Calls);
        // and afterwards the fallback copy carries the target's updater
        Assert.Equal(env.V2.Files["Squirrel.exe"], File.ReadAllBytes(env.UpdateExe));
        Assert.Equal(env.V1.Files["Squirrel.exe"], File.ReadAllBytes(Path.Combine(env.Root, "Update.exe")));
    }

    [Fact]
    public async Task ApplyStagedPinsCliArguments()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        await env.DownloadAsync();
        var stagedDir = env.ReadyStaged!.Dir;

        // explicit asset with a pointer and a matching ready tree
        env.Um.WaitExitThenApplyUpdates(env.V2.Asset, silent: true, restart: true, new[] { "--restarted", "x y" });
        var (exe, args, workDir) = Assert.Single(env.Locator.Started);
        Assert.Equal(env.UpdateExe, exe);
        Assert.Equal(Path.GetDirectoryName(env.UpdateExe), workDir);
        Assert.Equal(
            new[] {
                "--silent", "apply-staged", "--staged", stagedDir, "--rootDir", env.Root, "--packageDir", env.Packages, "--", "--restarted", "x y",
            },
            args);

        // null picks the pending staged tree; no restart adds --norestart and drops the args
        env.Locator.Started.Clear();
        env.Locator.ProcessId = 4242;
        env.Um.WaitExitThenApplyUpdates(null, silent: false, restart: false, new[] { "ignored" });
        (exe, args, _) = Assert.Single(env.Locator.Started);
        Assert.Equal(
            new[] { "apply-staged", "--staged", stagedDir, "--waitPid", "4242", "--norestart", "--rootDir", env.Root, "--packageDir", env.Packages },
            args);

        // the updater used is the target's: a stale UpdateExePath is replaced before launch
        File.WriteAllBytes(env.UpdateExe, TestPackageBuilder.Random(88, 500));
        env.Locator.Started.Clear();
        env.Um.ApplyUpdatesAndRestart(null);
        Assert.Single(env.Locator.Started);
        Assert.Equal(env.V2.Files["Squirrel.exe"], File.ReadAllBytes(env.UpdateExe));
        Assert.Equal(0, env.Locator.ExitCode);
    }

    [Fact]
    public async Task ApplyStagedRefusesMismatchedSidecar()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        await env.DownloadAsync();
        var stagedDir = env.ReadyStaged!.Dir;
        File.WriteAllBytes(Path.Combine(stagedDir, "Update.exe"), new byte[] { 9, 9, 9 });

        Assert.Throws<InvalidDataException>(() => UpdateExe.ApplyStaged(env.Locator, stagedDir, true, 0, true));
        Assert.Empty(env.Locator.Started);
    }

    [Fact]
    public async Task ApplyWithoutStagedTreeUsesClassicApply()
    {
        using var env = new ChunkTestEnv(_output, new UpdateOptions { DisableChunkedUpdates = true });
        await env.DownloadAsync();
        Assert.True(File.Exists(env.NupkgPath));

        env.Um.WaitExitThenApplyUpdates(env.V2.Asset, silent: false, restart: true);
        var (_, args, _) = Assert.Single(env.Locator.Started);
        Assert.Equal(new[] { "apply", "--package", env.NupkgPath, "--rootDir", env.Root, "--packageDir", env.Packages }, args);
    }

    [Fact]
    public async Task VelopackAppAutoAppliesStagedTree()
    {
        if (!ChunksSupported) return;
        using var env = new ChunkTestEnv(_output);
        await env.DownloadAsync();
        var stagedDir = env.ReadyStaged!.Dir;

        VelopackApp.Build().SetLocator(env.Locator).SetArgs(new[] { "--flag" }).Run();

        var (exe, args, _) = Assert.Single(env.Locator.Started);
        Assert.Equal(env.UpdateExe, exe);
        Assert.Equal(new[] { "apply-staged", "--staged", stagedDir, "--rootDir", env.Root, "--packageDir", env.Packages, "--", "--flag" }, args);
        Assert.Equal(0, env.Locator.ExitCode);
        // the pending tree survives startup cleanup
        Assert.NotNull(env.ReadyStaged);
    }
}
