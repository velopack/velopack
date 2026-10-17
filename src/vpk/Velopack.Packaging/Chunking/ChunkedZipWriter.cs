#nullable enable
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using Velopack.Chunks;

namespace Velopack.Packaging.Chunking;

/// <summary>Where the chunk index trailer landed in the written package (the feed pointer, delta-v2 spec §2.6).</summary>
public sealed class ChunkIndexPointer
{
    /// <summary>Byte offset of the trailer data inside the .nupkg.</summary>
    public long Offset { get; }

    /// <summary>Byte length of the trailer data.</summary>
    public long Length { get; }

    /// <summary>Upper-case hex SHA-256 of the trailer bytes.</summary>
    public string Sha256 { get; }

    public ChunkIndexPointer(long offset, long length, string sha256)
    {
        Offset = offset;
        Length = length;
        Sha256 = sha256;
    }
}

/// <summary>
/// Pass 2 of a chunk-aligned pack (delta-v2 spec §4): writes the release <c>.nupkg</c> from the staging directory.
/// Every file described by the content index is compressed chunk by chunk (<see cref="ChunkDeflate"/>) so that each
/// chunk occupies a known byte range of the archive, which the transport map (Section B) records; all other entries are
/// ordinary deflate/stored entries. The index (Section A ‖ Section B) is appended as the stored, last entry
/// <c>package/velopack/chunks.idx</c>. The output is a plain zip: no data descriptors, standard Zip64 where needed,
/// UTF-8 names and 1980 timestamps exactly like <c>EasyZip</c>, and an entry order that keeps the root
/// <c>.nuspec</c> and <c>lib/app/Squirrel.exe</c> the first suffix matches for every existing reader.
/// </summary>
public static class ChunkedZipWriter
{
    public const string TrailerEntryName = ChunkIndex.TrailerEntryName;
    public const string DefaultAppDirPrefix = "lib/app/";

    private const int MaxChunksInFlight = 32;
    private const ushort VersionDefault = 20;
    private const ushort VersionZip64 = 45;
    private const ushort FlagUtf8 = 0x0800;
    private const ushort DosDate1980 = 0x0021;
    private const uint Zip64Sentinel32 = 0xFFFFFFFF;
    private const ushort Zip64Sentinel16 = 0xFFFF;
    private const ushort MethodStored = ChunkIndex.MethodStored;
    private const ushort MethodDeflate = ChunkIndex.MethodDeflate;

    private sealed class Entry
    {
        public Entry(string name, string? fullPath, PackFileEntry? record)
        {
            Name = name;
            NameBytes = Encoding.UTF8.GetBytes(name);
            FullPath = fullPath;
            Record = record;
        }

        public string Name { get; }
        public byte[] NameBytes { get; }

        /// <summary>Source file on disk; null for a directory entry or the trailer.</summary>
        public string? FullPath { get; }

        /// <summary>The content index record for a chunked entry, null for everything else.</summary>
        public PackFileEntry? Record { get; }

        public bool IsDirectory => Name.EndsWith("/", StringComparison.Ordinal);

        // filled while writing
        public ushort Method;
        public uint Crc32;
        public long UncompressedSize;
        public long CompressedSize;
        public long LocalHeaderOffset;
        public long DataOffset;
        public bool IsZip64;
    }

    /// <summary>
    /// Writes <paramref name="outputPath"/> from <paramref name="stagingDir"/>. Every file under
    /// <paramref name="appDirPrefix"/> must have a record in <paramref name="index"/> (except <c>sq.chunks</c>, which must be
    /// byte-identical to the index's Section A), and every record must have its file. The trailer bytes are also written
    /// to <paramref name="trailerOutputPath"/> when given.
    /// </summary>
    public static ChunkIndexPointer Write(string stagingDir, PackContentIndex index, string outputPath, string? trailerOutputPath = null,
        Action<int>? progress = null, string appDirPrefix = DefaultAppDirPrefix)
    {
        progress ??= _ => { };
        var entries = CollectEntries(stagingDir, index, appDirPrefix);
        byte[] sectionA = index.SerializeSectionA();
        long totalBytes = entries.Sum(e => e.Record?.Length ?? 0) + sectionA.Length;
        long processedBytes = 0;

        var bMethod = new byte[index.Files.Count];
        var bDataOffset = new long[index.Files.Count];
        var bCompressedSize = new long[index.Files.Count];
        var bChunkOffset = new long[index.Chunks.Count];
        var bChunkLength = new int[index.Chunks.Count];
        var fileIndexOf = new Dictionary<PackFileEntry, int>();
        for (int i = 0; i < index.Files.Count; i++) fileIndexOf[index.Files[i]] = i;
        int recordsWritten = 0;

        try {
            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.ReadWrite, FileShare.None, 1024 * 1024);

            foreach (var entry in entries) {
                if (entry.Record != null) {
                    int fi = fileIndexOf[entry.Record];
                    WriteChunkedEntry(output, entry, index, bChunkOffset, bChunkLength);
                    bMethod[fi] = (byte) entry.Method;
                    bDataOffset[fi] = entry.Record.Length == 0 ? 0 : entry.DataOffset;
                    bCompressedSize[fi] = entry.Record.Length == 0 ? 0 : entry.CompressedSize;
                    recordsWritten++;
                    processedBytes += entry.Record.Length;
                } else if (entry.IsDirectory) {
                    WriteBufferedEntry(output, entry, Array.Empty<byte>());
                } else {
                    byte[] plain = File.ReadAllBytes(entry.FullPath!);
                    if (entry.Name == appDirPrefix + ChunkIndexBuilder.SqChunksFileName && !plain.AsSpan().SequenceEqual(sectionA)) {
                        throw new InvalidOperationException($"'{entry.Name}' in the staging directory does not match the content index.");
                    }

                    WriteBufferedEntry(output, entry, plain);
                }

                progress((int) ((double) processedBytes / Math.Max(1, totalBytes) * 100));
            }

            if (recordsWritten != index.Files.Count) {
                throw new InvalidOperationException($"{index.Files.Count - recordsWritten} indexed file(s) are missing from the staging directory.");
            }

            // the trailer: A ‖ B, stored, last
            byte[] trailer = BuildTrailer(sectionA, index, bMethod, bDataOffset, bCompressedSize, bChunkOffset, bChunkLength);
            var trailerEntry = new Entry(TrailerEntryName, null, null);
            WriteBufferedEntry(output, trailerEntry, trailer, forceStored: true);
            entries.Add(trailerEntry);

            WriteCentralDirectory(output, entries);
            output.Flush();

            if (trailerOutputPath != null) File.WriteAllBytes(trailerOutputPath, trailer);
            progress(100);
            return new ChunkIndexPointer(trailerEntry.DataOffset, trailer.Length, Convert.ToHexString(SHA256.HashData(trailer)));
        } catch {
            try { File.Delete(outputPath); } catch { }

            throw;
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // entry collection and ordering (§4.3)
    // ---------------------------------------------------------------------------------------------------------------

    private static List<Entry> CollectEntries(string stagingDir, PackContentIndex index, string appDirPrefix)
    {
        var root = new DirectoryInfo(stagingDir);
        if (!root.Exists) throw new ArgumentException($"Staging directory does not exist: {stagingDir}", nameof(stagingDir));

        var all = new List<Entry>();
        Collect(root, root.FullName, index, appDirPrefix, all);

        var rootEntries = new List<Entry>();
        var libEntries = new List<Entry>();
        Entry? updater = null;
        string updaterName = appDirPrefix + ChunkIndexBuilder.UpdaterFileName;
        foreach (var e in all) {
            if (e.Name == TrailerEntryName) throw new InvalidOperationException($"The staging directory already contains '{TrailerEntryName}'.");
            if (e.Name == updaterName) updater = e;
            else if (e.Name.StartsWith("lib/", StringComparison.Ordinal)) libEntries.Add(e);
            else rootEntries.Add(e);
        }

        Comparison<Entry> byUtf8 = (x, y) => PackContentIndex.CompareBytes(x.NameBytes, y.NameBytes);
        rootEntries.Sort(byUtf8);
        libEntries.Sort(byUtf8);

        var ordered = new List<Entry>(all.Count + 1);
        ordered.AddRange(rootEntries);
        if (updater != null) ordered.Add(updater);
        ordered.AddRange(libEntries);
        return ordered;
    }

    private static void Collect(DirectoryInfo dir, string rootPath, PackContentIndex index, string appDirPrefix, List<Entry> entries)
    {
        foreach (var subDir in dir.EnumerateDirectories("*", SearchOption.TopDirectoryOnly)) {
            if (IsDirEmpty(subDir)) {
                // parity with EasyZip: an empty directory (no files at any depth) gets one "name/" entry
                entries.Add(new Entry(RelativeName(subDir.FullName, rootPath) + "/", null, null));
            } else {
                Collect(subDir, rootPath, index, appDirPrefix, entries);
            }
        }

        foreach (var file in dir.EnumerateFiles("*", SearchOption.TopDirectoryOnly)) {
            string name = RelativeName(file.FullName, rootPath);
            PackFileEntry? record = null;
            if (name.StartsWith(appDirPrefix, StringComparison.Ordinal)) {
                string appPath = name.Substring(appDirPrefix.Length);
                record = index.FindFile(appPath);
                if (record == null && appPath != ChunkIndexBuilder.SqChunksFileName) {
                    throw new InvalidOperationException($"'{name}' is not described by the content index.");
                }

                if (record != null && record.Length != file.Length) {
                    throw new InvalidOperationException($"'{name}' changed since it was indexed ({file.Length} bytes vs {record.Length}).");
                }
            }

            entries.Add(new Entry(name, file.FullName, record));
        }
    }

    private static string RelativeName(string fullPath, string rootPath)
    {
        return fullPath.Substring(rootPath.Length).TrimStart('\\', '/').Replace('\\', '/');
    }

    private static bool IsDirEmpty(DirectoryInfo dir)
    {
        return !dir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).Any();
    }

    // ---------------------------------------------------------------------------------------------------------------
    // entries (§4.1)
    // ---------------------------------------------------------------------------------------------------------------

    private static void WriteChunkedEntry(FileStream output, Entry entry, PackContentIndex index, long[] bChunkOffset, int[] bChunkLength)
    {
        var record = entry.Record!;
        entry.Crc32 = record.Crc32;
        entry.UncompressedSize = record.Length;
        entry.LocalHeaderOffset = output.Position;
        entry.IsZip64 = record.Length >= Zip64Sentinel32 || entry.LocalHeaderOffset >= Zip64Sentinel32;

        if (record.Length == 0) {
            // zero-length: stored, 0 bytes, CRC 0, no ranges
            entry.Method = MethodStored;
            entry.CompressedSize = 0;
            WriteLocalHeader(output, entry);
            entry.DataOffset = output.Position;
            return;
        }

        using var input = new FileStream(entry.FullPath!, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan);
        int end = record.FirstChunk + record.ChunkCount;
        var buffers = new byte[MaxChunksInFlight][];
        var compressed = new byte[MaxChunksInFlight][];

        if (record.ChunkCount <= MaxChunksInFlight) {
            // the whole file fits in one batch: compress it first, so the method and sizes are final before the header
            // is written and no seek-back is needed (the common case; identical output to the streaming path below)
            CompressBatch(input, index, record.FirstChunk, record.ChunkCount, buffers, compressed);
            long deflatedSize = ChunkDeflate.FinalBlock.Length;
            for (int i = 0; i < record.ChunkCount; i++) deflatedSize += compressed[i].Length;

            if (deflatedSize < record.Length) {
                entry.Method = MethodDeflate;
                entry.CompressedSize = deflatedSize;
                WriteLocalHeader(output, entry);
                entry.DataOffset = output.Position;
                WriteBatch(output, record.FirstChunk, record.ChunkCount, compressed, bChunkOffset, bChunkLength);
                output.Write(ChunkDeflate.FinalBlock, 0, ChunkDeflate.FinalBlock.Length);
            } else {
                entry.Method = MethodStored;
                entry.CompressedSize = record.Length;
                WriteLocalHeader(output, entry);
                entry.DataOffset = output.Position;
                long plainOffset = 0;
                for (int i = 0; i < record.ChunkCount; i++) {
                    bChunkOffset[record.FirstChunk + i] = entry.DataOffset + plainOffset;
                    bChunkLength[record.FirstChunk + i] = buffers[i].Length;
                    output.Write(buffers[i], 0, buffers[i].Length);
                    plainOffset += buffers[i].Length;
                }
            }

            return;
        }

        // streaming path: write the header with a placeholder compressed size, compress chunk batches as they are read,
        // then patch the header (or fall back to a stored entry) once the real size is known
        entry.Method = MethodDeflate;
        entry.CompressedSize = 0;
        WriteLocalHeader(output, entry);
        entry.DataOffset = output.Position;

        int chunk = record.FirstChunk;
        while (chunk < end) {
            int batch = Math.Min(MaxChunksInFlight, end - chunk);
            CompressBatch(input, index, chunk, batch, buffers, compressed);
            WriteBatch(output, chunk, batch, compressed, bChunkOffset, bChunkLength);
            chunk += batch;
        }

        if (input.Position != record.Length) {
            throw new InvalidOperationException($"'{entry.Name}' is shorter than its index record.");
        }

        output.Write(ChunkDeflate.FinalBlock, 0, ChunkDeflate.FinalBlock.Length);
        entry.CompressedSize = output.Position - entry.DataOffset;

        if (entry.CompressedSize >= record.Length) {
            // deflate did not help: store the plaintext instead, chunk ranges then map 1:1 onto it
            output.Seek(entry.DataOffset, SeekOrigin.Begin);
            output.SetLength(entry.DataOffset);
            input.Seek(0, SeekOrigin.Begin);
            long plainOffset = 0;
            for (int c = record.FirstChunk; c < end; c++) {
                int length = index.Chunks[c].Length;
                bChunkOffset[c] = entry.DataOffset + plainOffset;
                bChunkLength[c] = length;
                plainOffset += length;
            }

            CopyExactly(input, output, record.Length);
            entry.Method = MethodStored;
            entry.CompressedSize = record.Length;
        }

        // patch method and compressed size in the local header (CRC and uncompressed size were right already)
        long endOfData = output.Position;
        PatchLocalHeader(output, entry);
        output.Seek(endOfData, SeekOrigin.Begin);
    }

    /// <summary>Reads <paramref name="count"/> chunks starting at chunk <paramref name="first"/> and compresses them in parallel.</summary>
    private static void CompressBatch(Stream input, PackContentIndex index, int first, int count, byte[][] buffers, byte[][] compressed)
    {
        for (int i = 0; i < count; i++) {
            int length = index.Chunks[first + i].Length;
            var buffer = new byte[length];
            ReadExactly(input, buffer, length);
            buffers[i] = buffer;
        }

        if (count == 1) {
            compressed[0] = ChunkDeflate.CompressChunk(buffers[0], 0, buffers[0].Length);
        } else {
            Parallel.For(0, count, i => compressed[i] = ChunkDeflate.CompressChunk(buffers[i], 0, buffers[i].Length));
        }
    }

    private static void WriteBatch(FileStream output, int first, int count, byte[][] compressed, long[] bChunkOffset, int[] bChunkLength)
    {
        for (int i = 0; i < count; i++) {
            bChunkOffset[first + i] = output.Position;
            bChunkLength[first + i] = compressed[i].Length;
            output.Write(compressed[i], 0, compressed[i].Length);
        }
    }

    private static void WriteBufferedEntry(FileStream output, Entry entry, byte[] plain, bool forceStored = false)
    {
        entry.Crc32 = plain.Length == 0 ? 0 : Crc32.HashToUInt32(plain);
        entry.UncompressedSize = plain.Length;
        byte[] data = plain;
        entry.Method = MethodStored;
        if (!forceStored && plain.Length > 0) {
            byte[] deflated = ChunkDeflate.CompressWhole(plain);
            if (deflated.Length < plain.Length) {
                data = deflated;
                entry.Method = MethodDeflate;
            }
        }

        entry.CompressedSize = data.Length;
        entry.LocalHeaderOffset = output.Position;
        entry.IsZip64 = entry.UncompressedSize >= Zip64Sentinel32 || entry.LocalHeaderOffset >= Zip64Sentinel32;
        WriteLocalHeader(output, entry);
        entry.DataOffset = output.Position;
        output.Write(data, 0, data.Length);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // headers (§4.3)
    // ---------------------------------------------------------------------------------------------------------------

    private static void WriteLocalHeader(Stream output, Entry entry)
    {
        var w = new ZipWriter(30 + entry.NameBytes.Length + (entry.IsZip64 ? 20 : 0));
        w.U32(0x04034B50);
        w.U16(entry.IsZip64 ? VersionZip64 : VersionDefault);
        w.U16(FlagUtf8);
        w.U16(entry.Method);
        w.U16(0); // mod time
        w.U16(DosDate1980);
        w.U32(entry.Crc32);
        w.U32(entry.IsZip64 ? Zip64Sentinel32 : (uint) entry.CompressedSize);
        w.U32(entry.IsZip64 ? Zip64Sentinel32 : (uint) entry.UncompressedSize);
        w.U16((ushort) entry.NameBytes.Length);
        w.U16((ushort) (entry.IsZip64 ? 20 : 0));
        w.Bytes(entry.NameBytes);
        if (entry.IsZip64) {
            w.U16(0x0001);
            w.U16(16);
            w.U64((ulong) entry.UncompressedSize);
            w.U64((ulong) entry.CompressedSize);
        }

        w.WriteTo(output);
    }

    private static void PatchLocalHeader(FileStream output, Entry entry)
    {
        var method = new byte[2];
        LittleEndian.WriteUInt16(method, 0, entry.Method);
        output.Seek(entry.LocalHeaderOffset + 8, SeekOrigin.Begin);
        output.Write(method, 0, 2);

        if (entry.IsZip64) {
            var size = new byte[8];
            LittleEndian.WriteUInt64(size, 0, (ulong) entry.CompressedSize);
            output.Seek(entry.LocalHeaderOffset + 30 + entry.NameBytes.Length + 4 + 8, SeekOrigin.Begin);
            output.Write(size, 0, 8);
        } else {
            var size = new byte[4];
            LittleEndian.WriteUInt32(size, 0, (uint) entry.CompressedSize);
            output.Seek(entry.LocalHeaderOffset + 18, SeekOrigin.Begin);
            output.Write(size, 0, 4);
        }
    }

    private static void WriteCentralDirectory(FileStream output, List<Entry> entries)
    {
        long cdOffset = output.Position;
        foreach (var entry in entries) {
            var w = new ZipWriter(46 + entry.NameBytes.Length + (entry.IsZip64 ? 28 : 0));
            w.U32(0x02014B50);
            w.U16(VersionDefault); // version made by (MS-DOS attributes)
            w.U16(entry.IsZip64 ? VersionZip64 : VersionDefault);
            w.U16(FlagUtf8);
            w.U16(entry.Method);
            w.U16(0);
            w.U16(DosDate1980);
            w.U32(entry.Crc32);
            w.U32(entry.IsZip64 ? Zip64Sentinel32 : (uint) entry.CompressedSize);
            w.U32(entry.IsZip64 ? Zip64Sentinel32 : (uint) entry.UncompressedSize);
            w.U16((ushort) entry.NameBytes.Length);
            w.U16((ushort) (entry.IsZip64 ? 28 : 0));
            w.U16(0); // comment length
            w.U16(0); // disk number start
            w.U16(0); // internal attributes
            w.U32(0); // external attributes
            w.U32(entry.IsZip64 ? Zip64Sentinel32 : (uint) entry.LocalHeaderOffset);
            w.Bytes(entry.NameBytes);
            if (entry.IsZip64) {
                w.U16(0x0001);
                w.U16(24);
                w.U64((ulong) entry.UncompressedSize);
                w.U64((ulong) entry.CompressedSize);
                w.U64((ulong) entry.LocalHeaderOffset);
            }

            w.WriteTo(output);
        }

        long cdSize = output.Position - cdOffset;
        bool zip64 = entries.Count >= Zip64Sentinel16 || cdSize >= Zip64Sentinel32 || cdOffset >= Zip64Sentinel32;
        if (zip64) {
            long zip64EocdOffset = output.Position;
            var r = new ZipWriter(56);
            r.U32(0x06064B50);
            r.U64(44); // size of the remainder of this record
            r.U16(VersionZip64);
            r.U16(VersionZip64);
            r.U32(0); // this disk
            r.U32(0); // disk with the central directory
            r.U64((ulong) entries.Count);
            r.U64((ulong) entries.Count);
            r.U64((ulong) cdSize);
            r.U64((ulong) cdOffset);
            r.WriteTo(output);

            var l = new ZipWriter(20);
            l.U32(0x07064B50);
            l.U32(0);
            l.U64((ulong) zip64EocdOffset);
            l.U32(1);
            l.WriteTo(output);
        }

        var e = new ZipWriter(22);
        e.U32(0x06054B50);
        e.U16(0);
        e.U16(0);
        e.U16(zip64 ? Zip64Sentinel16 : (ushort) entries.Count);
        e.U16(zip64 ? Zip64Sentinel16 : (ushort) entries.Count);
        e.U32(zip64 ? Zip64Sentinel32 : (uint) cdSize);
        e.U32(zip64 ? Zip64Sentinel32 : (uint) cdOffset);
        e.U16(0); // no archive comment
        e.WriteTo(output);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // trailer (§2.3)
    // ---------------------------------------------------------------------------------------------------------------

    private static byte[] BuildTrailer(byte[] sectionA, PackContentIndex index, byte[] bMethod, long[] bDataOffset, long[] bCompressedSize,
        long[] bChunkOffset, int[] bChunkLength)
    {
        int bLength = ChunkIndex.BHeaderSize + (index.Files.Count * ChunkIndex.BFileRecordSize) + (index.Chunks.Count * ChunkIndex.BChunkRecordSize);
        var trailer = new byte[sectionA.Length + bLength];
        Buffer.BlockCopy(sectionA, 0, trailer, 0, sectionA.Length);

        int pos = sectionA.Length;
        trailer[pos] = 0x56; // "VPKB"
        trailer[pos + 1] = 0x50;
        trailer[pos + 2] = 0x4B;
        trailer[pos + 3] = 0x42;
        LittleEndian.WriteUInt16(trailer, pos + 4, ChunkIndex.FormatVersion);
        LittleEndian.WriteUInt16(trailer, pos + 6, 0);
        LittleEndian.WriteUInt32(trailer, pos + 8, (uint) index.Files.Count);
        LittleEndian.WriteUInt32(trailer, pos + 12, (uint) index.Chunks.Count);
        pos += ChunkIndex.BHeaderSize;

        for (int i = 0; i < index.Files.Count; i++) {
            trailer[pos] = bMethod[i];
            LittleEndian.WriteUInt64(trailer, pos + 8, (ulong) bDataOffset[i]);
            LittleEndian.WriteUInt64(trailer, pos + 16, (ulong) bCompressedSize[i]);
            pos += ChunkIndex.BFileRecordSize;
        }

        for (int c = 0; c < index.Chunks.Count; c++) {
            LittleEndian.WriteUInt64(trailer, pos, (ulong) bChunkOffset[c]);
            LittleEndian.WriteUInt32(trailer, pos + 8, (uint) bChunkLength[c]);
            pos += ChunkIndex.BChunkRecordSize;
        }

        return trailer;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // helpers
    // ---------------------------------------------------------------------------------------------------------------

    private static void ReadExactly(Stream stream, byte[] buffer, int count)
    {
        int read = 0;
        while (read < count) {
            int n = stream.Read(buffer, read, count - read);
            if (n <= 0) throw new EndOfStreamException("File is shorter than its index record.");
            read += n;
        }
    }

    private static void CopyExactly(Stream input, Stream output, long count)
    {
        var buffer = new byte[81920];
        long remaining = count;
        while (remaining > 0) {
            int n = input.Read(buffer, 0, (int) Math.Min(buffer.Length, remaining));
            if (n <= 0) throw new EndOfStreamException("File is shorter than its index record.");
            output.Write(buffer, 0, n);
            remaining -= n;
        }
    }

    private sealed class ZipWriter
    {
        private readonly byte[] _buffer;
        private int _pos;

        public ZipWriter(int size)
        {
            _buffer = new byte[size];
        }

        public void U16(ushort v)
        {
            LittleEndian.WriteUInt16(_buffer, _pos, v);
            _pos += 2;
        }

        public void U32(uint v)
        {
            LittleEndian.WriteUInt32(_buffer, _pos, v);
            _pos += 4;
        }

        public void U64(ulong v)
        {
            LittleEndian.WriteUInt64(_buffer, _pos, v);
            _pos += 8;
        }

        public void Bytes(byte[] b)
        {
            Buffer.BlockCopy(b, 0, _buffer, _pos, b.Length);
            _pos += b.Length;
        }

        public void WriteTo(Stream output)
        {
            if (_pos != _buffer.Length) throw new InvalidOperationException("Zip header size mismatch.");
            output.Write(_buffer, 0, _buffer.Length);
        }
    }
}
