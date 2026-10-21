#nullable enable
using System.Text;
using Velopack.Chunks;

namespace Velopack.Packaging.Chunking;

/// <summary>Where a packed file is installed by the update binary (delta-v2 spec §2.2).</summary>
public enum PackFileDisposition : byte
{
    /// <summary><c>current/&lt;path&gt;</c></summary>
    AppFile = 0,

    /// <summary>The root <c>Update.exe</c> (the package's top-level <c>Squirrel.exe</c>).</summary>
    Updater = 1,

    /// <summary>A root execution stub (<c>&lt;name&gt;_ExecutionStub.exe</c> installed as <c>&lt;name&gt;.exe</c>).</summary>
    Stub = 2,
}

/// <summary>One regular file of the pack directory, as described by the content index.</summary>
public sealed class PackFileEntry
{
    /// <summary>Path relative to the pack directory, with <c>/</c> separators.</summary>
    public string Path { get; }

    public PackFileDisposition Disposition { get; }

    /// <summary>Plaintext length in bytes.</summary>
    public long Length { get; }

    /// <summary>SHA-256 of the plaintext (32 bytes).</summary>
    public byte[] Sha256 { get; }

    /// <summary>CRC-32 (IEEE) of the plaintext, as a zip local header needs it.</summary>
    public uint Crc32 { get; }

    /// <summary>Index of the file's first chunk in <see cref="PackContentIndex.Chunks"/> (0 for an empty file).</summary>
    public int FirstChunk { get; }

    /// <summary>Number of chunks (0 for an empty file).</summary>
    public int ChunkCount { get; }

    public PackFileEntry(string path, PackFileDisposition disposition, long length, byte[] sha256, uint crc32, int firstChunk, int chunkCount)
    {
        if (sha256 == null || sha256.Length != Hash256.Size) throw new ArgumentException("A SHA-256 digest is 32 bytes.", nameof(sha256));
        Path = path;
        Disposition = disposition;
        Length = length;
        Sha256 = sha256;
        Crc32 = crc32;
        FirstChunk = firstChunk;
        ChunkCount = chunkCount;
    }
}

/// <summary>One content-defined chunk of a packed file.</summary>
public readonly struct PackChunkEntry
{
    public int Length { get; }

    /// <summary>SHA-256 of the chunk plaintext (32 bytes).</summary>
    public byte[] Sha256 { get; }

    public PackChunkEntry(int length, byte[] sha256)
    {
        if (length <= 0 || length > ChunkIndex.MaxChunkLength) throw new ArgumentOutOfRangeException(nameof(length));
        if (sha256 == null || sha256.Length != Hash256.Size) throw new ArgumentException("A SHA-256 digest is 32 bytes.", nameof(sha256));
        Length = length;
        Sha256 = sha256;
    }
}

/// <summary>
/// The in-memory result of pass 1 of a chunk-aligned pack (delta-v2 spec §4.1): every regular file of the pack directory
/// with its disposition, hashes, CRC-32 and chunk table. <see cref="SerializeSectionA"/> produces the content map that
/// is installed as <c>current/sq.chunks</c> and forms the first part of the package trailer.
/// </summary>
public sealed class PackContentIndex
{
    private readonly Dictionary<string, PackFileEntry> _byPath;

    /// <summary>File records in strictly ascending ordinal order of their UTF-8 paths.</summary>
    public IReadOnlyList<PackFileEntry> Files { get; }

    /// <summary>Chunk records in file order (every file's chunks are consecutive).</summary>
    public IReadOnlyList<PackChunkEntry> Chunks { get; }

    public PackContentIndex(IReadOnlyList<PackFileEntry> files, IReadOnlyList<PackChunkEntry> chunks)
    {
        Files = files;
        Chunks = chunks;
        _byPath = new Dictionary<string, PackFileEntry>(StringComparer.Ordinal);
        int nextChunk = 0;
        PackFileEntry? previous = null;
        foreach (var file in files) {
            if (previous != null && CompareUtf8(previous.Path, file.Path) >= 0) {
                throw new ArgumentException($"File records are not strictly ascending: '{previous.Path}' >= '{file.Path}'.", nameof(files));
            }

            if (file.ChunkCount == 0) {
                if (file.Length != 0 || file.FirstChunk != 0) {
                    throw new ArgumentException($"'{file.Path}' has no chunks but a length.", nameof(files));
                }
            } else {
                if (file.FirstChunk != nextChunk) throw new ArgumentException($"'{file.Path}' chunks are not in file order.", nameof(files));
                long sum = 0;
                for (int c = 0; c < file.ChunkCount; c++) sum += chunks[nextChunk + c].Length;
                if (sum != file.Length) throw new ArgumentException($"'{file.Path}' length does not equal the sum of its chunks.", nameof(files));
                nextChunk += file.ChunkCount;
            }

            _byPath.Add(file.Path, file);
            previous = file;
        }

        if (nextChunk != chunks.Count) throw new ArgumentException("Chunk records not owned by any file.", nameof(chunks));
    }

    public long TotalLength => Files.Sum(f => f.Length);

    public PackFileEntry? FindFile(string path) => _byPath.TryGetValue(path, out var f) ? f : null;

    /// <summary>Length in bytes of the serialised Section A.</summary>
    public int SectionALength =>
        ChunkIndex.AHeaderSize + PathBlobLength + (Files.Count * ChunkIndex.FileRecordSize) + (Chunks.Count * ChunkIndex.ChunkRecordSize);

    private int PathBlobLength => Files.Sum(f => Encoding.UTF8.GetByteCount(f.Path));

    /// <summary>Serialises Section A, the content map (delta-v2 spec §2.2).</summary>
    public byte[] SerializeSectionA()
    {
        if (Files.Count > ChunkIndex.MaxFileCount) throw new InvalidOperationException($"Too many files for a chunk index ({Files.Count}).");
        if (Chunks.Count > ChunkIndex.MaxChunkCount) throw new InvalidOperationException($"Too many chunks for a chunk index ({Chunks.Count}).");
        int blobLength = PathBlobLength;
        if (blobLength > ChunkIndex.MaxPathBlobLength) throw new InvalidOperationException("Path blob exceeds the chunk index cap.");

        var data = new byte[SectionALength];
        data[0] = 0x56; // "VPKA"
        data[1] = 0x50;
        data[2] = 0x4B;
        data[3] = 0x41;
        LittleEndian.WriteUInt16(data, 4, ChunkIndex.FormatVersion);
        data[6] = ChunkIndex.HashAlgoSha256;
        data[7] = ChunkIndex.CdcAlgoFastCdcGearV1;
        LittleEndian.WriteUInt32(data, 8, 0); // flags
        LittleEndian.WriteUInt32(data, 12, FastCdc.MinSize);
        LittleEndian.WriteUInt32(data, 16, FastCdc.AvgSize);
        LittleEndian.WriteUInt32(data, 20, FastCdc.MaxSize);
        LittleEndian.WriteUInt32(data, 24, (uint) Files.Count);
        LittleEndian.WriteUInt32(data, 28, (uint) Chunks.Count);
        LittleEndian.WriteUInt32(data, 32, (uint) blobLength);
        // bytes 36..48 reserved (zero)

        int blobPos = ChunkIndex.AHeaderSize;
        int recordPos = ChunkIndex.AHeaderSize + blobLength;
        foreach (var file in Files) {
            int pathBytes = Encoding.UTF8.GetBytes(file.Path, 0, file.Path.Length, data, blobPos);
            LittleEndian.WriteUInt32(data, recordPos, (uint) (blobPos - ChunkIndex.AHeaderSize));
            LittleEndian.WriteUInt16(data, recordPos + 4, (ushort) pathBytes);
            data[recordPos + 6] = (byte) file.Disposition;
            data[recordPos + 7] = (byte) ChunkFileKind.File;
            LittleEndian.WriteUInt64(data, recordPos + 8, (ulong) file.Length);
            LittleEndian.WriteUInt32(data, recordPos + 16, (uint) file.FirstChunk);
            LittleEndian.WriteUInt32(data, recordPos + 20, (uint) file.ChunkCount);
            // target_off / target_len / mode are 0 for Windows packs
            Buffer.BlockCopy(file.Sha256, 0, data, recordPos + 32, Hash256.Size);
            blobPos += pathBytes;
            recordPos += ChunkIndex.FileRecordSize;
        }

        foreach (var chunk in Chunks) {
            LittleEndian.WriteUInt32(data, recordPos, (uint) chunk.Length);
            Buffer.BlockCopy(chunk.Sha256, 0, data, recordPos + 4, Hash256.Size);
            recordPos += ChunkIndex.ChunkRecordSize;
        }

        return data;
    }

    /// <summary>Compares two paths by the ordinal order of their UTF-8 bytes (the record order of the index).</summary>
    public static int CompareUtf8(string a, string b)
    {
        return CompareBytes(Encoding.UTF8.GetBytes(a), Encoding.UTF8.GetBytes(b));
    }

    public static int CompareBytes(byte[] a, byte[] b)
    {
        int n = Math.Min(a.Length, b.Length);
        for (int i = 0; i < n; i++) {
            int d = a[i] - b[i];
            if (d != 0) return d;
        }

        return a.Length - b.Length;
    }
}
