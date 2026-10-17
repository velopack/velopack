#nullable enable
using System.Diagnostics;
using System.Text;
using Velopack.Chunks;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack.Tests.Chunks;

public class ChunkIndexTests
{
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(DeltaV2Fixtures.FixtureDir, name));

    public static IEnumerable<object[]> InvalidFixtures()
    {
        return Directory.GetFiles(DeltaV2Fixtures.FixtureDir, "index_invalid_*.bin").Select(f => new object[] { Path.GetFileName(f) });
    }

    [Fact]
    public void ParsesSample()
    {
        var trailer = Fixture("index_sample.bin");
        var idx = ChunkIndex.ParseTrailer(trailer, long.MaxValue);
        Assert.True(idx.HasTransportMap);
        Assert.Equal(8, idx.FileCount);
        Assert.Equal(8, idx.ChunkCount);
        Assert.Equal(ChunkIndex.CdcAlgoFastCdcGearV1, idx.CdcAlgo);
        Assert.Equal(16384u, idx.CdcMin);
        Assert.Equal(65536u, idx.CdcAvg);
        Assert.Equal(262144u, idx.CdcMax);
        Assert.Equal(trailer.Length, idx.TotalLength);

        var paths = Enumerable.Range(0, idx.FileCount).Select(idx.GetPath).ToArray();
        Assert.Equal(new[] {
            "MyApp.exe", "MyApp_ExecutionStub.exe", "Squirrel.exe", "data/empty.txt", "data/link.txt", "plugins", "sq.version", "Übersicht/ü.txt",
        }, paths);

        var app = idx.GetFile(0);
        Assert.Equal(ChunkFileDisposition.AppFile, app.Disposition);
        Assert.Equal(ChunkFileKind.File, app.Kind);
        Assert.Equal(150_000, app.Length);
        Assert.Equal(0, app.FirstChunk);
        Assert.Equal(2, app.ChunkCount);
        Assert.True(app.HasChunks);
        Assert.Null(app.SymlinkTarget);
        Assert.Equal(ChunkInflate.Sha256(TestIndexBuilder.DeterministicBytes(13, 150_000)), app.Sha256);
        Assert.Equal(100_000, idx.GetChunk(0).Length);
        Assert.Equal(50_000, idx.GetChunk(1).Length);
        Assert.Equal(ChunkInflate.Sha256(TestIndexBuilder.DeterministicBytes(13, 150_000), 100_000, 50_000), idx.GetChunkHash(1));
        var appTransport = idx.GetTransportFile(0);
        Assert.True(appTransport.IsDeflate);
        Assert.Equal(idx.GetTransportChunk(0).Offset, appTransport.DataOffset);
        Assert.Equal(idx.GetTransportChunk(0).Offset + idx.GetTransportChunk(0).Length, idx.GetTransportChunk(1).Offset);
        Assert.Equal(appTransport.DataOffset + appTransport.CompressedSize - 2, idx.GetTransportChunk(1).Offset + idx.GetTransportChunk(1).Length);

        var stub = idx.GetFile(1);
        Assert.Equal(ChunkFileDisposition.Stub, stub.Disposition);
        Assert.Equal("MyApp.exe", ChunkIndex.GetStubInstalledName(stub.Path));
        var stubTransport = idx.GetTransportFile(1);
        Assert.Equal(ChunkIndex.MethodStored, stubTransport.Method);
        Assert.Equal(30_000, stubTransport.CompressedSize);
        Assert.Equal(stubTransport.DataOffset, idx.GetTransportChunk(2).Offset);
        Assert.Equal(30_000, idx.GetTransportChunk(2).Length);

        var updater = idx.GetFile(2);
        Assert.Equal(ChunkFileDisposition.Updater, updater.Disposition);
        Assert.Equal(3, updater.FirstChunk);
        Assert.Equal(3, updater.ChunkCount);

        var empty = idx.GetFile(3);
        Assert.Equal(ChunkFileKind.File, empty.Kind);
        Assert.Equal(0, empty.Length);
        Assert.Equal(0, empty.ChunkCount);
        Assert.False(empty.HasChunks);
        Assert.Equal(0, idx.GetTransportFile(3).CompressedSize);

        var link = idx.GetFile(4);
        Assert.Equal(ChunkFileKind.Symlink, link.Kind);
        Assert.Equal("../MyApp.exe", link.SymlinkTarget);
        Assert.Equal(Encoding.UTF8.GetByteCount("../MyApp.exe"), link.Length);

        var dir = idx.GetFile(5);
        Assert.Equal(ChunkFileKind.EmptyDir, dir.Kind);
        Assert.Equal(0, dir.Length);

        Assert.Equal(7, idx.GetFile(7).FirstChunk);
        Assert.Equal(5000, idx.GetFileLength(7));

        Assert.Equal(2, idx.FindFile("Squirrel.exe"));
        Assert.Equal(7, idx.FindFile("Übersicht/ü.txt"));
        Assert.Equal(-1, idx.FindFile("squirrel.exe"));
        Assert.Equal(-1, idx.FindFile("nope"));

        // the A prefix alone is the local content map
        var a = idx.CopySectionA();
        Assert.Equal(TestIndexBuilder.SectionAOf(trailer), a);
        var local = ChunkIndex.ParseLocal(a);
        Assert.False(local.HasTransportMap);
        Assert.Equal(idx.FileCount, local.FileCount);
        Assert.Equal(idx.GetFileHash(2), local.GetFileHash(2));
        Assert.Throws<InvalidOperationException>(() => local.GetTransportFile(0));

        // a trailer is not a valid local map and A alone is not a valid trailer
        Assert.Throws<InvalidDataException>(() => ChunkIndex.ParseLocal(trailer));
        Assert.Throws<InvalidDataException>(() => ChunkIndex.ParseTrailer(a, long.MaxValue));
    }

    [Theory]
    [MemberData(nameof(InvalidFixtures))]
    public void RejectsEach_Invalid(string fixture)
    {
        var bytes = Fixture(fixture);
        Assert.Throws<InvalidDataException>(() => ChunkIndex.ParseTrailer(bytes, long.MaxValue));
    }

    [Fact]
    public void InvalidFixturesAreAllPresent()
    {
        var names = InvalidFixtures().Select(o => (string) o[0]).ToList();
        foreach (var expected in new[] {
            "truncated", "count_overflow", "noncontiguous_chunks", "unsorted", "bad_path", "duplicate_path", "fold_duplicate", "dotdot",
            "reserved_name", "nested_squirrel", "nested_stub", "symlink_escape", "hash_algo",
        }) {
            Assert.Contains($"index_invalid_{expected}.bin", names);
        }
    }

    [Fact]
    public void RemoteRangesMustFitTheAsset()
    {
        var trailer = Fixture("index_sample.bin");
        var idx = ChunkIndex.ParseTrailer(trailer, long.MaxValue);
        long lastEnd = 0;
        for (int c = 0; c < idx.ChunkCount; c++) {
            var t = idx.GetTransportChunk(c);
            lastEnd = Math.Max(lastEnd, t.Offset + t.Length);
        }
        var fits = ChunkIndex.ParseTrailer(trailer, lastEnd + 2);
        Assert.Equal(idx.ChunkCount, fits.ChunkCount);
        Assert.Throws<InvalidDataException>(() => ChunkIndex.ParseTrailer(trailer, lastEnd - 1));
    }

    [Fact]
    public void AcceptsUnknownCdcAlgo()
    {
        var idx = ChunkIndex.ParseTrailer(Fixture("index_unknown_cdc_algo.bin"), long.MaxValue);
        Assert.Equal(77, idx.CdcAlgo);
        Assert.Equal(4096u, idx.CdcMin);
        Assert.Equal(8, idx.FileCount);
    }

    [Fact]
    public void AcceptsUnicodeFoldSample()
    {
        var trailer = Fixture("index_unicode_fold.bin");
        var idx = ChunkIndex.ParseTrailer(trailer, long.MaxValue);
        Assert.Equal(6, idx.FileCount);
        var paths = Enumerable.Range(0, idx.FileCount).Select(idx.GetPath).ToArray();
        Assert.Equal(new[] { "i.txt", "k.txt", "s.txt", "\u0130.txt", "\u017F.txt", "\u212A.txt" }, paths);
        var local = ChunkIndex.ParseLocal(TestIndexBuilder.SectionAOf(trailer));
        Assert.Equal(6, local.FileCount);
    }

    [Fact]
    public void StagedIdIsFirst32HexLower()
    {
        const string sha = "0123456789ABCDEF0123456789ABCDEFFEDCBA9876543210FEDCBA9876543210";
        Assert.Equal("0123456789abcdef0123456789abcdef", ChunkIndex.StagedIdOf(sha));
        Assert.Equal("0123456789abcdef0123456789abcdef", ChunkIndex.StagedIdOf(sha.ToLowerInvariant()));
        Assert.Throws<ArgumentException>(() => ChunkIndex.StagedIdOf("not-a-hash"));
        Assert.Throws<ArgumentException>(() => ChunkIndex.StagedIdOf(sha.Substring(0, 40)));
    }

    [Fact]
    public void CapsCheckedBeforeAllocation()
    {
        // header declaring the maximum counts on a tiny buffer: must fail on the length check without allocating per-record state
        var header = new byte[64];
        Encoding.ASCII.GetBytes("VPKA").CopyTo(header, 0);
        BitConverter.GetBytes((ushort) 1).CopyTo(header, 4);
        header[6] = 1;
        header[7] = 1;
        BitConverter.GetBytes(1_000_000u).CopyTo(header, 24);
        BitConverter.GetBytes(4_194_304u).CopyTo(header, 28);
        BitConverter.GetBytes(64u * 1024 * 1024).CopyTo(header, 32);
        var sw = Stopwatch.StartNew();
        Assert.Throws<InvalidDataException>(() => ChunkIndex.ParseTrailer(header, long.MaxValue));
        Assert.Throws<InvalidDataException>(() => ChunkIndex.ParseLocal(header));

        // counts above the caps are rejected on the cap itself
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(header, 24);
        var ex = Assert.Throws<InvalidDataException>(() => ChunkIndex.ParseTrailer(header, long.MaxValue));
        Assert.Contains("file_count", ex.Message);
        BitConverter.GetBytes(0u).CopyTo(header, 24);
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(header, 28);
        ex = Assert.Throws<InvalidDataException>(() => ChunkIndex.ParseTrailer(header, long.MaxValue));
        Assert.Contains("chunk_count", ex.Message);
        BitConverter.GetBytes(0u).CopyTo(header, 28);
        BitConverter.GetBytes(0xFFFFFFFFu).CopyTo(header, 32);
        ex = Assert.Throws<InvalidDataException>(() => ChunkIndex.ParseTrailer(header, long.MaxValue));
        Assert.Contains("path_blob_len", ex.Message);
        Assert.True(sw.ElapsedMilliseconds < 2000, "cap checks must be instant");

        // the 512 MiB trailer cap is checked on the buffer length alone
        Assert.Throws<InvalidDataException>(() => ChunkIndex.ParseTrailer(new byte[ChunkIndex.MaxTrailerLength + 1], long.MaxValue));
    }

    [Fact]
    public void EmptyIndexIsValid()
    {
        var b = new TestIndexBuilder();
        var idx = ChunkIndex.ParseTrailer(b.BuildTrailer(), 0);
        Assert.Equal(0, idx.FileCount);
        Assert.Equal(0, idx.ChunkCount);
        Assert.Equal(ChunkIndex.AHeaderSize, idx.SectionALength);
        Assert.Equal(-1, idx.FindFile("x"));
    }

    [Fact]
    public void TryReadLocalReturnsNullForMissingOrInvalid()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = Path.Combine(dir, ChunkIndex.LocalFileName);
        Assert.Null(ChunkIndex.TryReadLocal(path));
        File.WriteAllBytes(path, new byte[100]);
        Assert.Null(ChunkIndex.TryReadLocal(path));
        File.WriteAllBytes(path, TestIndexBuilder.SectionAOf(Fixture("index_sample.bin")));
        Assert.NotNull(ChunkIndex.TryReadLocal(path));
    }

    [Theory]
    [InlineData("a.txt", null)]
    [InlineData("dir/sub/a.txt", null)]
    [InlineData("Übersicht/ü.txt", null)]
    [InlineData("con.txt.bak", "reserved")]
    [InlineData("LPT9", "reserved")]
    [InlineData("conx.txt", null)]
    [InlineData("a:b", "illegal")]
    [InlineData("a\\b", "illegal")]
    [InlineData("a?", "illegal")]
    [InlineData("a*", "illegal")]
    [InlineData("a\"b", "illegal")]
    [InlineData("a<b", "illegal")]
    [InlineData("a>b", "illegal")]
    [InlineData("a|b", "illegal")]
    [InlineData("trailing.", "ends with")]
    [InlineData("trailing ", "ends with")]
    [InlineData("dir./x", "ends with")]
    [InlineData("/abs", "leading or trailing")]
    [InlineData("abs/", "leading or trailing")]
    [InlineData("a//b", "empty path segment")]
    [InlineData("./a", "'.' segment")]
    [InlineData("a/../b", "'..' segment")]
    [InlineData("sq.chunks", "may not be a record")]
    [InlineData("SQ.CHUNKS", "may not be a record")]
    [InlineData("x/sq.chunks", null)]
    [InlineData("tab\there", "control")]
    public void PathRules(string path, string? expectedError)
    {
        var err = ChunkIndex.ValidatePath(path);
        if (expectedError == null) {
            Assert.Null(err);
        } else {
            Assert.NotNull(err);
            Assert.Contains(expectedError, err);
        }
    }

    [Fact]
    public void PathRulesSegmentLength()
    {
        Assert.Null(ChunkIndex.ValidatePath(new string('a', 255)));
        Assert.Contains("255", ChunkIndex.ValidatePath(new string('a', 256))!);
        // counted in UTF-16 code units like NTFS, not UTF-8 bytes: 255 two-byte characters (510 bytes) are fine
        Assert.Null(ChunkIndex.ValidatePath(new string('ü', 255)));
        Assert.Contains("255", ChunkIndex.ValidatePath(new string('ü', 256))!);
        // a CJK file name of 90 characters (270 UTF-8 bytes) packs as it always did
        Assert.Null(ChunkIndex.ValidatePath("docs/" + new string('文', 90) + ".pdf"));
        // a character outside the BMP is two code units
        Assert.Contains("255", ChunkIndex.ValidatePath(string.Concat(Enumerable.Repeat("😀", 128)))!);
        Assert.Null(ChunkIndex.ValidatePath(string.Concat(Enumerable.Repeat("😀", 127))));
        // symlink target segments follow the same limit as the Rust reader
        Assert.Contains("255", ChunkIndex.ValidateSymlinkTarget("link", new string('a', 256))!);
    }

    [Theory]
    [InlineData("data/link", "../MyApp.exe", null)]
    [InlineData("data/link", "./sibling", null)]
    [InlineData("data/link", "sub/dir/", null)]
    [InlineData("link", "../x", "escapes")]
    [InlineData("data/link", "../../x", "escapes")]
    [InlineData("a/b/link", "../../x", null)]
    [InlineData("a/b/link", "../..", null)]
    [InlineData("a/b/link", "../../..", "escapes")]
    [InlineData("data/link", "/etc/passwd", "absolute")]
    [InlineData("data/link", "C:\\x", "absolute")]
    [InlineData("data/link", "a|b", "illegal")]
    [InlineData("data/link", "a//b", "empty")]
    [InlineData("data/link", "x\\y", null)]
    [InlineData("data/link", "x. /y", "ends with")]
    public void SymlinkTargetRules(string link, string target, string? expectedError)
    {
        var err = ChunkIndex.ValidateSymlinkTarget(link, target);
        if (expectedError == null) {
            Assert.Null(err);
        } else {
            Assert.NotNull(err);
            Assert.Contains(expectedError, err);
        }
    }

    [Fact]
    public void AsciiFoldOnlyFoldsAsciiLetters()
    {
        Assert.Equal("abc/def.txt", ChunkIndex.AsciiFold("ABC/Def.TXT"));
        Assert.Equal("\u212A\u0130\u017F", ChunkIndex.AsciiFold("\u212A\u0130\u017F"));
        Assert.Same("already", ChunkIndex.AsciiFold("already"));
    }

    [Fact]
    public void FeedWithNullPointerFieldsParses()
    {
        var feed = VelopackAssetFeed.FromJson(File.ReadAllText(Path.Combine(DeltaV2Fixtures.FixtureDir, "feed_null_pointer.json")));
        Assert.Equal(3, feed.Assets.Length);
        var nulls = feed.Assets.Single(a => a.Version.ToString() == "1.0.0");
        Assert.Null(nulls.ChunkIndexOffset);
        Assert.Null(nulls.ChunkIndexLength);
        Assert.Null(nulls.ChunkIndexSHA256);
        Assert.False(nulls.HasChunkIndex);
        var pointer = feed.Assets.Single(a => a.Version.ToString() == "1.1.0");
        Assert.Equal(1500, pointer.ChunkIndexOffset);
        Assert.Equal(400, pointer.ChunkIndexLength);
        Assert.Equal("0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF", pointer.ChunkIndexSHA256);
        Assert.True(pointer.HasChunkIndex);
        Assert.Equal("0123456789abcdef0123456789abcdef", ChunkIndex.StagedIdOf(pointer.ChunkIndexSHA256));
        var absent = feed.Assets.Single(a => a.Version.ToString() == "0.9.0");
        Assert.False(absent.HasChunkIndex);
    }

    [Fact]
    public void AssetPointerRoundTripsThroughCompiledJson()
    {
        var asset = new VelopackAsset {
            PackageId = "App",
            Version = SemanticVersion.Parse("1.2.3"),
            Type = VelopackAssetType.Full,
            FileName = "App-1.2.3-full.nupkg",
            SHA1 = "A",
            SHA256 = "B",
            Size = 10,
            ChunkIndexOffset = 5,
            ChunkIndexLength = 4,
            ChunkIndexSHA256 = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
        };
        var json = CompiledJson.SerializeVelopackAsset(asset);
        Assert.Contains("\"ChunkIndexOffset\": 5", json);
        Assert.Contains("\"Type\": \"Full\"", json);
        var back = CompiledJson.DeserializeVelopackAsset(json)!;
        Assert.Equal(asset, back);

        var plain = new VelopackAsset { PackageId = "App", Version = SemanticVersion.Parse("1.0.0"), FileName = "x", Type = VelopackAssetType.Full };
        var plainJson = CompiledJson.SerializeVelopackAsset(plain);
        Assert.DoesNotContain("ChunkIndex", plainJson);
        Assert.False(CompiledJson.DeserializeVelopackAsset(plainJson)!.HasChunkIndex);
    }

    [Fact]
    public void ToPlainAssetAndGitBaseAssetCarryThePointer()
    {
        var asset = new VelopackAsset {
            PackageId = "App",
            Version = SemanticVersion.Parse("1.2.3"),
            Type = VelopackAssetType.Full,
            FileName = "App-1.2.3-full.nupkg",
            SHA1 = "A",
            SHA256 = "B",
            Size = 10,
            NotesMarkdown = "md",
            NotesHTML = "html",
            ChunkIndexOffset = 5,
            ChunkIndexLength = 4,
            ChunkIndexSHA256 = "C",
        };
        var git = new GitBase<GithubRelease>.GitBaseAsset(asset, new GithubRelease { Name = "r" });
        Assert.Equal(5, git.ChunkIndexOffset);
        Assert.Equal(4, git.ChunkIndexLength);
        Assert.Equal("C", git.ChunkIndexSHA256);
        Assert.True(git.HasChunkIndex);

        var plain = git.ToPlainAsset();
        Assert.IsType<VelopackAsset>(plain);
        Assert.Equal(asset, plain);
        Assert.DoesNotContain("Release", CompiledJson.SerializeVelopackAsset(plain));
    }

    [Fact]
    public void StagedReadyMarkerRoundTrips()
    {
        var marker = new StagedReadyMarker {
            Format = StagedReadyMarker.CurrentFormat,
            StagedId = "0123456789abcdef0123456789abcdef",
            IndexSHA256 = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
            UpdaterSHA256 = "FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210FEDCBA9876543210",
            Asset = new VelopackAsset {
                PackageId = "App",
                Version = SemanticVersion.Parse("2.0.0-beta.1"),
                Type = VelopackAssetType.Full,
                FileName = "App-2.0.0-beta.1-full.nupkg",
                SHA256 = "B",
                Size = 10,
                ChunkIndexOffset = 5,
                ChunkIndexLength = 4,
                ChunkIndexSHA256 = "0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF0123456789ABCDEF",
            },
            CreatedUtc = "2026-10-17T12:00:00Z",
        };
        var json = CompiledJson.SerializeStagedReadyMarker(marker);
        Assert.Contains("\"Format\": 1", json);
        Assert.Contains("\"Version\": \"2.0.0-beta.1\"", json);
        var back = CompiledJson.DeserializeStagedReadyMarker(json)!;
        Assert.Equal(marker.Format, back.Format);
        Assert.Equal(marker.StagedId, back.StagedId);
        Assert.Equal(marker.IndexSHA256, back.IndexSHA256);
        Assert.Equal(marker.UpdaterSHA256, back.UpdaterSHA256);
        Assert.Equal(marker.CreatedUtc, back.CreatedUtc);
        Assert.Equal(marker.Asset, back.Asset);
        Assert.Equal(marker.StagedId, ChunkIndex.StagedIdOf(back.Asset.ChunkIndexSHA256));

        // unknown and missing members are tolerated (a Rust-written marker)
        var sparse = CompiledJson.DeserializeStagedReadyMarker("{ \"Format\": 1, \"StagedId\": \"x\", \"Extra\": true }")!;
        Assert.Equal("x", sparse.StagedId);
        Assert.Null(sparse.Asset);
    }
}
