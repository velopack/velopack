using System.IO.Compression;
using System.Text;
using Velopack.Chunks;
using Velopack.Core;
using Velopack.Packaging.Chunking;
using Velopack.TestCommon;
using Velopack.Util;

namespace Velopack.Packaging.Tests.Chunking;

public class ChunkIndexWriterTests
{
    private readonly ITestOutputHelper _output;

    public ChunkIndexWriterTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void IndexRoundTripsThroughLibCsharpReader()
    {
        using var _1 = TempUtil.GetTempDirectory(out var packDir);
        ChunkTestTree.CreateDefaultPackDir(packDir);
        var index = ChunkIndexBuilder.Build(packDir);
        ChunkIndexBuilder.WriteSqChunks(index, packDir);

        var sectionA = File.ReadAllBytes(Path.Combine(packDir, "sq.chunks"));
        Assert.Equal(index.SerializeSectionA(), sectionA);
        Assert.Equal(index.SectionALength, sectionA.Length);

        var parsed = ChunkIndex.ParseLocal(sectionA);
        Assert.Equal(index.Files.Count, parsed.FileCount);
        Assert.Equal(index.Chunks.Count, parsed.ChunkCount);
        Assert.Equal(ChunkIndex.CdcAlgoFastCdcGearV1, parsed.CdcAlgo);
        Assert.Equal((uint) FastCdc.MinSize, parsed.CdcMin);
        Assert.Equal((uint) FastCdc.AvgSize, parsed.CdcAvg);
        Assert.Equal((uint) FastCdc.MaxSize, parsed.CdcMax);

        for (int i = 0; i < index.Files.Count; i++) {
            var expected = index.Files[i];
            var actual = parsed.GetFile(i);
            Assert.Equal(expected.Path, actual.Path);
            Assert.Equal((byte) expected.Disposition, (byte) actual.Disposition);
            Assert.Equal(ChunkFileKind.File, actual.Kind);
            Assert.Equal(expected.Length, actual.Length);
            Assert.Equal(expected.FirstChunk, actual.FirstChunk);
            Assert.Equal(expected.ChunkCount, actual.ChunkCount);
            Assert.Equal(0, actual.Mode);
            Assert.Null(actual.SymlinkTarget);
            Assert.Equal(Hash256.FromBytes(expected.Sha256), actual.Sha256);
            Assert.Equal(i, parsed.FindFile(expected.Path));

            // the hashes describe the real file contents
            var bytes = File.ReadAllBytes(Path.Combine(packDir, expected.Path));
            Assert.Equal(ChunkTestTree.Sha256(bytes), expected.Sha256);
            long offset = 0;
            for (int c = 0; c < expected.ChunkCount; c++) {
                var chunk = parsed.GetChunk(expected.FirstChunk + c);
                Assert.Equal(index.Chunks[expected.FirstChunk + c].Length, chunk.Length);
                Assert.Equal(ChunkInflate.Sha256(bytes, (int) offset, chunk.Length), chunk.Sha256);
                offset += chunk.Length;
            }

            Assert.Equal(expected.Length, offset);
        }
    }

    [Fact]
    public void DispositionsAssigned()
    {
        using var _1 = TempUtil.GetTempDirectory(out var packDir);
        ChunkTestTree.CreateDefaultPackDir(packDir);
        var index = ChunkIndexBuilder.Build(packDir);

        Assert.Equal(PackFileDisposition.Updater, index.FindFile("Squirrel.exe")!.Disposition);
        Assert.Equal(PackFileDisposition.Stub, index.FindFile("MyApp_ExecutionStub.exe")!.Disposition);
        Assert.Equal(PackFileDisposition.AppFile, index.FindFile("MyApp.exe")!.Disposition);
        Assert.Equal(PackFileDisposition.AppFile, index.FindFile("sq.version")!.Disposition);
        Assert.Equal(PackFileDisposition.AppFile, index.FindFile("data/nested/deep.bin")!.Disposition);
        Assert.Single(index.Files, f => f.Disposition == PackFileDisposition.Updater);
        Assert.Single(index.Files, f => f.Disposition == PackFileDisposition.Stub);

        // records are in ordinal UTF-8 order
        var paths = index.Files.Select(f => f.Path).ToList();
        Assert.Equal(paths.OrderBy(p => Encoding.UTF8.GetBytes(p), new ByteArrayComparer()).ToList(), paths);
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public int Compare(byte[] x, byte[] y) => PackContentIndex.CompareBytes(x, y);
    }

    [Fact]
    public void SqVersionEqualsNuspec()
    {
        // a real pack through WindowsPackCommandRunner: the installed sq.version must be byte-identical to the .nuspec
        Assert.SkipUnless(TestApp.CanPackWindowsTarget(RID.Parse("win-x64")), "Windows target binaries are not available on this host");
        using var logger = _output.BuildLoggerFor<ChunkIndexWriterTests>();
        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = TempUtil.GetTempDirectory(out var unzipDir);

        var exe = "testapp.exe";
        PathHelper.CopyRustAssetTo(exe, tmpOutput);
        var options = new Velopack.Packaging.Windows.Commands.WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = "SqVersionTest",
            PackVersion = "1.2.3",
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = tmpOutput,
            NoInst = true,
            SkipVelopackAppCheck = true,
        };
        var console = new Velopack.Vpk.Logging.BasicConsole(logger, new Velopack.Vpk.VelopackDefaults(false));
        new Velopack.Packaging.Windows.Commands.WindowsPackCommandRunner(logger, console).Run(options).GetAwaiterResult();

        var nupkg = Path.Combine(tmpReleaseDir, "SqVersionTest-1.2.3-full.nupkg");
        using var zip = ZipFile.OpenRead(nupkg);
        var nuspec = ReadEntry(zip, "SqVersionTest.nuspec");
        var sqVersion = ReadEntry(zip, "lib/app/sq.version");
        Assert.Equal(nuspec, sqVersion);
        Assert.Contains("<machineArchitecture>x64</machineArchitecture>", Encoding.UTF8.GetString(sqVersion));

        // and the index record for sq.version describes exactly those bytes
        var trailerEntry = zip.GetEntry(ChunkIndex.TrailerEntryName)!;
        using var ts = trailerEntry.Open();
        var trailer = new byte[trailerEntry.Length];
        ts.ReadExactly(trailer);
        var index = ChunkIndex.ParseTrailer(trailer, new FileInfo(nupkg).Length);
        var record = index.GetFile(index.FindFile("sq.version"));
        Assert.Equal(ChunkInflate.Sha256(sqVersion), record.Sha256);
    }

    private static byte[] ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        Assert.NotNull(entry);
        using var s = entry.Open();
        var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    [Theory]
    [InlineData("bad name.", "ends with '.'")]
    [InlineData("data/CON.txt", "reserved name")]
    [InlineData("data/a|b.txt", "illegal character")]
    [InlineData("SQ.CHUNKS", "may not be a record")]
    public void PathSafetyViolationFailsPack(string path, string reason)
    {
        var ex = Assert.Throws<UserInfoException>(() => ChunkIndexBuilder.AssignDispositions(["MyApp.exe", path]));
        Assert.Contains(path, ex.Message);
        Assert.Contains(reason, ex.Message);

        // the same violation on disk (where the file system allows the name)
        using var _1 = TempUtil.GetTempDirectory(out var packDir);
        ChunkTestTree.WriteFile(packDir, "MyApp.exe", new byte[10]);
        try {
            ChunkTestTree.WriteFile(packDir, path, new byte[10]);
        } catch (Exception) {
            return; // the host file system refuses the name; the list-based check above covers the rule
        }

        // Windows silently strips a trailing '.' from file names; only assert when the name really exists as given
        var onDisk = Directory.EnumerateFiles(packDir, "*", SearchOption.AllDirectories)
            .Select(f => Path.GetRelativePath(packDir, f).Replace('\\', '/'));
        if (!onDisk.Contains(path, StringComparer.Ordinal)) return;
        var ex2 = Assert.Throws<UserInfoException>(() => ChunkIndexBuilder.Build(packDir));
        Assert.Contains(reason, ex2.Message);
    }

    [Fact]
    public void NestedSquirrelExeFailsPack()
    {
        using var _1 = TempUtil.GetTempDirectory(out var packDir);
        ChunkTestTree.CreateDefaultPackDir(packDir);
        ChunkTestTree.WriteFile(packDir, "Plugins/Squirrel.exe", new byte[10]);
        var ex = Assert.Throws<UserInfoException>(() => ChunkIndexBuilder.Build(packDir));
        Assert.Contains("Plugins/Squirrel.exe", ex.Message);

        // the suffix match is what old extractors use, so a top-level decoy is rejected too
        Assert.Throws<UserInfoException>(() => ChunkIndexBuilder.AssignDispositions(["NotSquirrel.exe"]));
        // ...but only case-sensitively (the extractor's ends_with is case-sensitive)
        Assert.Equal([PackFileDisposition.AppFile], ChunkIndexBuilder.AssignDispositions(["Plugins/squirrel.exe"]));
    }

    [Fact]
    public void NestedStubFailsPack()
    {
        using var _1 = TempUtil.GetTempDirectory(out var packDir);
        ChunkTestTree.CreateDefaultPackDir(packDir);
        ChunkTestTree.WriteFile(packDir, "tools/Other_ExecutionStub.exe", new byte[10]);
        var ex = Assert.Throws<UserInfoException>(() => ChunkIndexBuilder.Build(packDir));
        Assert.Contains("tools/Other_ExecutionStub.exe", ex.Message);

        Assert.Equal([PackFileDisposition.AppFile], ChunkIndexBuilder.AssignDispositions(["tools/Other_executionstub.exe"]));
    }

    [Fact]
    public void SymlinkInPackDirFailsPack()
    {
        using var _1 = TempUtil.GetTempDirectory(out var packDir);
        ChunkTestTree.CreateDefaultPackDir(packDir);
        var link = Path.Combine(packDir, "data", "link.dll");
        try {
            File.CreateSymbolicLink(link, Path.Combine(packDir, "MyApp.dll"));
        } catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) {
            Assert.Skip("Creating symbolic links requires Developer Mode or elevation: " + ex.Message);
        }

        var ex2 = Assert.Throws<UserInfoException>(() => ChunkIndexBuilder.Build(packDir));
        Assert.Contains("data/link.dll", ex2.Message);
    }

    [Fact]
    public void CaseInsensitiveDuplicateFailsPack()
    {
        var ex = Assert.Throws<UserInfoException>(() => ChunkIndexBuilder.AssignDispositions(["lib/Foo.dll", "lib/foo.DLL"]));
        Assert.Contains("differ only by case", ex.Message);

        // paths that differ by more than ASCII case are accepted by this rule
        ChunkIndexBuilder.AssignDispositions(["lib/Foo.dll", "lib/Foo2.dll"]);

        if (!VelopackRuntimeInfo.IsWindows) {
            using var _1 = TempUtil.GetTempDirectory(out var packDir);
            ChunkTestTree.WriteFile(packDir, "Foo.dll", new byte[10]);
            ChunkTestTree.WriteFile(packDir, "foo.dll", new byte[10]);
            Assert.Throws<UserInfoException>(() => ChunkIndexBuilder.Build(packDir));
        }
    }

    [Fact]
    public void SqChunksExcludedFromIndex()
    {
        using var _1 = TempUtil.GetTempDirectory(out var packDir);
        ChunkTestTree.CreateDefaultPackDir(packDir);
        var first = ChunkIndexBuilder.Build(packDir);
        ChunkIndexBuilder.WriteSqChunks(first, packDir);
        Assert.True(File.Exists(Path.Combine(packDir, "sq.chunks")));

        // re-indexing a pack dir that already holds sq.chunks (eg. a second pack) ignores it and yields the same index
        var second = ChunkIndexBuilder.Build(packDir);
        Assert.Null(second.FindFile("sq.chunks"));
        Assert.Equal(first.SerializeSectionA(), second.SerializeSectionA());

        // a nested file of that name is an ordinary app file
        ChunkTestTree.WriteFile(packDir, "data/sq.chunks", new byte[10]);
        Assert.NotNull(ChunkIndexBuilder.Build(packDir).FindFile("data/sq.chunks"));
    }

    [Fact]
    public void OnlyFileRecordsEmitted()
    {
        using var _1 = TempUtil.GetTempDirectory(out var packDir);
        ChunkTestTree.CreateDefaultPackDir(packDir);
        Directory.CreateDirectory(Path.Combine(packDir, "emptydir"));
        Directory.CreateDirectory(Path.Combine(packDir, "data", "another-empty", "nested-empty"));

        var index = ChunkIndexBuilder.Build(packDir);
        var files = Directory.EnumerateFiles(packDir, "*", SearchOption.AllDirectories).Count();
        Assert.Equal(files, index.Files.Count);
        Assert.DoesNotContain(index.Files, f => f.Path.Contains("empty", StringComparison.OrdinalIgnoreCase) && f.Path != "empty.txt");

        var parsed = ChunkIndex.ParseLocal(index.SerializeSectionA());
        for (int i = 0; i < parsed.FileCount; i++) {
            Assert.Equal(ChunkFileKind.File, parsed.GetFileKind(i));
        }

        var empty = index.FindFile("empty.txt")!;
        Assert.Equal(0, empty.Length);
        Assert.Equal(0, empty.ChunkCount);
        Assert.Equal(0, empty.FirstChunk);
        Assert.Equal(0u, empty.Crc32);
        Assert.Equal(ChunkTestTree.Sha256(Array.Empty<byte>()), empty.Sha256);
    }

    [Fact]
    public void Crc32MatchesZipHeader()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packed = ChunkTestTree.PackDefault(workDir);
        using var zip = ZipFile.OpenRead(packed.NupkgPath);
        foreach (var file in packed.Index.Files) {
            var entry = zip.GetEntry("lib/app/" + file.Path);
            Assert.NotNull(entry);
            Assert.Equal(file.Crc32, entry.Crc32);
            Assert.Equal(file.Length, entry.Length);
            Assert.Equal(System.IO.Hashing.Crc32.HashToUInt32(File.ReadAllBytes(Path.Combine(packed.PackDir, file.Path))), file.Crc32);
        }
    }
}
