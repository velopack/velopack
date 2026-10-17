#nullable enable
using System.IO.Compression;

namespace Velopack.Packaging.Chunking;

/// <summary>
/// The per-chunk deflate procedure of the chunk-aligned zip writer (delta-v2 spec §4.1). Every chunk is compressed by a
/// fresh <see cref="DeflateStream"/> (so it is self-contained: no dictionary carries over) and ended with a sync flush,
/// which byte-aligns the output and terminates it with the empty stored block marker <c>00 00 FF FF</c>. The
/// concatenation of all chunks followed by <see cref="FinalBlock"/> is one standard raw deflate stream, while each
/// chunk can also be inflated alone by appending a stored final block.
/// </summary>
public static class ChunkDeflate
{
    /// <summary>The empty final fixed-Huffman block (BFINAL = 1) that ends a chunked entry.</summary>
    public static readonly byte[] FinalBlock = { 0x03, 0x00 };

    /// <summary>The trailing bytes of a sync-flushed chunk: an empty stored block (LEN = 0, NLEN = 0xFFFF).</summary>
    public static readonly byte[] SyncFlushMarker = { 0x00, 0x00, 0xFF, 0xFF };

    /// <summary>
    /// Compresses one chunk as a non-final deflate segment. The whole chunk is handed to the compressor in exactly one
    /// write, so the output depends only on the chunk bytes and the runtime's zlib, never on how the file was buffered.
    /// </summary>
    public static byte[] CompressChunk(byte[] data, int offset, int count)
    {
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

        var ms = new MemoryStream(count / 2);
        byte[] result;
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) {
            ds.Write(new ReadOnlySpan<byte>(data, offset, count));
            ds.Flush(); // Z_SYNC_FLUSH
            result = ms.ToArray(); // before Dispose, whose final block is discarded
        }

        if (!EndsWithSyncFlushMarker(result)) {
            // a runtime whose Flush() does not sync-flush (eg. .NET Framework) cannot produce self-contained chunks
            throw new InvalidOperationException(
                "DeflateStream.Flush() did not emit a sync flush; chunk-aligned packaging is not supported on this runtime.");
        }

        return result;
    }

    /// <summary>Compresses a whole entry as one ordinary, finished deflate stream (entries that carry no chunk records).</summary>
    public static byte[] CompressWhole(byte[] data)
    {
        var ms = new MemoryStream(data.Length / 2);
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) {
            ds.Write(data, 0, data.Length);
        }

        return ms.ToArray();
    }

    private static bool EndsWithSyncFlushMarker(byte[] data)
    {
        if (data.Length < SyncFlushMarker.Length) return false;
        for (int i = 0; i < SyncFlushMarker.Length; i++) {
            if (data[data.Length - SyncFlushMarker.Length + i] != SyncFlushMarker[i]) return false;
        }

        return true;
    }
}
