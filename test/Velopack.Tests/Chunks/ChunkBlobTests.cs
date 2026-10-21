#nullable enable
using System.Text;
using Velopack.Chunks;
using Velopack.Util;

namespace Velopack.Tests.Chunks;

public class ChunkBlobTests
{
    private static (Hash256 hash, byte[] data) Chunk(ulong seed, int size)
    {
        var data = TestIndexBuilder.DeterministicBytes(seed, size);
        return (ChunkInflate.Sha256(data), data);
    }

    [Fact]
    public void AppendAndRead()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = Path.Combine(dir, ChunkBlob.FileName);
        var (h1, d1) = Chunk(1, 70_000);
        var (h2, d2) = Chunk(2, 1);
        var (h3, d3) = Chunk(3, 300_000);

        using (var blob = ChunkBlob.Open(path)) {
            Assert.Equal(0, blob.Count);
            Assert.False(blob.Contains(h1));
            Assert.Null(blob.Read(h1));
            blob.Append(h1, d1);
            blob.Append(h2, d2);
            var padded = new byte[d3.Length + 50];
            d3.CopyTo(padded, 25);
            blob.Append(h3, padded, 25, d3.Length);
            Assert.Equal(3, blob.Count);
            Assert.True(blob.Contains(h2));
            Assert.Equal(d2, blob.Read(h2));
            Assert.Equal(d1, blob.Read(h1));
            Assert.Equal(d3, blob.Read(h3));
            Assert.Equal((3 * ChunkBlob.RecordHeaderSize) + d1.Length + d2.Length + d3.Length, blob.Length);
        }

        // survives reopen; the record layout is "VCHK" ‖ u32 length ‖ sha256 ‖ payload
        var raw = File.ReadAllBytes(path);
        Assert.Equal("VCHK", Encoding.ASCII.GetString(raw, 0, 4));
        Assert.Equal(70_000u, BitConverter.ToUInt32(raw, 4));
        Assert.Equal(h1, Hash256.FromBytes(raw, 8));
        Assert.Equal(d1, raw.AsSpan(ChunkBlob.RecordHeaderSize, d1.Length).ToArray());

        using (var blob = ChunkBlob.Open(path)) {
            Assert.Equal(3, blob.Count);
            Assert.Equal(new[] { h1, h2, h3 }.OrderBy(h => h.ToHex()), blob.GetHashes().OrderBy(h => h.ToHex()));
            Assert.Equal(d3, blob.Read(h3));
            Assert.Equal(raw.Length, blob.Length);
        }
    }

    [Fact]
    public void AppendRejectsBadLengths()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        using var blob = ChunkBlob.Open(Path.Combine(dir, ChunkBlob.FileName));
        Assert.Throws<ArgumentOutOfRangeException>(() => blob.Append(default, Array.Empty<byte>()));
        Assert.Throws<ArgumentOutOfRangeException>(() => blob.Append(default, new byte[10], 5, 10));
    }

    [Fact]
    public void TruncatedTailIsDropped()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = Path.Combine(dir, ChunkBlob.FileName);
        var (h1, d1) = Chunk(1, 10_000);
        var (h2, d2) = Chunk(2, 20_000);
        using (var blob = ChunkBlob.Open(path)) {
            blob.Append(h1, d1);
            blob.Append(h2, d2);
        }
        long firstRecordEnd = ChunkBlob.RecordHeaderSize + d1.Length;

        // cut the second record's payload short
        using (var fs = new FileStream(path, FileMode.Open)) fs.SetLength(firstRecordEnd + ChunkBlob.RecordHeaderSize + 5000);
        using (var blob = ChunkBlob.Open(path)) {
            Assert.Equal(1, blob.Count);
            Assert.True(blob.Contains(h1));
            Assert.False(blob.Contains(h2));
            Assert.Equal(firstRecordEnd, blob.Length);
        }
        Assert.Equal(firstRecordEnd, new FileInfo(path).Length);

        // a partial header
        using (var fs = new FileStream(path, FileMode.Open)) {
            fs.Seek(0, SeekOrigin.End);
            fs.Write(Encoding.ASCII.GetBytes("VCHK"), 0, 4);
            fs.Write(new byte[10], 0, 10);
        }
        using (var blob = ChunkBlob.Open(path)) {
            Assert.Equal(1, blob.Count);
            Assert.Equal(firstRecordEnd, blob.Length);
        }

        // a bad magic ends the scan and drops everything after it, even a valid record
        using (var fs = new FileStream(path, FileMode.Open)) {
            fs.Seek(0, SeekOrigin.End);
            fs.Write(new byte[ChunkBlob.RecordHeaderSize + 10], 0, ChunkBlob.RecordHeaderSize + 10);
        }
        using (var blob = ChunkBlob.Open(path)) {
            blob.Append(h2, d2);
        }
        Assert.Equal(firstRecordEnd + ChunkBlob.RecordHeaderSize + d2.Length, new FileInfo(path).Length);
        using (var blob = ChunkBlob.Open(path)) {
            Assert.Equal(2, blob.Count);
            Assert.Equal(d2, blob.Read(h2));
        }
    }

    [Fact]
    public void CorruptPayloadIsNotIndexedAndScanContinues()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = Path.Combine(dir, ChunkBlob.FileName);
        var (h1, d1) = Chunk(1, 10_000);
        var (h2, d2) = Chunk(2, 20_000);
        var (h3, d3) = Chunk(3, 30_000);
        using (var blob = ChunkBlob.Open(path)) {
            blob.Append(h1, d1);
            blob.Append(h2, d2);
            blob.Append(h3, d3);
        }
        long total = new FileInfo(path).Length;

        // zero the second payload (header intact): the power-loss tail
        using (var fs = new FileStream(path, FileMode.Open)) {
            fs.Seek((2 * ChunkBlob.RecordHeaderSize) + d1.Length, SeekOrigin.Begin);
            fs.Write(new byte[d2.Length], 0, d2.Length);
        }
        using (var blob = ChunkBlob.Open(path)) {
            Assert.Equal(2, blob.Count);
            Assert.True(blob.Contains(h1));
            Assert.False(blob.Contains(h2));
            Assert.True(blob.Contains(h3));
            Assert.Equal(d3, blob.Read(h3));
            Assert.Equal(total, blob.Length); // nothing truncated
            Assert.Null(blob.Read(h2));

            // re-appending the chunk makes it available again (the missing-round path)
            blob.Append(h2, d2);
            Assert.Equal(d2, blob.Read(h2));
        }
        using (var blob = ChunkBlob.Open(path)) {
            Assert.Equal(3, blob.Count);
            Assert.Equal(d2, blob.Read(h2));
        }
    }

    [Fact]
    public void ReadRehashesAndDropsCorruptedChunks()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = Path.Combine(dir, ChunkBlob.FileName);
        var (h1, d1) = Chunk(1, 10_000);
        using var blob = ChunkBlob.Open(path);
        blob.Append(h1, d1);
        blob.Flush();
        // corrupt the payload underneath the open blob (share mode allows readers; write through a second handle)
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Write, FileShare.ReadWrite)) {
            fs.Seek(ChunkBlob.RecordHeaderSize + 100, SeekOrigin.Begin);
            fs.WriteByte((byte) (d1[100] ^ 0xFF));
        }
        Assert.True(blob.Contains(h1));
        Assert.Null(blob.Read(h1));
        Assert.False(blob.Contains(h1));
    }

    [Fact]
    public void DuplicateRecordsFirstWinsOnScanLatestWinsInProcess()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = Path.Combine(dir, ChunkBlob.FileName);
        var (h1, d1) = Chunk(1, 10_000);
        using (var blob = ChunkBlob.Open(path)) {
            blob.Append(h1, d1);
            blob.Append(h1, d1);
            Assert.Equal(1, blob.Count);
            Assert.Equal(2 * (ChunkBlob.RecordHeaderSize + d1.Length), blob.Length);
        }
        using (var blob = ChunkBlob.Open(path)) {
            Assert.Equal(1, blob.Count);
            Assert.Equal(d1, blob.Read(h1));
        }
    }

    [Fact]
    public void ConcurrentAppendsAreSerialised()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var path = Path.Combine(dir, ChunkBlob.FileName);
        var chunks = Enumerable.Range(1, 64).Select(i => Chunk((ulong) i, 5000 + (i * 100))).ToArray();
        using (var blob = ChunkBlob.Open(path)) {
            Parallel.ForEach(chunks, new ParallelOptions { MaxDegreeOfParallelism = 4 }, c => blob.Append(c.hash, c.data));
            Assert.Equal(64, blob.Count);
        }
        using (var blob = ChunkBlob.Open(path)) {
            Assert.Equal(64, blob.Count);
            foreach (var (hash, data) in chunks) Assert.Equal(data, blob.Read(hash));
        }
    }
}
