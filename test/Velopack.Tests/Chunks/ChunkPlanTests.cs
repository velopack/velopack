#nullable enable
using System.Text.Json;
using Velopack.Chunks;
using Velopack.Util;

namespace Velopack.Tests.Chunks;

public class ChunkPlanTests
{
    [Fact]
    public void PlanVectors()
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(DeltaV2Fixtures.FixtureDir, "plan_vectors.json")));
        var constants = doc.RootElement.GetProperty("constants");
        Assert.Equal(ChunkPlan.MaxRequestLength, constants.GetProperty("max_request_length").GetInt64());
        Assert.Equal(ChunkPlan.MaxGap, constants.GetProperty("max_gap").GetInt32());
        Assert.Equal(ChunkPlan.TransportCeiling, constants.GetProperty("transport_ceiling").GetDouble());

        int count = 0;
        foreach (var v in doc.RootElement.GetProperty("vectors").EnumerateArray()) {
            count++;
            var name = v.GetProperty("name").GetString();
            long assetSize = v.GetProperty("asset_size").GetInt64();
            var remote = ChunkIndex.ParseTrailer(Convert.FromHexString(v.GetProperty("remote_trailer_hex").GetString()!), assetSize);
            var localHex = v.GetProperty("local_index_hex");
            var local = localHex.ValueKind == JsonValueKind.Null ? null : ChunkIndex.ParseLocal(Convert.FromHexString(localHex.GetString()!));
            var unusable = v.GetProperty("local_unusable_paths").EnumerateArray().Select(e => e.GetString()!).ToHashSet(StringComparer.Ordinal);
            var blob = v.GetProperty("blob_hashes").EnumerateArray().Select(e => Hash256.Parse(e.GetString()!)).ToHashSet();

            var plan = ChunkPlan.Create(remote, local, i => !unusable.Contains(local!.GetPath(i)), blob.Contains);
            var ranges = ChunkPlan.Coalesce(plan.Needed);

            var expected = v.GetProperty("expected");
            var expectedNeeded = expected.GetProperty("needed").EnumerateArray().ToArray();
            Assert.True(expectedNeeded.Length == plan.Needed.Count, $"{name}: needed count");
            for (int i = 0; i < expectedNeeded.Length; i++) {
                var e = expectedNeeded[i];
                var n = plan.Needed[i];
                Assert.True(e.GetProperty("chunk").GetInt32() == n.ChunkIndex, $"{name}: needed[{i}].chunk");
                Assert.True(e.GetProperty("file").GetInt32() == n.FileIndex, $"{name}: needed[{i}].file");
                Assert.True(e.GetProperty("sha256").GetString() == n.Hash.ToHex(), $"{name}: needed[{i}].sha256");
                Assert.True(e.GetProperty("method").GetByte() == n.Method, $"{name}: needed[{i}].method");
                Assert.True(e.GetProperty("offset").GetInt64() == n.Offset, $"{name}: needed[{i}].offset");
                Assert.True(e.GetProperty("length").GetInt32() == n.Length, $"{name}: needed[{i}].length");
                Assert.True(e.GetProperty("plain_length").GetInt32() == n.PlainLength, $"{name}: needed[{i}].plain_length");
            }
            Assert.True(expected.GetProperty("needed_compressed_bytes").GetInt64() == plan.NeededCompressedBytes, $"{name}: compressed bytes");
            Assert.True(expected.GetProperty("needed_plaintext_bytes").GetInt64() == plan.NeededPlaintextBytes, $"{name}: plaintext bytes");
            Assert.True(expected.GetProperty("remote_file_bytes").GetInt64() == plan.RemoteFileBytes, $"{name}: remote bytes");
            Assert.True(expected.GetProperty("whole_file_copies").GetInt32() == plan.WholeFileCopies, $"{name}: whole copies");
            Assert.True(expected.GetProperty("classic_fallback").GetBoolean() == plan.ExceedsTransportCeiling(assetSize), $"{name}: fallback");

            var expectedRanges = expected.GetProperty("ranges").EnumerateArray().ToArray();
            Assert.True(expectedRanges.Length == ranges.Count, $"{name}: range count");
            for (int i = 0; i < expectedRanges.Length; i++) {
                Assert.True(expectedRanges[i].GetProperty("offset").GetInt64() == ranges[i].Offset, $"{name}: ranges[{i}].offset");
                Assert.True(expectedRanges[i].GetProperty("length").GetInt64() == ranges[i].Length, $"{name}: ranges[{i}].length");
                var chunks = expectedRanges[i].GetProperty("chunks").EnumerateArray().Select(c => c.GetInt32()).ToArray();
                Assert.Equal(chunks, ranges[i].Chunks.Select(c => c.ChunkIndex).ToArray());
            }
        }
        Assert.True(count >= 10);
    }

    private static NeededChunk Chunk(int index, long offset, int length) =>
        new NeededChunk(index, 0, Hash256.Parse(new string('A', 60) + index.ToString("X4")), ChunkIndex.MethodDeflate, offset, length, length);

    [Fact]
    public void CoalesceGapAndSizeLimits()
    {
        // gap of exactly 32 KiB merges, 32 KiB + 1 splits
        var r = ChunkPlan.Coalesce(new[] { Chunk(0, 0, 1000), Chunk(1, 1000 + ChunkPlan.MaxGap, 1000) });
        Assert.Single(r);
        Assert.Equal(2000 + ChunkPlan.MaxGap, r[0].Length);
        r = ChunkPlan.Coalesce(new[] { Chunk(0, 0, 1000), Chunk(1, 1000 + ChunkPlan.MaxGap + 1, 1000) });
        Assert.Equal(2, r.Count);

        // merged length of exactly 8 MiB is allowed, one byte more splits
        int half = (int) (ChunkPlan.MaxRequestLength / 2);
        r = ChunkPlan.Coalesce(new[] { Chunk(0, 0, half), Chunk(1, half, half) });
        Assert.Single(r);
        Assert.Equal(ChunkPlan.MaxRequestLength, r[0].Length);
        r = ChunkPlan.Coalesce(new[] { Chunk(0, 0, half), Chunk(1, half, half + 1) });
        Assert.Equal(2, r.Count);

        // input order does not matter: requests come out sorted by offset
        r = ChunkPlan.Coalesce(new[] { Chunk(2, 500_000, 100), Chunk(0, 0, 100), Chunk(1, 150, 100) });
        Assert.Equal(2, r.Count);
        Assert.Equal(0, r[0].Offset);
        Assert.Equal(250, r[0].Length);
        Assert.Equal(new[] { 0, 1 }, r[0].Chunks.Select(c => c.ChunkIndex).ToArray());
        Assert.Equal(500_000, r[1].Offset);

        Assert.Empty(ChunkPlan.Coalesce(Array.Empty<NeededChunk>()));
    }

    [Fact]
    public void LoneChunkLargerThanRequestCapIsScheduled()
    {
        int big = (int) ChunkPlan.MaxRequestLength + 12345;
        var r = ChunkPlan.Coalesce(new[] { Chunk(0, 100, 50), Chunk(1, 150, big), Chunk(2, 150 + big, 50) });
        Assert.Equal(3, r.Count);
        Assert.Equal(big, r[1].Length);
        Assert.Single(r[1].Chunks);
        Assert.Equal(1, r[1].Chunks[0].ChunkIndex);
    }

    [Fact]
    public void TransportCeilingFallsBack()
    {
        var b = new TestIndexBuilder();
        b.AddFile("a.bin", TestIndexBuilder.DeterministicBytes(1, 90_000), deflate: false);
        b.Layout();
        var remote = ChunkIndex.ParseTrailer(b.BuildTrailer(), long.MaxValue);
        var plan = ChunkPlan.Create(remote, null, _ => true, _ => false);
        Assert.Equal(90_000, plan.NeededCompressedBytes);
        Assert.True(plan.ExceedsTransportCeiling(100_000)); // exactly 90 %
        Assert.True(plan.ExceedsTransportCeiling(99_999));
        Assert.False(plan.ExceedsTransportCeiling(100_001));

        var local = ChunkIndex.ParseLocal(b.BuildSectionA());
        var cheap = ChunkPlan.Create(remote, local, _ => true, _ => false);
        Assert.Empty(cheap.Needed);
        Assert.False(cheap.ExceedsTransportCeiling(100_000));
    }

    private static (ChunkIndex remote, ChunkIndex local, LocalRoots roots, byte[] content) SetupLocalTree(string dir, int size)
    {
        var content = TestIndexBuilder.DeterministicBytes(42, size);
        var b = new TestIndexBuilder();
        b.AddFile("app/data.bin", content, chunkLengths: new[] { size / 2, size - (size / 2) });
        b.AddFile("Squirrel.exe", TestIndexBuilder.DeterministicBytes(43, 5000), ChunkFileDisposition.Updater);
        b.AddFile("App_ExecutionStub.exe", TestIndexBuilder.DeterministicBytes(44, 4000), ChunkFileDisposition.Stub, deflate: false);
        b.Layout();
        var remote = ChunkIndex.ParseTrailer(b.BuildTrailer(), long.MaxValue);
        var local = ChunkIndex.ParseLocal(b.BuildSectionA());

        var current = Path.Combine(dir, "current");
        Directory.CreateDirectory(Path.Combine(current, "app"));
        File.WriteAllBytes(Path.Combine(current, "app", "data.bin"), content);
        File.WriteAllBytes(Path.Combine(dir, "Update.exe"), TestIndexBuilder.DeterministicBytes(43, 5000));
        File.WriteAllBytes(Path.Combine(dir, "App.exe"), TestIndexBuilder.DeterministicBytes(44, 4000));
        var roots = new LocalRoots(current, Path.Combine(dir, "Update.exe"), dir);
        return (remote, local, roots, content);
    }

    [Fact]
    public void DispositionsResolveAgainstTheInstalledView()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var (remote, local, roots, _) = SetupLocalTree(dir, 100_000);
        var plan = ChunkPlan.Create(remote, local, roots, null);
        Assert.Empty(plan.Needed);
        Assert.Equal(3, plan.WholeFileCopies);
        Assert.Equal(Path.Combine(dir, "current", "app", "data.bin"), roots.Resolve(local.GetFile(local.FindFile("app/data.bin"))));
        Assert.Equal(Path.Combine(dir, "Update.exe"), roots.Resolve(local.GetFile(local.FindFile("Squirrel.exe"))));
        Assert.Equal(Path.Combine(dir, "App.exe"), roots.Resolve(local.GetFile(local.FindFile("App_ExecutionStub.exe"))));

        // the updater copy may live elsewhere (LocalAppData fallback); a missing stub makes only that file unusable
        File.Delete(Path.Combine(dir, "App.exe"));
        plan = ChunkPlan.Create(remote, local, roots, null);
        Assert.Single(plan.Needed);
        Assert.Equal("App_ExecutionStub.exe", remote.GetPath(plan.Needed[0].FileIndex));
    }

    [Fact]
    public void LocalFileWithWrongLengthIsNotASource()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var (remote, local, roots, content) = SetupLocalTree(dir, 200_000);
        int fileIndex = local.FindFile("app/data.bin");
        Assert.True(ChunkPlan.IsLocalFileUsable(local, fileIndex, roots));

        File.WriteAllBytes(Path.Combine(dir, "current", "app", "data.bin"), content.Concat(new byte[] { 1 }).ToArray());
        Assert.False(ChunkPlan.IsLocalFileUsable(local, fileIndex, roots));
        var plan = ChunkPlan.Create(remote, local, roots, null);
        Assert.Equal(2, plan.Needed.Count);
        Assert.All(plan.Needed, c => Assert.Equal(fileIndex, c.FileIndex));

        File.Delete(Path.Combine(dir, "current", "app", "data.bin"));
        Assert.False(ChunkPlan.IsLocalFileUsable(local, fileIndex, roots));
    }

    [Fact]
    public void SmallLocalFileIsHashedDuringPlanning()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        // 64 KiB: at the threshold, so it is hashed; a same-length modification is detected
        var (remote, local, roots, content) = SetupLocalTree(dir, ChunkPlan.PlanningHashThreshold);
        int fileIndex = local.FindFile("app/data.bin");
        var modified = (byte[]) content.Clone();
        modified[10] ^= 0xFF;
        File.WriteAllBytes(Path.Combine(dir, "current", "app", "data.bin"), modified);
        Assert.False(ChunkPlan.IsLocalFileUsable(local, fileIndex, roots));
        Assert.Equal(2, ChunkPlan.Create(remote, local, roots, null).Needed.Count);

        // one byte above the threshold: only the length is checked, the mismatch is left for assembly to discover
        using var _2 = TempUtil.GetTempDirectory(out var dir2);
        var (remote2, local2, roots2, content2) = SetupLocalTree(dir2, ChunkPlan.PlanningHashThreshold + 1);
        var modified2 = (byte[]) content2.Clone();
        modified2[10] ^= 0xFF;
        File.WriteAllBytes(Path.Combine(dir2, "current", "app", "data.bin"), modified2);
        Assert.True(ChunkPlan.IsLocalFileUsable(local2, local2.FindFile("app/data.bin"), roots2));
        Assert.Empty(ChunkPlan.Create(remote2, local2, roots2, null).Needed);
    }

    [Fact]
    public void BlobChunksAreNotFetchedAgain()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var (remote, _, _, content) = SetupLocalTree(dir, 100_000);
        using var blob = ChunkBlob.Open(Path.Combine(dir, ChunkBlob.FileName));
        var first = remote.GetChunk(0);
        blob.Append(first.Sha256, content, 0, first.Length);
        var plan = ChunkPlan.Create(remote, null, new LocalRoots(dir, dir, dir), blob);
        Assert.DoesNotContain(plan.Needed, c => c.ChunkIndex == 0);
        Assert.Contains(plan.Needed, c => c.ChunkIndex == 1);
    }

    [Fact]
    public void ForHashesResolvesRanges()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var (remote, _, _, _) = SetupLocalTree(dir, 100_000);
        var wanted = new[] { remote.GetChunkHash(2), remote.GetChunkHash(0), Hash256.Parse(new string('0', 64)) };
        var needed = ChunkPlan.ForHashes(remote, wanted);
        Assert.Equal(2, needed.Count);
        Assert.Equal(new[] { 0, 2 }, needed.Select(c => c.ChunkIndex).ToArray());
        Assert.Equal(remote.GetTransportChunk(2).Offset, needed[1].Offset);
        Assert.Equal(remote.GetTransportChunk(2).Length, needed[1].Length);
        Assert.Equal(remote.GetTransportFile(needed[1].FileIndex).Method, needed[1].Method);
        Assert.Empty(ChunkPlan.ForHashes(remote, Array.Empty<Hash256>()));
    }

    [Fact]
    public void PlanRequiresTransportMap()
    {
        var b = new TestIndexBuilder();
        b.AddFile("a.bin", TestIndexBuilder.DeterministicBytes(1, 1000), deflate: false);
        b.Layout();
        var local = ChunkIndex.ParseLocal(b.BuildSectionA());
        Assert.Throws<ArgumentException>(() => ChunkPlan.Create(local, null, _ => true, _ => false));
    }
}
