#nullable enable
namespace Velopack.Packaging.Chunking;

/// <summary>
/// FastCDC-gear-v1 content-defined chunking (delta-v2 spec §3). Runs only at pack time; the boundaries it produces are
/// recorded in the content index, so clients never need to run it. The chunker restarts for every file and the result
/// never depends on how the input is fed (<see cref="ChunkStream"/> slides a buffer of at least <see cref="MaxSize"/>
/// bytes over the input and yields exactly the boundaries <see cref="Chunk(byte[])"/> yields for the whole file).
/// </summary>
public static class FastCdc
{
    public const int MinSize = 16384;
    public const int AvgSize = 65536;
    public const int MaxSize = 262144;

    /// <summary>Strict mask (top 18 bits), used until <see cref="AvgSize"/> bytes.</summary>
    public const ulong MaskS = 0xFFFFC00000000000;

    /// <summary>Relaxed mask (top 14 bits), used from <see cref="AvgSize"/> until <see cref="MaxSize"/> bytes.</summary>
    public const ulong MaskL = 0xFFFC000000000000;

    /// <summary>Receives one chunk: the bytes <c>buffer[offset .. offset + length)</c>, valid only during the call.</summary>
    public delegate void ChunkHandler(byte[] buffer, int offset, int length);

    /// <summary>
    /// Finds the next cut point: the length of the chunk that starts at <paramref name="offset"/>, given that
    /// <paramref name="remaining"/> bytes of the file remain from there. When the caller has buffered at least
    /// <see cref="MaxSize"/> bytes, <paramref name="remaining"/> may be the buffered amount rather than the true remainder.
    /// </summary>
    public static int NextCut(byte[] data, int offset, int remaining)
    {
        if (remaining <= MinSize) {
            return remaining; // EOF tail, may be shorter than MinSize
        }

        var gear = GearTable.Table;
        int end = Math.Min(remaining, MaxSize);
        int normal = Math.Min(remaining, AvgSize);
        ulong h = 0;
        int i = MinSize; // bytes [0, MinSize) are skipped, not hashed

        unchecked {
            for (; i < normal; i++) {
                h = (h << 1) + gear[data[offset + i]];
                if ((h & MaskS) == 0) return i + 1;
            }

            for (; i < end; i++) {
                h = (h << 1) + gear[data[offset + i]];
                if ((h & MaskL) == 0) return i + 1;
            }
        }

        return end; // forced cut at MaxSize (or the end of the file)
    }

    /// <summary>Chunks a whole in-memory buffer and returns the chunk lengths in order (empty for empty input).</summary>
    public static List<int> Chunk(byte[] data)
    {
        var lengths = new List<int>();
        int pos = 0;
        while (pos < data.Length) {
            int cut = NextCut(data, pos, data.Length - pos);
            lengths.Add(cut);
            pos += cut;
        }

        return lengths;
    }

    /// <summary>
    /// Chunks a stream through a sliding buffer, invoking <paramref name="onChunk"/> once per chunk with the chunk's bytes
    /// in one contiguous region. Returns the number of bytes consumed.
    /// </summary>
    public static long ChunkStream(Stream stream, ChunkHandler onChunk)
    {
        // a seekable stream (a file) of known length needs no more buffer than its remaining bytes
        long bufferSize = MaxSize * 2;
        if (stream.CanSeek) bufferSize = Math.Max(1, Math.Min(bufferSize, stream.Length - stream.Position));
        var buffer = new byte[bufferSize];
        int start = 0, end = 0;
        bool eof = false;
        long total = 0;

        while (true) {
            if (!eof && end - start < MaxSize) {
                // keep at least MaxSize bytes (or the rest of the file) ahead of the chunk start, so NextCut sees the
                // same window it would see over the whole file
                if (start > 0) {
                    Buffer.BlockCopy(buffer, start, buffer, 0, end - start);
                    end -= start;
                    start = 0;
                }

                while (end < buffer.Length) {
                    int n = stream.Read(buffer, end, buffer.Length - end);
                    if (n <= 0) {
                        eof = true;
                        break;
                    }

                    end += n;
                }
            }

            int available = end - start;
            if (available == 0) break;

            int cut = NextCut(buffer, start, available);
            onChunk(buffer, start, cut);
            start += cut;
            total += cut;
        }

        return total;
    }
}
