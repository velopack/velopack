#nullable enable
using System.Text;
using Velopack.Chunks;
using Velopack.Core;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack.Tests.Chunks;

/// <summary>An in-memory <see cref="IRangeReader"/> over a byte array with fault and corruption hooks.</summary>
internal sealed class MemoryRangeReader : IRangeReader
{
    private readonly byte[] _data;
    public int Reads;
    public long BytesRead;
    public List<long> Offsets { get; } = new();

    /// <summary>Returns an exception to throw for a read of (offset, count), or null to serve it.</summary>
    public Func<long, int, Exception?>? Fault { get; set; }

    /// <summary>Lets a test corrupt the bytes served for (offset, buffer, bufferOffset, count).</summary>
    public Action<long, byte[], int, int>? Mutate { get; set; }

    public MemoryRangeReader(byte[] data)
    {
        _data = data;
    }

    public long Length => _data.Length;

    public Task ReadExactAsync(long offset, byte[] buffer, int bufferOffset, int count, CancellationToken cancelToken)
    {
        cancelToken.ThrowIfCancellationRequested();
        var ex = Fault?.Invoke(offset, count);
        if (ex != null) throw ex;
        if (offset < 0 || offset + count > _data.Length) throw new RangeUnavailableException("range outside the package");
        Buffer.BlockCopy(_data, (int) offset, buffer, bufferOffset, count);
        Mutate?.Invoke(offset, buffer, bufferOffset, count);
        Interlocked.Increment(ref Reads);
        Interlocked.Add(ref BytesRead, count);
        lock (Offsets) Offsets.Add(offset);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
    }
}

/// <summary>A source serving one feed and one package from memory, with ranged access.</summary>
internal class FakeChunkSource : IUpdateSource, IRangeReadableUpdateSource
{
    public VelopackAssetFeed Feed { get; set; } = new();
    public byte[] PackageBytes { get; set; } = Array.Empty<byte>();
    public bool RangesSupported { get; set; } = true;
    public bool FullDownloadFails { get; set; }
    public int FullDownloads { get; private set; }
    public int ReadersOpened { get; private set; }
    public MemoryRangeReader? Reader { get; private set; }
    public Func<long, int, Exception?>? RangeFault { get; set; }
    public Action<long, byte[], int, int>? RangeMutate { get; set; }

    public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null,
        VelopackAsset? latestLocalRelease = null)
    {
        return Task.FromResult(Feed);
    }

    public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress,
        CancellationToken cancelToken = default)
    {
        FullDownloads++;
        if (FullDownloadFails) throw new IOException("the full download failed (test)");
        File.WriteAllBytes(localFile, PackageBytes);
        progress(50);
        progress(100);
        return Task.CompletedTask;
    }

    public Task<IRangeReader?> OpenRangeReaderAsync(IVelopackLogger logger, VelopackAsset asset, CancellationToken cancelToken)
    {
        if (!RangesSupported) return Task.FromResult<IRangeReader?>(null);
        ReadersOpened++;
        Reader = new MemoryRangeReader(PackageBytes) { Fault = RangeFault, Mutate = RangeMutate };
        return Task.FromResult<IRangeReader?>(Reader);
    }
}

/// <summary>Wraps a source so it exposes no ranged access at all.</summary>
internal sealed class FullOnlySource : IUpdateSource
{
    private readonly IUpdateSource _inner;

    public FullOnlySource(IUpdateSource inner)
    {
        _inner = inner;
    }

    public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null,
        VelopackAsset? latestLocalRelease = null) => _inner.GetReleaseFeed(logger, appId, channel, stagingId, latestLocalRelease);

    public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress,
        CancellationToken cancelToken = default) => _inner.DownloadReleaseEntry(logger, releaseEntry, localFile, progress, cancelToken);
}

/// <summary>
/// Plays <c>Update.exe assemble</c> for <see cref="UpdateManager"/> tests: records every invocation, and by default
/// behaves like the real assembler as far as the tests can observe it (verifies that every remote chunk is sourceable
/// from the blob or the installed view, reports the rest in <c>missing.bin</c>, otherwise writes the staged tree,
/// the sidecar updater, <c>sq.chunks</c> and <c>ready.json</c>).
/// </summary>
internal sealed class FakeAssembler
{
    public TestPackage Remote { get; set; }
    public List<IReadOnlyList<string>> Calls { get; } = new();
    public List<string> UpdaterHashesAtCall { get; } = new();

    /// <summary>Scripted results consumed per call; a null entry (or an exhausted queue) runs the default behaviour.</summary>
    public Queue<Func<string, UpdateExeRunResult?>> Script { get; } = new();

    /// <summary>When set, the sidecar written by the default behaviour does not match the marker.</summary>
    public bool CorruptSidecar { get; set; }

    public FakeAssembler(TestPackage remote)
    {
        Remote = remote;
    }

    public Task<UpdateExeRunResult> RunAsync(string exe, IReadOnlyList<string> args, Action<int> progress, CancellationToken ct)
    {
        Calls.Add(args.ToList());
        UpdaterHashesAtCall.Add(File.Exists(exe) ? ChunkInflate.Sha256File(exe).ToHex() : "");
        var stagedDir = args[args.ToList().IndexOf("--staged") + 1];
        var rootDir = args[args.ToList().IndexOf("--rootDir") + 1];
        if (Script.Count > 0) {
            var scripted = Script.Dequeue()(stagedDir);
            if (scripted != null) return Task.FromResult(scripted);
        }
        return Task.FromResult(Assemble(stagedDir, rootDir, progress));
    }

    private UpdateExeRunResult Assemble(string stagedDir, string rootDir, Action<int> progress)
    {
        var staged = new StagedUpdate(stagedDir, Path.GetFileName(stagedDir));
        var asset = CompiledJson.DeserializeVelopackAsset(File.ReadAllText(staged.AssetPath))!;
        var indexBytes = File.ReadAllBytes(staged.IndexPath);
        if (ChunkInflate.Sha256(indexBytes).ToHex() != asset.ChunkIndexSHA256) {
            return new UpdateExeRunResult(2, true, null, "index digest mismatch");
        }
        var remote = ChunkIndex.ParseTrailer(indexBytes, asset.Size);
        var currentDir = Path.Combine(rootDir, "current");
        var local = ChunkIndex.TryReadLocal(Path.Combine(currentDir, ChunkIndex.LocalFileName));
        var localRoots = new LocalRoots(currentDir, Path.Combine(rootDir, "Update.exe"), rootDir);
        var localChunks = new Dictionary<Hash256, (string path, long offset, int length)>();
        if (local != null) {
            for (int i = 0; i < local.FileCount; i++) {
                var f = local.GetFile(i);
                if (!f.HasChunks) continue;
                var path = localRoots.Resolve(f);
                long pos = 0;
                for (int c = 0; c < f.ChunkCount; c++) {
                    var chunk = local.GetChunk(f.FirstChunk + c);
                    if (!localChunks.ContainsKey(chunk.Sha256)) localChunks[chunk.Sha256] = (path, pos, chunk.Length);
                    pos += chunk.Length;
                }
            }
        }

        File.Delete(staged.ReadyPath);
        var missing = new List<Hash256>();
        using (var blob = ChunkBlob.Open(staged.BlobPath)) {
            for (int c = 0; c < remote.ChunkCount; c++) {
                var hash = remote.GetChunkHash(c);
                if (blob.Read(hash) != null) continue;
                if (localChunks.TryGetValue(hash, out var loc) && LocalChunkMatches(loc.path, loc.offset, loc.length, hash)) continue;
                missing.Add(hash);
            }
        }
        progress(10);
        if (missing.Count > 0) {
            var bytes = new byte[missing.Count * Hash256.Size];
            for (int i = 0; i < missing.Count; i++) missing[i].CopyTo(bytes, i * Hash256.Size);
            File.WriteAllBytes(staged.MissingPath, bytes);
            return new UpdateExeRunResult(3, true, missing.Count, null);
        }

        // every chunk is sourceable: write the tree
        Directory.CreateDirectory(staged.CurrentDir);
        Directory.CreateDirectory(staged.StubsDir);
        byte[]? updater = null;
        for (int i = 0; i < remote.FileCount; i++) {
            var f = remote.GetFile(i);
            var content = Remote.Files[f.Path];
            string dest;
            switch (f.Disposition) {
            case ChunkFileDisposition.Updater:
                dest = staged.UpdaterPath;
                updater = content;
                break;
            case ChunkFileDisposition.Stub:
                dest = Path.Combine(staged.StubsDir, ChunkIndex.GetStubInstalledName(f.Path));
                break;
            default:
                dest = Path.Combine(staged.CurrentDir, f.Path.Replace('/', Path.DirectorySeparatorChar));
                break;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, content);
        }
        File.WriteAllBytes(Path.Combine(staged.CurrentDir, ChunkIndex.LocalFileName), remote.CopySectionA());
        progress(90);

        var updaterHash = ChunkInflate.Sha256(updater ?? Array.Empty<byte>()).ToHex();
        if (CorruptSidecar) File.WriteAllBytes(staged.UpdaterPath, new byte[] { 1, 2, 3 });
        staged.WriteReadyMarker(new StagedReadyMarker {
            Format = StagedReadyMarker.CurrentFormat,
            StagedId = staged.Id,
            IndexSHA256 = asset.ChunkIndexSHA256,
            UpdaterSHA256 = updaterHash,
            Asset = asset,
            CreatedUtc = DateTime.UtcNow.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'"),
        });
        progress(100);
        return new UpdateExeRunResult(0, true, null, null);
    }

    private static bool LocalChunkMatches(string path, long offset, int length, Hash256 hash)
    {
        try {
            using var fs = File.OpenRead(path);
            if (fs.Length < offset + length) return false;
            fs.Seek(offset, SeekOrigin.Begin);
            return ChunkInflate.Sha256(fs, length) == hash;
        } catch (Exception) {
            return false;
        }
    }
}

/// <summary>A test locator which records launched processes instead of starting them.</summary>
internal sealed class RecordingLocator : TestVelopackLocator
{
    private sealed class RecordingProcess : IProcessImpl
    {
        private readonly RecordingLocator _owner;

        public RecordingProcess(RecordingLocator owner)
        {
            _owner = owner;
        }

        public string GetCurrentProcessPath() => _owner.ProcessPath ?? throw new NotSupportedException();
        public uint GetCurrentProcessId() => _owner.ProcessId;

        public void StartProcess(string exePath, IEnumerable<string> args, string workDir, bool showWindow)
        {
            if (_owner.StartFault != null) throw _owner.StartFault;
            _owner.Started.Add((exePath, args.ToList(), workDir));
        }

        public void Exit(int exitCode) => _owner.Exit(exitCode);
    }

    public List<(string exe, List<string> args, string workDir)> Started { get; } = new();
    public uint ProcessId { get; set; }
    public string? ProcessPath { get; set; }

    /// <summary>When set, every <c>StartProcess</c> throws this (a launch failure).</summary>
    public Exception? StartFault { get; set; }

    public RecordingLocator(string appId, string version, string packagesDir, string? appDir, string? rootDir, string? updateExe,
        IVelopackLogger logger)
        : base(appId, version, packagesDir, appDir, rootDir, updateExe, null, logger)
    {
    }

    public override IProcessImpl Process => new RecordingProcess(this);
}

/// <summary>
/// An installed v1 under a temp root, a remote v2 package, and an <see cref="UpdateManager"/> wired to fakes for the
/// range source and the assembler, with the retry policy's sleep removed.
/// </summary>
internal sealed class ChunkTestEnv : IDisposable
{
    private readonly IDisposable _tempDir;
    private readonly IDisposable _logger;

    public string Root { get; }
    public string Current => Path.Combine(Root, "current");
    public string Packages { get; }
    public string UpdateExe { get; }
    public TestPackage V1 { get; }
    public TestPackage V2 { get; private set; }
    public RecordingLocator Locator { get; }
    public FakeChunkSource Source { get; }
    public FakeAssembler Assembler { get; }
    public UpdateManager Um { get; }
    public List<int> Progress { get; } = new();
    public List<TimeSpan> Sleeps { get; } = new();
    public IVelopackLogger Log { get; }

    public string StagedRoot => Path.Combine(Packages, "staged");
    public string StagedDir => Path.Combine(StagedRoot, V2.StagedId);
    public string NupkgPath => Path.Combine(Packages, V2.Asset.FileName);

    public ChunkTestEnv(ITestOutputHelper output, UpdateOptions? options = null, Func<FakeChunkSource, IUpdateSource>? wrapSource = null,
        bool installLocalIndex = true, bool useFallbackUpdateExe = false, TestPackage? remote = null)
    {
        _tempDir = TempUtil.GetTempDirectory(out var dir);
        var logger = output.BuildLoggerFor<ChunkTestEnv>();
        _logger = logger;
        Log = logger.ToVelopackLogger();

        Root = Path.Combine(dir, "root");
        Packages = Path.Combine(Root, "packages");
        Directory.CreateDirectory(Packages);
        V1 = BuildV1();
        V2 = remote ?? BuildV2(V1);
        V1.Install(Root);
        if (!installLocalIndex) File.Delete(Path.Combine(Current, ChunkIndex.LocalFileName));

        UpdateExe = Path.Combine(Root, "Update.exe");
        if (useFallbackUpdateExe) {
            // the LocalAppData-style fallback copy: a different (stale) updater than the one vendored in root
            var fallbackDir = Path.Combine(dir, "fallback");
            Directory.CreateDirectory(fallbackDir);
            UpdateExe = Path.Combine(fallbackDir, "Update.exe");
            File.WriteAllBytes(UpdateExe, TestPackageBuilder.Random(77, 1000));
        }

        Locator = new RecordingLocator("ChunkApp", "1.0.0", Packages, Current, Root, UpdateExe, Log);
        Source = new FakeChunkSource { Feed = new VelopackAssetFeed { Assets = new[] { V2.Asset } }, PackageBytes = V2.Bytes };
        Assembler = new FakeAssembler(V2);
        Um = new UpdateManager(wrapSource != null ? wrapSource(Source) : Source, options, Locator) {
            AssembleRunner = Assembler.RunAsync,
            ChunkRetryPolicy = new RetryPolicy(randomSeed: 7) {
                Sleep = (d, _) => {
                    lock (Sleeps) Sleeps.Add(d);
                    return Task.CompletedTask;
                },
            },
        };
    }

    /// <summary>Replaces the remote package (feed + bytes + assembler).</summary>
    public void SetRemote(TestPackage remote)
    {
        V2 = remote;
        Source.Feed = new VelopackAssetFeed { Assets = new[] { remote.Asset } };
        Source.PackageBytes = remote.Bytes;
        Assembler.Remote = remote;
    }

    public static TestPackage BuildV1()
    {
        var files = new Dictionary<string, byte[]> {
            ["Squirrel.exe"] = TestPackageBuilder.Text(1, 300_000),
            ["App.exe"] = TestPackageBuilder.Text(2, 900_000),
            ["lib/Core.dll"] = TestPackageBuilder.Text(3, 1_200_000),
            ["data/blob.bin"] = TestPackageBuilder.Random(4, 500_000),
            ["sq.version"] = Encoding.UTF8.GetBytes("<package><id>ChunkApp</id><version>1.0.0</version></package>"),
            ["ChunkApp_ExecutionStub.exe"] = TestPackageBuilder.Random(5, 40_000),
        };
        return TestPackageBuilder.Build(files, version: "1.0.0");
    }

    public static TestPackage BuildV2(TestPackage v1)
    {
        var app = v1.Files["App.exe"];
        var modified = new byte[app.Length + 1000];
        Buffer.BlockCopy(app, 0, modified, 0, 200_000);
        Buffer.BlockCopy(TestPackageBuilder.Text(9, 1000), 0, modified, 200_000, 1000);
        Buffer.BlockCopy(app, 200_000, modified, 201_000, app.Length - 200_000);
        var files = new Dictionary<string, byte[]> {
            ["Squirrel.exe"] = TestPackageBuilder.Text(11, 310_000),
            ["App.exe"] = modified,
            ["lib/Core.dll"] = v1.Files["lib/Core.dll"],
            ["lib/New.dll"] = TestPackageBuilder.Text(6, 100_000),
            ["data/blob.bin"] = v1.Files["data/blob.bin"],
            ["sq.version"] = Encoding.UTF8.GetBytes("<package><id>ChunkApp</id><version>1.1.0</version></package>"),
            ["ChunkApp_ExecutionStub.exe"] = v1.Files["ChunkApp_ExecutionStub.exe"],
        };
        return TestPackageBuilder.Build(files, version: "1.1.0");
    }

    public UpdateInfo Check()
    {
        var info = Um.CheckForUpdates();
        Assert.NotNull(info);
        return info!;
    }

    public Task DownloadAsync(CancellationToken ct = default)
    {
        return Um.DownloadUpdatesAsync(Check(), p => {
            lock (Progress) Progress.Add(p);
        }, ct);
    }

    /// <summary>Reported progress never decreased (and, when <paramref name="complete"/>, ended at 100).</summary>
    public void AssertProgressMonotonic(bool complete)
    {
        lock (Progress) {
            for (int i = 1; i < Progress.Count; i++) {
                Assert.True(Progress[i] > Progress[i - 1], $"progress went from {Progress[i - 1]} to {Progress[i]}: [{string.Join(",", Progress)}]");
            }
            if (complete) Assert.Equal(100, Progress.LastOrDefault());
        }
    }

    public StagedReady? ReadyStaged => StagedUpdate.TryReadReady(StagedDir);

    public void Dispose()
    {
        _logger.Dispose();
        _tempDir.Dispose();
    }
}
