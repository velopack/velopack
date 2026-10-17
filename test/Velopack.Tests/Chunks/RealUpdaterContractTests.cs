#nullable enable
using System.IO.Compression;
using System.Text.Json.Nodes;
using Velopack.Chunks;
using Velopack.Core;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack.Tests.Chunks;

/// <summary>
/// The C# client against the real Rust updater (<c>target/debug/update.exe</c>, built by <c>cargo build --features windows</c>)
/// and the vpk-packed <c>ChunkTestApp-*</c> fixtures: the assemble protocol and exit codes, a whole chunk update, and the
/// <c>apply-staged</c> arguments as the real command line parser reads them.
/// </summary>
public class RealUpdaterContractTests
{
    private readonly ITestOutputHelper _output;

    public RealUpdaterContractTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string RealUpdateExe {
        get {
            var path = PathHelper.GetRustAsset("update.exe");
            if (!File.Exists(path)) throw new FileNotFoundException($"Build the rust binaries first (cargo build --features windows): {path}");
            return path;
        }
    }

    /// <summary>Installs a package the way the classic Windows extractor does (§2.2 dispositions).</summary>
    private static void ClassicInstall(string nupkg, string root, bool writeUpdater)
    {
        using var zip = ZipFile.OpenRead(nupkg);
        foreach (var entry in zip.Entries) {
            var name = entry.FullName;
            if (!name.StartsWith("lib/app/", StringComparison.Ordinal) || name.EndsWith("/", StringComparison.Ordinal)) continue;
            var rel = name.Substring("lib/app/".Length);
            string dest;
            if (rel == "Squirrel.exe") {
                if (!writeUpdater) continue;
                dest = Path.Combine(root, "Update.exe");
            } else if (rel.EndsWith("_ExecutionStub.exe", StringComparison.Ordinal)) {
                dest = Path.Combine(root, ChunkIndex.GetStubInstalledName(Path.GetFileName(rel)));
            } else {
                dest = Path.Combine(root, "current", rel.Replace('/', Path.DirectorySeparatorChar));
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            entry.ExtractToFile(dest, true);
        }
    }

    private static string[] Tree(string dir) =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
            .Select(f => f.Substring(dir.Length + 1).Replace('\\', '/'))
            .OrderBy(f => f, StringComparer.Ordinal)
            .ToArray();

    private static VelopackAsset FixtureAsset(string version)
    {
        var feed = CompiledJson.DeserializeVelopackAssetFeed(File.ReadAllText(Path.Combine(DeltaV2Fixtures.FixtureDir, "releases.win.json")))!;
        return feed.Assets.Single(a => a.Version.ToString() == version);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("1.1.0")]
    public void VpkPackedFixtureTrailerMatchesFeedAndEntries(string version)
    {
        var asset = FixtureAsset(version);
        var path = Path.Combine(DeltaV2Fixtures.FixtureDir, asset.FileName);
        var bytes = File.ReadAllBytes(path);
        Assert.Equal(asset.Size, bytes.Length);

        using (var fs = File.OpenRead(path)) {
            var loc = ZipCentralDirectory.FindEntryData(fs, ChunkIndex.TrailerEntryName);
            Assert.NotNull(loc);
            Assert.Equal(asset.ChunkIndexOffset, loc!.Offset);
            Assert.Equal(asset.ChunkIndexLength, loc.Length);
        }
        var recomputed = VelopackAsset.FromNupkgNoChecksum(path);
        Assert.Equal(asset.ChunkIndexOffset, recomputed.ChunkIndexOffset);
        Assert.Equal(asset.ChunkIndexLength, recomputed.ChunkIndexLength);
        Assert.Equal(asset.ChunkIndexSHA256, recomputed.ChunkIndexSHA256);

        var trailer = new byte[asset.ChunkIndexLength!.Value];
        Buffer.BlockCopy(bytes, (int) asset.ChunkIndexOffset!.Value, trailer, 0, trailer.Length);
        Assert.Equal(asset.ChunkIndexSHA256, ChunkInflate.Sha256(trailer).ToHex());
        var index = ChunkIndex.ParseTrailer(trailer, asset.Size);

        // every File record decodes from its B ranges into exactly the zip entry's content
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using (var s = zip.GetEntry("lib/app/" + ChunkIndex.LocalFileName)!.Open()) {
            var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal(index.CopySectionA(), ms.ToArray());
            ChunkIndex.ParseLocal(ms.ToArray());
        }
        for (int i = 0; i < index.FileCount; i++) {
            var f = index.GetFile(i);
            if (f.Kind != ChunkFileKind.File) continue;
            var t = index.GetTransportFile(i);
            var rebuilt = new MemoryStream();
            for (int c = 0; c < f.ChunkCount; c++) {
                var range = index.GetTransportChunk(f.FirstChunk + c);
                var chunk = index.GetChunk(f.FirstChunk + c);
                var plain = ChunkInflate.TryDecodeChunk(bytes, (int) range.Offset, range.Length, t.Method, chunk.Length, chunk.Sha256);
                Assert.True(plain != null, $"{f.Path} chunk {c}");
                rebuilt.Write(plain!, 0, plain!.Length);
            }
            using var es = zip.GetEntry("lib/app/" + f.Path)!.Open();
            var expected = new MemoryStream();
            es.CopyTo(expected);
            Assert.True(expected.ToArray().SequenceEqual(rebuilt.ToArray()), f.Path);
        }
    }

    [Fact]
    public async Task RealAssembleCompletesChunkTestAppUpdate()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "the chunk path runs on Windows only");
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        using var logger = _output.BuildLoggerFor<RealUpdaterContractTests>();
        var log = logger.ToVelopackLogger();

        // 1.0.0 installed classically, with the real updater at the root
        var root = Path.Combine(dir, "root");
        var packages = Path.Combine(root, "packages");
        Directory.CreateDirectory(packages);
        ClassicInstall(Path.Combine(DeltaV2Fixtures.FixtureDir, "ChunkTestApp-1.0.0-win-full.nupkg"), root, writeUpdater: false);
        File.Copy(RealUpdateExe, Path.Combine(root, "Update.exe"));
        Assert.True(File.Exists(Path.Combine(root, "current", ChunkIndex.LocalFileName)));

        // the feed with broken full-package checksums: only the chunk path can succeed
        var feedDir = Path.Combine(dir, "feed");
        Directory.CreateDirectory(feedDir);
        var feed = JsonNode.Parse(File.ReadAllText(Path.Combine(DeltaV2Fixtures.FixtureDir, "releases.win.json")))!;
        foreach (var asset in feed["Assets"]!.AsArray()) {
            var fileName = asset!["FileName"]!.GetValue<string>();
            File.Copy(Path.Combine(DeltaV2Fixtures.FixtureDir, fileName), Path.Combine(feedDir, fileName));
            asset["SHA1"] = new string('0', 40);
            asset["SHA256"] = new string('0', 64);
        }
        File.WriteAllText(Path.Combine(feedDir, "releases.win.json"), feed.ToJsonString());

        var locator = new TestVelopackLocator("ChunkTestApp", "1.0.0", packages, Path.Combine(root, "current"), root,
            Path.Combine(root, "Update.exe"), "win", log);
        var um = new UpdateManager(new SimpleFileSource(new DirectoryInfo(feedDir)), null, locator);
        var info = await um.CheckForUpdatesAsync();
        Assert.NotNull(info);
        var target = info!.TargetFullRelease;
        Assert.Equal("1.1.0", target.Version.ToString());
        Assert.True(target.HasChunkIndex);

        var progress = new List<int>();
        await um.DownloadUpdatesAsync(info, p => {
            lock (progress) progress.Add(p);
        });
        for (int i = 1; i < progress.Count; i++) Assert.True(progress[i] > progress[i - 1], string.Join(",", progress));
        Assert.Equal(100, progress.Last());

        var ready = StagedUpdate.FindReadyStagedFor(locator, target);
        Assert.NotNull(ready);
        Assert.Empty(Directory.EnumerateFiles(packages, "*.nupkg"));
        Assert.Equal(target.ChunkIndexSHA256, um.UpdatePendingRestart?.ChunkIndexSHA256);

        // the real assembler's tree equals the classic extraction of 1.1.0
        var expected = Path.Combine(dir, "expected");
        ClassicInstall(Path.Combine(DeltaV2Fixtures.FixtureDir, "ChunkTestApp-1.1.0-win-full.nupkg"), expected, writeUpdater: true);
        var stagedCurrent = Path.Combine(ready!.Dir, "current");
        var want = Tree(Path.Combine(expected, "current"));
        Assert.Equal(want, Tree(stagedCurrent));
        foreach (var rel in want) {
            Assert.True(
                File.ReadAllBytes(Path.Combine(expected, "current", rel)).SequenceEqual(File.ReadAllBytes(Path.Combine(stagedCurrent, rel))),
                rel);
        }
        Assert.Equal(File.ReadAllBytes(Path.Combine(expected, "Update.exe")), File.ReadAllBytes(Path.Combine(ready.Dir, "Update.exe")));
        Assert.Equal(
            File.ReadAllBytes(Path.Combine(expected, "ChunkTestApp.exe")),
            File.ReadAllBytes(Path.Combine(ready.Dir, "stubs", "ChunkTestApp.exe")));
    }

    /// <summary>Lays out a staged dir for the 1.1.0 fixture (asset.json + index.bin, empty blob) under a root without a local map.</summary>
    private static (string root, string packages, string staged) StageWithoutSources(string dir)
    {
        var root = Path.Combine(dir, "root");
        var packages = Path.Combine(root, "packages");
        var asset = FixtureAsset("1.1.0");
        var nupkg = File.ReadAllBytes(Path.Combine(DeltaV2Fixtures.FixtureDir, asset.FileName));
        var index = new byte[asset.ChunkIndexLength!.Value];
        Buffer.BlockCopy(nupkg, (int) asset.ChunkIndexOffset!.Value, index, 0, index.Length);
        var staged = Path.Combine(packages, "staged", ChunkIndex.StagedIdOf(asset.ChunkIndexSHA256));
        Directory.CreateDirectory(staged);
        File.WriteAllBytes(Path.Combine(staged, "index.bin"), index);
        File.WriteAllText(Path.Combine(staged, "asset.json"), CompiledJson.SerializeVelopackAsset(asset.ToPlainAsset()));
        Directory.CreateDirectory(Path.Combine(root, "current"));
        File.Copy(RealUpdateExe, Path.Combine(root, "Update.exe"));
        using (var zip = ZipFile.OpenRead(Path.Combine(DeltaV2Fixtures.FixtureDir, "ChunkTestApp-1.0.0-win-full.nupkg"))) {
            zip.GetEntry("lib/app/sq.version")!.ExtractToFile(Path.Combine(root, "current", "sq.version"));
        }
        return (root, packages, staged);
    }

    private static string[] AssembleArgs(string root, string packages, string staged, uint? parentPid)
    {
        var args = new List<string> { "--silent", "assemble", "--staged", staged };
        if (parentPid != null) args.AddRange(new[] { "--parentPid", parentPid.Value.ToString() });
        args.AddRange(new[] { "--rootDir", root, "--packageDir", packages });
        return args.ToArray();
    }

    [Fact]
    public async Task RealAssembleExitCodesMatchTheRunner()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "assemble runs on Windows only");
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        using var logger = _output.BuildLoggerFor<RealUpdaterContractTests>();
        var runner = new UpdateExeRunner(logger.ToVelopackLogger());
        var (root, packages, staged) = StageWithoutSources(dir);
        uint pid = (uint) System.Diagnostics.Process.GetCurrentProcess().Id;

        // 3: nothing is sourceable (no local map, empty blob) -> missing.bin with every distinct chunk
        var progress = new List<int>();
        var result = await runner.RunAsync(RealUpdateExe, AssembleArgs(root, packages, staged, pid), progress.Add, CancellationToken.None);
        Assert.Equal(3, result.ExitCode);
        Assert.True(result.SawProtocolLine);
        var index = ChunkIndex.ParseTrailer(File.ReadAllBytes(Path.Combine(staged, "index.bin")), long.MaxValue);
        int distinct = Enumerable.Range(0, index.ChunkCount).Select(index.GetChunkHash).Distinct().Count();
        Assert.Equal(distinct, result.MissingCount);
        Assert.Equal(distinct * 32, new FileInfo(Path.Combine(staged, "missing.bin")).Length);
        Assert.False(File.Exists(Path.Combine(staged, "ready.json")));
        Assert.DoesNotContain(100, progress);

        // 2: missing --parentPid
        result = await runner.RunAsync(RealUpdateExe, AssembleArgs(root, packages, staged, null), null, CancellationToken.None);
        Assert.Equal(2, result.ExitCode);
        Assert.True(result.SawProtocolLine);
        Assert.Contains("parentPid", result.ErrorMessage);

        // 2: index.bin no longer matches asset.json
        var indexPath = Path.Combine(staged, "index.bin");
        var bytes = File.ReadAllBytes(indexPath);
        bytes[bytes.Length - 1] ^= 0xFF;
        File.WriteAllBytes(indexPath, bytes);
        result = await runner.RunAsync(RealUpdateExe, AssembleArgs(root, packages, staged, pid), null, CancellationToken.None);
        Assert.Equal(2, result.ExitCode);
        Assert.NotNull(result.ErrorMessage);

        // an updater without the command: exit 1 and no protocol line (what DownloadUpdatesAsync maps to "unsupported")
        result = await runner.RunAsync(RealUpdateExe, new[] { "--silent", "assemble-unknown-to-this-updater" }, null, CancellationToken.None);
        Assert.Equal(1, result.ExitCode);
        Assert.False(result.SawProtocolLine);
    }

    [Fact]
    public async Task ApplyStagedArgumentsParseWithTheRealUpdater()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "the chunk path runs on Windows only");
        using var env = new ChunkTestEnv(_output);
        await env.DownloadAsync();
        var stagedDir = env.ReadyStaged!.Dir;

        env.Um.WaitExitThenApplyUpdates(env.V2.Asset, silent: true, restart: true, new[] { "--restarted", "x y" });
        env.Locator.ProcessId = 4242;
        env.Um.WaitExitThenApplyUpdates(null, silent: false, restart: false);
        Assert.Equal(2, env.Locator.Started.Count);

        using var _1 = TempUtil.GetTempDirectory(out var emptyRoot);
        var expectations = new[] {
            new[] { "Restart: true", "Wait: NoWait", "Exe Args: Some([\"--restarted\", \"x y\"])" },
            new[] { "Restart: false", "Wait: WaitPid(4242)", "Exe Args: None" },
        };
        for (int i = 0; i < 2; i++) {
            var (_, args, _) = env.Locator.Started[i];
            // the arguments exactly as C# builds them, except that --rootDir points at an empty directory: the real
            // updater parses everything, logs it, then stops at locating the app (nothing is applied, nothing restarted)
            var argv = args.ToArray();
            int rootIdx = Array.IndexOf(argv, "--rootDir");
            Assert.True(rootIdx > 0);
            Assert.Equal(env.Root, argv[rootIdx + 1]);
            argv[rootIdx + 1] = emptyRoot;

            var output = RunAndCapture(RealUpdateExe, argv, out int exitCode);
            _output.WriteLine(output);
            Assert.Equal(1, exitCode);
            Assert.Contains("Command: Apply-staged", output);
            Assert.Contains("Staged: Some(" + JsonEscapedDebugPath(stagedDir) + ")", output);
            foreach (var expected in expectations[i]) Assert.Contains(expected, output);
            Assert.DoesNotContain("No known subcommand", output);
            Assert.Contains("Root Directory: Some(" + JsonEscapedDebugPath(emptyRoot) + ")", output);
        }
    }

    /// <summary>Rust's <c>{:?}</c> of a path: quoted, with backslashes escaped.</summary>
    private static string JsonEscapedDebugPath(string path) => "\"" + path.Replace("\\", "\\\\") + "\"";

    private static string RunAndCapture(string exe, IEnumerable<string> args, out int exitCode)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(exe) {
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = System.Diagnostics.Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        Assert.True(p.WaitForExit(60_000), "update.exe did not exit");
        exitCode = p.ExitCode;
        return stdout + stderr.Result;
    }
}
