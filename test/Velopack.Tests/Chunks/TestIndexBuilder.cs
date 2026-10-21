#nullable enable
using System.Security.Cryptography;
using System.Text;
using Velopack.Chunks;

namespace Velopack.Tests.Chunks;

/// <summary>
/// Serialises delta-v2 chunk indexes (Section A and A‖B trailers) from a plain description, without validating
/// anything, so tests can build both valid and deliberately broken indexes. Transport (Section B) values are
/// synthetic: nothing here compresses data, the builder only lays entries out contiguously like the zip writer would.
/// </summary>
internal sealed class TestIndexBuilder
{
    public sealed class Chunk
    {
        public int Length;
        public byte[] Sha256 = new byte[32];
        public long Offset;
        public int TransportLength;
    }

    public sealed class Entry
    {
        public string Path = "";
        public ChunkFileDisposition Disposition;
        public ChunkFileKind Kind;
        public long Length;
        public List<Chunk> Chunks = new();
        public string? Target;
        public ushort Mode { get; set; }
        public byte[] Sha256 = new byte[32];
        public byte Method;
        public long DataOffset;
        public long CompressedSize;
        /// <summary>Raw overrides, applied verbatim when set (used to build invalid fixtures).</summary>
        public uint? FirstChunkOverride { get; set; }
        public uint? ChunkCountOverride;
        public ushort? TargetLengthOverride;

        public byte[] PathBytes => Encoding.UTF8.GetBytes(Path);
    }

    public ushort FormatVersion = 1;
    public byte HashAlgo = 1;
    public byte CdcAlgo = 1;
    public uint Flags { get; set; }
    public uint CdcMin = 16384, CdcAvg = 65536, CdcMax = 262144;
    public uint? FileCountOverride { get; set; }
    public uint? ChunkCountOverride { get; set; }
    public uint? BFileCountOverride;
    /// <summary>Sort entries by the ordinal byte order of their UTF-8 path (the format's required order).</summary>
    public bool Sort = true;
    public List<Entry> Entries = new();

    private static readonly byte[] MagicA = Encoding.ASCII.GetBytes("VPKA");
    private static readonly byte[] MagicB = Encoding.ASCII.GetBytes("VPKB");

    public Entry AddFile(string path, byte[] content, ChunkFileDisposition disposition = ChunkFileDisposition.AppFile, bool deflate = true,
        int[]? chunkLengths = null, int chunkSize = 65536)
    {
        var e = new Entry {
            Path = path, Disposition = disposition, Kind = ChunkFileKind.File, Length = content.Length, Sha256 = SHA256.HashData(content),
        };
        if (chunkLengths == null) {
            var list = new List<int>();
            for (int pos = 0; pos < content.Length; pos += chunkSize) list.Add(Math.Min(chunkSize, content.Length - pos));
            chunkLengths = list.ToArray();
        }
        if (chunkLengths.Sum() != content.Length) throw new ArgumentException("chunk lengths must sum to the content length");
        int offset = 0;
        foreach (var len in chunkLengths) {
            var c = new Chunk { Length = len, Sha256 = SHA256.HashData(content.AsSpan(offset, len)) };
            // synthetic compressed size: roughly 40 % plus a little, at least 1 byte
            c.TransportLength = deflate ? Math.Max(1, (len * 2 / 5) + 5) : len;
            e.Chunks.Add(c);
            offset += len;
        }
        e.Method = content.Length == 0 ? (byte) 0 : (deflate ? ChunkIndex.MethodDeflate : ChunkIndex.MethodStored);
        Entries.Add(e);
        return e;
    }

    public Entry AddSymlink(string path, string target)
    {
        var targetBytes = Encoding.UTF8.GetBytes(target);
        var e = new Entry {
            Path = path, Kind = ChunkFileKind.Symlink, Target = target, Length = targetBytes.Length, Sha256 = SHA256.HashData(targetBytes),
        };
        Entries.Add(e);
        return e;
    }

    public Entry AddEmptyDir(string path)
    {
        var e = new Entry { Path = path, Kind = ChunkFileKind.EmptyDir };
        Entries.Add(e);
        return e;
    }

    /// <summary>
    /// Assigns transport offsets as a zip writer would: entries back to back from <paramref name="firstLocalHeader"/>,
    /// each preceded by a 30-byte local header plus its name. Returns the offset just past the last entry.
    /// </summary>
    public long Layout(long firstLocalHeader = 1000)
    {
        long pos = firstLocalHeader;
        foreach (var e in Ordered()) {
            if (e.Kind != ChunkFileKind.File || e.Length == 0) {
                e.DataOffset = 0;
                e.CompressedSize = 0;
                e.Method = 0;
                continue;
            }
            pos += 30 + e.PathBytes.Length;
            e.DataOffset = pos;
            long cursor = pos;
            foreach (var c in e.Chunks) {
                c.Offset = cursor;
                cursor += c.TransportLength;
            }
            e.CompressedSize = e.Method == ChunkIndex.MethodDeflate ? cursor - pos + 2 : e.Length;
            pos += e.CompressedSize;
        }
        return pos;
    }

    public List<Entry> Ordered()
    {
        var list = new List<Entry>(Entries);
        if (Sort) list.Sort((a, b) => CompareBytes(a.PathBytes, b.PathBytes));
        return list;
    }

    public static int CompareBytes(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) {
            if (a[i] != b[i]) return a[i] - b[i];
        }
        return a.Length - b.Length;
    }

    public byte[] BuildSectionA()
    {
        var entries = Ordered();
        var blob = new MemoryStream();
        var blobIndex = new Dictionary<string, uint>(StringComparer.Ordinal);
        uint Intern(string s)
        {
            if (blobIndex.TryGetValue(s, out var off)) return off;
            off = (uint) blob.Length;
            var bytes = Encoding.UTF8.GetBytes(s);
            blob.Write(bytes, 0, bytes.Length);
            blobIndex[s] = off;
            return off;
        }

        var files = new MemoryStream();
        var chunks = new MemoryStream();
        var fw = new BinaryWriter(files);
        var cw = new BinaryWriter(chunks);
        uint nextChunk = 0;
        foreach (var e in entries) {
            uint pathOff = Intern(e.Path);
            uint targetOff = e.Target != null ? Intern(e.Target) : 0;
            fw.Write(pathOff);
            fw.Write((ushort) e.PathBytes.Length);
            fw.Write((byte) e.Disposition);
            fw.Write((byte) e.Kind);
            fw.Write((ulong) e.Length);
            fw.Write(e.FirstChunkOverride ?? (e.Chunks.Count > 0 ? nextChunk : 0));
            fw.Write(e.ChunkCountOverride ?? (uint) e.Chunks.Count);
            fw.Write(targetOff);
            fw.Write(e.TargetLengthOverride ?? (ushort) (e.Target != null ? Encoding.UTF8.GetByteCount(e.Target) : 0));
            fw.Write(e.Mode);
            fw.Write(e.Sha256);
            foreach (var c in e.Chunks) {
                cw.Write((uint) c.Length);
                cw.Write(c.Sha256);
                nextChunk++;
            }
        }

        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        w.Write(MagicA);
        w.Write(FormatVersion);
        w.Write(HashAlgo);
        w.Write(CdcAlgo);
        w.Write(Flags);
        w.Write(CdcMin);
        w.Write(CdcAvg);
        w.Write(CdcMax);
        w.Write(FileCountOverride ?? (uint) entries.Count);
        w.Write(ChunkCountOverride ?? nextChunk);
        w.Write((uint) blob.Length);
        w.Write(new byte[12]);
        blob.WriteTo(ms);
        files.WriteTo(ms);
        chunks.WriteTo(ms);
        return ms.ToArray();
    }

    public byte[] BuildSectionB()
    {
        var entries = Ordered();
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        uint chunkCount = (uint) entries.Sum(e => e.Chunks.Count);
        w.Write(MagicB);
        w.Write(FormatVersion);
        w.Write((ushort) 0);
        w.Write(BFileCountOverride ?? (uint) entries.Count);
        w.Write(chunkCount);
        foreach (var e in entries) {
            w.Write(e.Method);
            w.Write(new byte[7]);
            w.Write((ulong) e.DataOffset);
            w.Write((ulong) e.CompressedSize);
        }
        foreach (var e in entries) {
            foreach (var c in e.Chunks) {
                w.Write((ulong) c.Offset);
                w.Write((uint) c.TransportLength);
            }
        }
        return ms.ToArray();
    }

    public byte[] BuildTrailer()
    {
        var a = BuildSectionA();
        var b = BuildSectionB();
        var t = new byte[a.Length + b.Length];
        Buffer.BlockCopy(a, 0, t, 0, a.Length);
        Buffer.BlockCopy(b, 0, t, a.Length, b.Length);
        return t;
    }

    /// <summary>Length of Section A inside a trailer, from its header.</summary>
    public static int SectionALength(byte[] trailer)
    {
        uint fc = BitConverter.ToUInt32(trailer, 24);
        uint cc = BitConverter.ToUInt32(trailer, 28);
        uint pbl = BitConverter.ToUInt32(trailer, 32);
        return (int) (48 + pbl + (64 * fc) + (36 * cc));
    }

    public static byte[] SectionAOf(byte[] trailer) => trailer.AsSpan(0, SectionALength(trailer)).ToArray();

    /// <summary>Byte offset of file record <paramref name="index"/> inside a Section A / trailer buffer.</summary>
    public static int FileRecordOffset(byte[] data, int index) => 48 + (int) BitConverter.ToUInt32(data, 32) + (index * 64);

    /// <summary>Byte offset of chunk record <paramref name="index"/> inside a Section A / trailer buffer.</summary>
    public static int ChunkRecordOffset(byte[] data, int index)
    {
        uint fc = BitConverter.ToUInt32(data, 24);
        return FileRecordOffset(data, 0) + ((int) fc * 64) + (index * 36);
    }

    /// <summary>Deterministic pseudo-random bytes: the SplitMix64 stream for <paramref name="seed"/>, each output as 8 LE bytes.</summary>
    public static byte[] DeterministicBytes(ulong seed, int length)
    {
        var result = new byte[length];
        ulong state = seed;
        int pos = 0;
        while (pos < length) {
            ulong z = SplitMix64Next(ref state);
            for (int i = 0; i < 8 && pos < length; i++, pos++) {
                result[pos] = (byte) (z >> (8 * i));
            }
        }
        return result;
    }

    public static ulong SplitMix64Next(ref ulong state)
    {
        unchecked {
            state += 0x9E3779B97F4A7C15UL;
            ulong z = state;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }
    }
}
