#nullable enable
using System.IO.Compression;
using Velopack.Chunks;
using Velopack.Util;

namespace Velopack.Tests.Chunks;

public class ChunkInflateTests
{
    /// <summary>The §4.1 per-chunk procedure: one Write, a sync flush, bytes taken before Dispose.</summary>
    public static byte[] DeflateChunk(byte[] plain)
    {
        var ms = new MemoryStream();
        var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true);
        ds.Write(plain, 0, plain.Length);
        ds.Flush();
        var c = ms.ToArray();
        ds.Dispose();
        Assert.True(c.Length >= 4 && c[^4] == 0 && c[^3] == 0 && c[^2] == 0xFF && c[^1] == 0xFF, "sync flush must end with 00 00 FF FF");
        return c;
    }

    private static byte[] Plain(ulong seed, int size)
    {
        // compressible: a repeating pattern mixed with a little noise
        var bytes = TestIndexBuilder.DeterministicBytes(seed, size);
        for (int i = 0; i < size; i++) {
            if (i % 4 != 0) bytes[i] = (byte) (i % 251);
        }
        return bytes;
    }

    [Fact]
    public void InflatesNonFinalChunk()
    {
        var plain = Plain(1, 100_000);
        var comp = DeflateChunk(plain);
        Assert.True(comp.Length < plain.Length);
        var back = ChunkInflate.TryInflate(comp, 0, comp.Length, plain.Length);
        Assert.NotNull(back);
        Assert.Equal(plain, back);

        // works at an offset inside a larger buffer (a coalesced response)
        var buffer = new byte[comp.Length + 100];
        comp.CopyTo(buffer, 37);
        Assert.Equal(plain, ChunkInflate.TryInflate(buffer, 37, comp.Length, plain.Length));
        Assert.Equal(plain, ChunkInflate.TryDecodeChunk(buffer, 37, comp.Length, ChunkIndex.MethodDeflate, plain.Length, ChunkInflate.Sha256(plain)));
    }

    [Fact]
    public void InflatesFinalChunk()
    {
        // the last chunk of an entry followed by the writer's final block (never fetched in practice, but legal input)
        var plain = Plain(2, 70_000);
        var comp = DeflateChunk(plain).Concat(ChunkInflate.FinalBlock).ToArray();
        Assert.Equal(plain, ChunkInflate.TryInflate(comp, 0, comp.Length, plain.Length));

        // a complete deflate stream (DeflateStream disposed) also inflates
        var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) ds.Write(plain, 0, plain.Length);
        var complete = ms.ToArray();
        Assert.Equal(plain, ChunkInflate.TryInflate(complete, 0, complete.Length, plain.Length));
    }

    [Fact]
    public void ConcatenatedChunksFormOneStream()
    {
        var p1 = Plain(3, 60_000);
        var p2 = Plain(4, 50_000);
        var stream = DeflateChunk(p1).Concat(DeflateChunk(p2)).Concat(ChunkInflate.FinalBlock).ToArray();
        using var ds = new DeflateStream(new MemoryStream(stream), CompressionMode.Decompress);
        var all = new MemoryStream();
        ds.CopyTo(all);
        Assert.Equal(p1.Concat(p2).ToArray(), all.ToArray());
    }

    [Fact]
    public void RejectsWrongLength()
    {
        var plain = Plain(5, 40_000);
        var comp = DeflateChunk(plain);
        Assert.Null(ChunkInflate.TryInflate(comp, 0, comp.Length, plain.Length - 1));
        Assert.Null(ChunkInflate.TryInflate(comp, 0, comp.Length, plain.Length + 1));
        Assert.Null(ChunkInflate.TryInflate(comp, 0, comp.Length, 0));
        // truncated compressed input
        Assert.Null(ChunkInflate.TryInflate(comp, 0, comp.Length / 2, plain.Length));
        Assert.Null(ChunkInflate.TryDecodeChunk(comp, 0, comp.Length, ChunkIndex.MethodDeflate, plain.Length - 1, ChunkInflate.Sha256(plain)));
    }

    [Fact]
    public void RejectsWrongHash()
    {
        var plain = Plain(6, 40_000);
        var comp = DeflateChunk(plain);
        var wrong = ChunkInflate.Sha256(Plain(7, 40_000));
        Assert.Null(ChunkInflate.TryDecodeChunk(comp, 0, comp.Length, ChunkIndex.MethodDeflate, plain.Length, wrong));
        Assert.Null(ChunkInflate.TryDecodeChunk(plain, 0, plain.Length, ChunkIndex.MethodStored, plain.Length, wrong));
    }

    [Fact]
    public void StoredChunkPassthrough()
    {
        var plain = Plain(8, 30_000);
        var buffer = new byte[plain.Length + 20];
        plain.CopyTo(buffer, 10);
        var back = ChunkInflate.TryDecodeChunk(buffer, 10, plain.Length, ChunkIndex.MethodStored, plain.Length, ChunkInflate.Sha256(plain));
        Assert.Equal(plain, back);
        Assert.Null(ChunkInflate.TryDecodeChunk(buffer, 10, plain.Length - 1, ChunkIndex.MethodStored, plain.Length, ChunkInflate.Sha256(plain)));
        Assert.Null(ChunkInflate.TryDecodeChunk(buffer, 10, plain.Length, ChunkIndex.MethodStored, plain.Length - 1, ChunkInflate.Sha256(plain)));
        Assert.Null(ChunkInflate.TryDecodeChunk(buffer, 10, plain.Length, 3, plain.Length, ChunkInflate.Sha256(plain)));
    }

    [Fact]
    public void ExtraBytesBeforeTerminatorFailLengthOrHash()
    {
        var p1 = Plain(9, 50_000);
        var p2 = Plain(10, 20_000);
        var two = DeflateChunk(p1).Concat(DeflateChunk(p2)).ToArray();
        // the second segment produces more than the expected length
        Assert.Null(ChunkInflate.TryInflate(two, 0, two.Length, p1.Length));
        // expecting both lengths makes the length check pass but the hash of chunk 1 fails
        Assert.Null(ChunkInflate.TryDecodeChunk(two, 0, two.Length, ChunkIndex.MethodDeflate, p1.Length + p2.Length, ChunkInflate.Sha256(p1)));
    }

    [Fact]
    public void ExtraBytesAfterTerminatorAreIgnoredButHashStillVerified()
    {
        var plain = Plain(11, 50_000);
        var withJunk = DeflateChunk(plain).Concat(ChunkInflate.FinalBlock).Concat(new byte[] { 0xDE, 0xAD, 0xBE, 0xEF }).ToArray();
        // .NET's DeflateStream stops at the final block; the junk is not observable, so the length check passes...
        Assert.Equal(plain, ChunkInflate.TryInflate(withJunk, 0, withJunk.Length, plain.Length));
        // ...and only the SHA-256 guards the content
        var good = ChunkInflate.Sha256(plain);
        var bad = ChunkInflate.Sha256(Plain(12, 50_000));
        Assert.Equal(plain, ChunkInflate.TryDecodeChunk(withJunk, 0, withJunk.Length, ChunkIndex.MethodDeflate, plain.Length, good));
        Assert.Null(ChunkInflate.TryDecodeChunk(withJunk, 0, withJunk.Length, ChunkIndex.MethodDeflate, plain.Length, bad));
    }

    [Fact]
    public void GarbageInputIsRejectedWithoutThrowing()
    {
        var garbage = TestIndexBuilder.DeterministicBytes(99, 5000);
        Assert.Null(ChunkInflate.TryInflate(garbage, 0, garbage.Length, 10_000));
        Assert.Null(ChunkInflate.TryInflate(Array.Empty<byte>(), 0, 0, 10));
    }

    [Fact]
    public void Sha256HelpersAgree()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var data = TestIndexBuilder.DeterministicBytes(5, 200_000);
        var expected = Hash256.FromBytes(System.Security.Cryptography.SHA256.HashData(data));
        Assert.Equal(expected, ChunkInflate.Sha256(data));
        Assert.Equal(expected, ChunkInflate.Sha256(new MemoryStream(data)));
        Assert.Equal(expected, ChunkInflate.Sha256(new MemoryStream(data.Concat(new byte[10]).ToArray()), data.Length));
        var path = Path.Combine(dir, "f.bin");
        File.WriteAllBytes(path, data);
        Assert.Equal(expected, ChunkInflate.Sha256File(path));
        Assert.Equal(Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(data)), expected.ToHex());
        Assert.Equal(expected, Hash256.Parse(expected.ToHex().ToLowerInvariant()));
        Assert.Throws<EndOfStreamException>(() => ChunkInflate.Sha256(new MemoryStream(data), data.Length + 1));
    }
}
