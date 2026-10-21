using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace Velopack.Chunks
{
    /// <summary>Where a file described by a chunk index is installed.</summary>
    internal enum ChunkFileDisposition : byte
    {
        /// <summary><c>current/&lt;path&gt;</c></summary>
        AppFile = 0,
        /// <summary>The root <c>Update.exe</c> (the package's <c>Squirrel.exe</c>).</summary>
        Updater = 1,
        /// <summary>A root execution stub: <c>&lt;name minus _ExecutionStub&gt;.exe</c>.</summary>
        Stub = 2,
    }

    /// <summary>The kind of entry a chunk index file record describes.</summary>
    internal enum ChunkFileKind : byte
    {
        File = 0,
        Symlink = 1,
        EmptyDir = 2,
    }

    /// <summary>A decoded Section A file record.</summary>
    internal readonly struct ChunkFileRecord
    {
        public readonly string Path;
        public readonly ChunkFileDisposition Disposition;
        public readonly ChunkFileKind Kind;
        /// <summary>File: plaintext bytes. Symlink: UTF-8 length of the target. EmptyDir: 0.</summary>
        public readonly long Length;
        public readonly int FirstChunk;
        public readonly int ChunkCount;
        /// <summary>Symlink target (with <c>\</c> already replaced by <c>/</c>), null for other kinds.</summary>
        public readonly string? SymlinkTarget;
        public readonly ushort Mode;
        public readonly Hash256 Sha256;

        public ChunkFileRecord(string path, ChunkFileDisposition disposition, ChunkFileKind kind, long length, int firstChunk, int chunkCount,
            string? symlinkTarget, ushort mode, Hash256 sha256)
        {
            Path = path;
            Disposition = disposition;
            Kind = kind;
            Length = length;
            FirstChunk = firstChunk;
            ChunkCount = chunkCount;
            SymlinkTarget = symlinkTarget;
            Mode = mode;
            Sha256 = sha256;
        }

        /// <summary>True for a File record that owns at least one chunk (i.e. a non-empty regular file).</summary>
        public bool HasChunks => Kind == ChunkFileKind.File && ChunkCount > 0;
    }

    /// <summary>A decoded Section A chunk record.</summary>
    internal readonly struct ChunkRecord
    {
        public readonly int Length;
        public readonly Hash256 Sha256;

        public ChunkRecord(int length, Hash256 sha256)
        {
            Length = length;
            Sha256 = sha256;
        }
    }

    /// <summary>A decoded Section B file record: how the file's zip entry was written.</summary>
    internal readonly struct TransportFileRecord
    {
        /// <summary>0 = stored, 8 = deflate.</summary>
        public readonly byte Method;
        /// <summary>Offset of the entry's first data byte, relative to the start of the .nupkg.</summary>
        public readonly long DataOffset;
        /// <summary>The entry's compressed size as written in its zip headers.</summary>
        public readonly long CompressedSize;

        public TransportFileRecord(byte method, long dataOffset, long compressedSize)
        {
            Method = method;
            DataOffset = dataOffset;
            CompressedSize = compressedSize;
        }

        public bool IsDeflate => Method == ChunkIndex.MethodDeflate;
    }

    /// <summary>A decoded Section B chunk record: the byte range of the .nupkg to fetch for one chunk.</summary>
    internal readonly struct TransportChunkRecord
    {
        public readonly long Offset;
        public readonly int Length;

        public TransportChunkRecord(long offset, int length)
        {
            Offset = offset;
            Length = length;
        }
    }

    /// <summary>
    /// A parsed and validated delta-v2 chunk index: Section A (the content map, <c>lib/app/sq.chunks</c>) optionally
    /// followed by Section B (the transport map). Together A‖B form the <c>package/velopack/chunks.idx</c> trailer entry.
    /// The index is kept as the original packed bytes; records are decoded on access. Every structural rule and path
    /// safety rule is checked at parse time, so a <see cref="ChunkIndex"/> instance is always well-formed. Parse failures
    /// throw <see cref="InvalidDataException"/>; callers treat such an index as absent.
    /// </summary>
    internal sealed class ChunkIndex
    {
        public const string TrailerEntryName = "package/velopack/chunks.idx";
        public const string LocalFileName = "sq.chunks";
        public const string PackageLocalPath = "lib/app/sq.chunks";
        public const string UpdaterPath = "Squirrel.exe";
        public const string StubSuffix = "_ExecutionStub.exe";

        public const int FormatVersion = 1;
        public const byte HashAlgoSha256 = 1;
        public const byte CdcAlgoFastCdcGearV1 = 1;
        public const byte MethodStored = 0;
        public const byte MethodDeflate = 8;

        public const int AHeaderSize = 48;
        public const int FileRecordSize = 64;
        public const int ChunkRecordSize = 36;
        public const int BHeaderSize = 16;
        public const int BFileRecordSize = 24;
        public const int BChunkRecordSize = 12;

        public const int MaxFileCount = 1_000_000;
        public const int MaxChunkCount = 4_194_304;
        public const int MaxPathBlobLength = 64 * 1024 * 1024;
        public const int MaxSectionALength = 256 * 1024 * 1024;
        public const int MaxTrailerLength = 512 * 1024 * 1024;
        public const int MaxChunkLength = 16 * 1024 * 1024;
        public const int MaxPathLength = 1024;
        public const int MaxSegmentLength = 255;

        private static readonly byte[] MagicA = { 0x56, 0x50, 0x4B, 0x41 }; // "VPKA"
        private static readonly byte[] MagicB = { 0x56, 0x50, 0x4B, 0x42 }; // "VPKB"
        private static readonly UTF8Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static readonly string[] ReservedStems = {
            "CON", "PRN", "AUX", "NUL", "CONIN$", "CONOUT$",
            "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
            "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
        };

        private readonly byte[] _data;
        private readonly int _pathBlobLength;
        private readonly int _filesOffset;
        private readonly int _chunksOffset;
        private readonly int _bFilesOffset;
        private readonly int _bChunksOffset;

        private ChunkIndex(byte[] data, int fileCount, int chunkCount, int pathBlobLength, int sectionALength, bool hasTransportMap)
        {
            _data = data;
            FileCount = fileCount;
            ChunkCount = chunkCount;
            _pathBlobLength = pathBlobLength;
            SectionALength = sectionALength;
            HasTransportMap = hasTransportMap;
            _filesOffset = AHeaderSize + pathBlobLength;
            _chunksOffset = _filesOffset + (fileCount * FileRecordSize);
            _bFilesOffset = sectionALength + BHeaderSize;
            _bChunksOffset = _bFilesOffset + (fileCount * BFileRecordSize);
            CdcAlgo = data[7];
            CdcMin = LittleEndian.ReadUInt32(data, 12);
            CdcAvg = LittleEndian.ReadUInt32(data, 16);
            CdcMax = LittleEndian.ReadUInt32(data, 20);
        }

        public int FileCount { get; }
        public int ChunkCount { get; }
        /// <summary>Length of Section A in bytes (the <c>sq.chunks</c> content).</summary>
        public int SectionALength { get; }
        /// <summary>True when Section B is present (a trailer parsed with <see cref="ParseTrailer(byte[], long)"/>).</summary>
        public bool HasTransportMap { get; }
        /// <summary>Advisory CDC parameters recorded by the writer.</summary>
        public byte CdcAlgo { get; }
        public uint CdcMin { get; }
        public uint CdcAvg { get; }
        public uint CdcMax { get; }

        /// <summary>Total length of the bytes this index was parsed from (A, or A‖B for a trailer).</summary>
        public int TotalLength => _data.Length;

        /// <summary>Returns a copy of the Section A bytes (what is written to <c>current/sq.chunks</c>).</summary>
        public byte[] CopySectionA()
        {
            var a = new byte[SectionALength];
            Buffer.BlockCopy(_data, 0, a, 0, SectionALength);
            return a;
        }

        /// <summary>Writes the Section A bytes to a stream.</summary>
        public void WriteSectionA(Stream stream)
        {
            stream.Write(_data, 0, SectionALength);
        }

        /// <summary>
        /// The staged-tree identity derived from a trailer digest: the first 32 hex characters of the SHA-256, lower-case.
        /// </summary>
        public static string StagedIdOf(string chunkIndexSha256)
        {
            if (chunkIndexSha256 == null) throw new ArgumentNullException(nameof(chunkIndexSha256));
            if (!Hash256.TryParse(chunkIndexSha256, out _)) throw new ArgumentException("Not a SHA-256 hex string.", nameof(chunkIndexSha256));
            return chunkIndexSha256.Substring(0, 32).ToLowerInvariant();
        }

        /// <summary>
        /// The installed file name of a <see cref="ChunkFileDisposition.Stub"/> record (<c>_ExecutionStub.exe</c> replaced by <c>.exe</c>).
        /// </summary>
        public static string GetStubInstalledName(string stubPath)
        {
            if (!stubPath.EndsWith(StubSuffix, StringComparison.Ordinal)) throw new ArgumentException("Not a stub path.", nameof(stubPath));
            return stubPath.Substring(0, stubPath.Length - StubSuffix.Length) + ".exe";
        }

        /// <summary>
        /// Parses and validates a trailer (A‖B). <paramref name="assetSize"/> is the size of the .nupkg the ranges refer to;
        /// every transport range must fit inside it. Throws <see cref="InvalidDataException"/> when the index is rejected.
        /// </summary>
        public static ChunkIndex ParseTrailer(byte[] data, long assetSize)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return Parse(data, true, assetSize);
        }

        /// <summary>Parses and validates a trailer whose transport ranges are not bounded by a known asset size.</summary>
        public static ChunkIndex ParseTrailer(byte[] data) => ParseTrailer(data, long.MaxValue);

        /// <summary>Parses and validates a local content map (Section A only, the content of <c>current/sq.chunks</c>).</summary>
        public static ChunkIndex ParseLocal(byte[] data)
        {
            if (data == null) throw new ArgumentNullException(nameof(data));
            return Parse(data, false, long.MaxValue);
        }

        /// <summary>Reads and parses a local content map file; returns null when the file is missing or invalid.</summary>
        public static ChunkIndex? TryReadLocal(string sqChunksPath)
        {
            try {
                if (!File.Exists(sqChunksPath)) return null;
                var info = new FileInfo(sqChunksPath);
                if (info.Length < AHeaderSize || info.Length > MaxSectionALength) return null;
                return ParseLocal(File.ReadAllBytes(sqChunksPath));
            } catch (Exception) {
                return null;
            }
        }

        public ChunkFileRecord GetFile(int index)
        {
            CheckFileIndex(index);
            int o = _filesOffset + (index * FileRecordSize);
            var path = ReadBlobString(LittleEndian.ReadUInt32(_data, o), LittleEndian.ReadUInt16(_data, o + 4));
            var disposition = (ChunkFileDisposition) _data[o + 6];
            var kind = (ChunkFileKind) _data[o + 7];
            long length = (long) LittleEndian.ReadUInt64(_data, o + 8);
            int firstChunk = (int) LittleEndian.ReadUInt32(_data, o + 16);
            int chunkCount = (int) LittleEndian.ReadUInt32(_data, o + 20);
            string? target = null;
            if (kind == ChunkFileKind.Symlink) {
                target = ReadBlobString(LittleEndian.ReadUInt32(_data, o + 24), LittleEndian.ReadUInt16(_data, o + 28)).Replace('\\', '/');
            }
            ushort mode = LittleEndian.ReadUInt16(_data, o + 30);
            var sha = Hash256.FromBytes(_data, o + 32);
            return new ChunkFileRecord(path, disposition, kind, length, firstChunk, chunkCount, target, mode, sha);
        }

        public string GetPath(int index)
        {
            CheckFileIndex(index);
            int o = _filesOffset + (index * FileRecordSize);
            return ReadBlobString(LittleEndian.ReadUInt32(_data, o), LittleEndian.ReadUInt16(_data, o + 4));
        }

        public ChunkFileKind GetFileKind(int index)
        {
            CheckFileIndex(index);
            return (ChunkFileKind) _data[_filesOffset + (index * FileRecordSize) + 7];
        }

        public long GetFileLength(int index)
        {
            CheckFileIndex(index);
            return (long) LittleEndian.ReadUInt64(_data, _filesOffset + (index * FileRecordSize) + 8);
        }

        public Hash256 GetFileHash(int index)
        {
            CheckFileIndex(index);
            return Hash256.FromBytes(_data, _filesOffset + (index * FileRecordSize) + 32);
        }

        public ChunkRecord GetChunk(int index)
        {
            CheckChunkIndex(index);
            int o = _chunksOffset + (index * ChunkRecordSize);
            return new ChunkRecord((int) LittleEndian.ReadUInt32(_data, o), Hash256.FromBytes(_data, o + 4));
        }

        public int GetChunkLength(int index)
        {
            CheckChunkIndex(index);
            return (int) LittleEndian.ReadUInt32(_data, _chunksOffset + (index * ChunkRecordSize));
        }

        public Hash256 GetChunkHash(int index)
        {
            CheckChunkIndex(index);
            return Hash256.FromBytes(_data, _chunksOffset + (index * ChunkRecordSize) + 4);
        }

        public TransportFileRecord GetTransportFile(int index)
        {
            CheckTransportMap();
            CheckFileIndex(index);
            int o = _bFilesOffset + (index * BFileRecordSize);
            return new TransportFileRecord(_data[o], (long) LittleEndian.ReadUInt64(_data, o + 8), (long) LittleEndian.ReadUInt64(_data, o + 16));
        }

        public TransportChunkRecord GetTransportChunk(int index)
        {
            CheckTransportMap();
            CheckChunkIndex(index);
            int o = _bChunksOffset + (index * BChunkRecordSize);
            return new TransportChunkRecord((long) LittleEndian.ReadUInt64(_data, o), (int) LittleEndian.ReadUInt32(_data, o + 8));
        }

        /// <summary>
        /// Finds the record index of a path (exact, ordinal) by binary search over the sorted records, or -1.
        /// </summary>
        public int FindFile(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));
            var key = Encoding.UTF8.GetBytes(path);
            int lo = 0, hi = FileCount - 1;
            while (lo <= hi) {
                int mid = lo + ((hi - lo) / 2);
                int o = _filesOffset + (mid * FileRecordSize);
                int pathStart = AHeaderSize + (int) LittleEndian.ReadUInt32(_data, o);
                int c = CompareBytes(_data, pathStart, LittleEndian.ReadUInt16(_data, o + 4), key, 0, key.Length);
                if (c == 0) return mid;
                if (c < 0) lo = mid + 1;
                else hi = mid - 1;
            }
            return -1;
        }

        private string ReadBlobString(uint offset, int length)
        {
            // ranges were validated at parse time
            return Encoding.UTF8.GetString(_data, AHeaderSize + (int) offset, length);
        }

        private void CheckFileIndex(int index)
        {
            if (index < 0 || index >= FileCount) throw new ArgumentOutOfRangeException(nameof(index));
        }

        private void CheckChunkIndex(int index)
        {
            if (index < 0 || index >= ChunkCount) throw new ArgumentOutOfRangeException(nameof(index));
        }

        private void CheckTransportMap()
        {
            if (!HasTransportMap) throw new InvalidOperationException("This chunk index has no transport map (Section B).");
        }

        // ------------------------------------------------------------------------------------------------------------
        // Parsing and validation (§2.8 caps first, then records, then paths, then B). Nothing proportional to the
        // declared counts is allocated before the counts have been checked against the caps and the buffer length.
        // ------------------------------------------------------------------------------------------------------------

        private static InvalidDataException Invalid(string reason) => new InvalidDataException("Invalid chunk index: " + reason);

        private static ChunkIndex Parse(byte[] data, bool requireTransportMap, long assetSize)
        {
            if (data.Length < AHeaderSize) throw Invalid("too short");
            if (requireTransportMap && data.Length < AHeaderSize + BHeaderSize) throw Invalid("too short");
            if (data.Length > MaxTrailerLength) throw Invalid("larger than the 512 MiB cap");
            if (!HasMagic(data, 0, MagicA)) throw Invalid("bad Section A magic");
            if (LittleEndian.ReadUInt16(data, 4) != FormatVersion) throw Invalid("unsupported format version " + LittleEndian.ReadUInt16(data, 4));
            if (data[6] != HashAlgoSha256) throw Invalid("unsupported hash algorithm " + data[6]);
            // data[7] (cdc_algo), flags and the cdc parameters are advisory and never rejected.

            uint fileCount = LittleEndian.ReadUInt32(data, 24);
            uint chunkCount = LittleEndian.ReadUInt32(data, 28);
            uint pathBlobLength = LittleEndian.ReadUInt32(data, 32);
            if (fileCount > MaxFileCount) throw Invalid("file_count exceeds the cap");
            if (chunkCount > MaxChunkCount) throw Invalid("chunk_count exceeds the cap");
            if (pathBlobLength > MaxPathBlobLength) throw Invalid("path_blob_len exceeds the cap");

            long aLength = AHeaderSize + pathBlobLength + ((long) fileCount * FileRecordSize) + ((long) chunkCount * ChunkRecordSize);
            if (aLength > MaxSectionALength) throw Invalid("Section A exceeds the 256 MiB cap");
            long expectedLength = aLength;
            if (requireTransportMap) {
                expectedLength += BHeaderSize + ((long) fileCount * BFileRecordSize) + ((long) chunkCount * BChunkRecordSize);
            }
            if (expectedLength != data.Length) throw Invalid($"length mismatch: expected {expectedLength} bytes, got {data.Length}");

            if (requireTransportMap) {
                int b = (int) aLength;
                if (!HasMagic(data, b, MagicB)) throw Invalid("bad Section B magic");
                if (LittleEndian.ReadUInt16(data, b + 4) != FormatVersion) throw Invalid("unsupported Section B version");
                if (LittleEndian.ReadUInt32(data, b + 8) != fileCount) throw Invalid("Section B file_count does not match Section A");
                if (LittleEndian.ReadUInt32(data, b + 12) != chunkCount) throw Invalid("Section B chunk_count does not match Section A");
            }

            var index = new ChunkIndex(data, (int) fileCount, (int) chunkCount, (int) pathBlobLength, (int) aLength, requireTransportMap);
            index.ValidateChunks();
            index.ValidateFiles();
            if (requireTransportMap) index.ValidateTransportMap(assetSize);
            return index;
        }

        private static bool HasMagic(byte[] data, int offset, byte[] magic)
        {
            for (int i = 0; i < magic.Length; i++) {
                if (data[offset + i] != magic[i]) return false;
            }
            return true;
        }

        private void ValidateChunks()
        {
            for (int c = 0; c < ChunkCount; c++) {
                uint len = LittleEndian.ReadUInt32(_data, _chunksOffset + (c * ChunkRecordSize));
                if (len == 0 || len > MaxChunkLength) throw Invalid($"chunk {c} has length {len}");
            }
        }

        private void ValidateFiles()
        {
            var folded = new HashSet<string>(StringComparer.Ordinal);
            var exact = new HashSet<string>(StringComparer.Ordinal);
            int prevOff = -1, prevLen = 0;
            int nextChunk = 0;
            bool seenUpdater = false;

            for (int i = 0; i < FileCount; i++) {
                int o = _filesOffset + (i * FileRecordSize);
                uint pathOff = LittleEndian.ReadUInt32(_data, o);
                int pathLen = LittleEndian.ReadUInt16(_data, o + 4);
                byte disposition = _data[o + 6];
                byte kind = _data[o + 7];
                ulong length = LittleEndian.ReadUInt64(_data, o + 8);
                uint firstChunk = LittleEndian.ReadUInt32(_data, o + 16);
                uint chunkCount = LittleEndian.ReadUInt32(_data, o + 20);
                uint targetOff = LittleEndian.ReadUInt32(_data, o + 24);
                int targetLen = LittleEndian.ReadUInt16(_data, o + 28);

                if (pathLen < 1 || pathLen > MaxPathLength) throw Invalid($"record {i}: path_len {pathLen}");
                if (pathOff > (uint) _pathBlobLength || pathLen > _pathBlobLength - pathOff) throw Invalid($"record {i}: path outside the path blob");
                int pathStart = AHeaderSize + (int) pathOff;
                if (prevOff >= 0 && CompareBytes(_data, prevOff, prevLen, _data, pathStart, pathLen) >= 0) {
                    throw Invalid($"record {i}: paths are not strictly ascending");
                }
                prevOff = pathStart;
                prevLen = pathLen;

                string path = DecodeUtf8(_data, pathStart, pathLen, $"record {i}: path");
                string? error = ValidatePath(path);
                if (error != null) throw Invalid($"record {i} '{path}': {error}");
                if (!folded.Add(AsciiFold(path))) throw Invalid($"record {i} '{path}': duplicate path (ASCII case-insensitive)");
                exact.Add(path);

                if (disposition > (byte) ChunkFileDisposition.Stub) throw Invalid($"record {i} '{path}': disposition {disposition}");
                if (kind > (byte) ChunkFileKind.EmptyDir) throw Invalid($"record {i} '{path}': kind {kind}");

                // rule 8: dispositions and the suffixes old extractors key on
                bool endsWithUpdater = path.EndsWith(UpdaterPath, StringComparison.Ordinal);
                bool endsWithStub = path.EndsWith(StubSuffix, StringComparison.Ordinal);
                switch ((ChunkFileDisposition) disposition) {
                case ChunkFileDisposition.Updater:
                    if (seenUpdater) throw Invalid($"record {i} '{path}': more than one updater record");
                    if (kind != (byte) ChunkFileKind.File || path != UpdaterPath) {
                        throw Invalid($"record {i} '{path}': updater must be the file '{UpdaterPath}'");
                    }
                    seenUpdater = true;
                    break;
                case ChunkFileDisposition.Stub:
                    if (kind != (byte) ChunkFileKind.File || path.IndexOf('/') >= 0 || !endsWithStub || path.Length == StubSuffix.Length) {
                        throw Invalid($"record {i} '{path}': a stub must be a top-level file named *{StubSuffix}");
                    }
                    if (endsWithUpdater) throw Invalid($"record {i} '{path}': path ends with '{UpdaterPath}'");
                    break;
                default:
                    if (endsWithUpdater) throw Invalid($"record {i} '{path}': path ends with '{UpdaterPath}'");
                    if (endsWithStub) throw Invalid($"record {i} '{path}': path ends with '{StubSuffix}'");
                    break;
                }

                switch ((ChunkFileKind) kind) {
                case ChunkFileKind.File:
                    if (targetLen != 0) throw Invalid($"record {i} '{path}': target_len on a file record");
                    if (chunkCount == 0) {
                        if (length != 0) throw Invalid($"record {i} '{path}': non-empty file without chunks");
                        if (firstChunk != 0) throw Invalid($"record {i} '{path}': first_chunk must be 0 without chunks");
                    } else {
                        if (firstChunk != (uint) nextChunk) throw Invalid($"record {i} '{path}': chunks are not stored in file order");
                        if (chunkCount > (uint) (ChunkCount - nextChunk)) throw Invalid($"record {i} '{path}': chunk range outside the chunk table");
                        ulong sum = 0;
                        for (int c = 0; c < (int) chunkCount; c++) {
                            sum += LittleEndian.ReadUInt32(_data, _chunksOffset + ((nextChunk + c) * ChunkRecordSize));
                        }
                        if (sum != length) throw Invalid($"record {i} '{path}': length {length} != sum of chunk lengths {sum}");
                        nextChunk += (int) chunkCount;
                    }
                    break;
                case ChunkFileKind.Symlink:
                    if (chunkCount != 0 || firstChunk != 0) throw Invalid($"record {i} '{path}': symlink with chunks");
                    if (targetLen < 1 || targetLen > MaxPathLength) throw Invalid($"record {i} '{path}': target_len {targetLen}");
                    if (targetOff > (uint) _pathBlobLength || targetLen > _pathBlobLength - targetOff) {
                        throw Invalid($"record {i} '{path}': target outside the path blob");
                    }
                    if (length != (ulong) targetLen) throw Invalid($"record {i} '{path}': symlink length != target_len");
                    string target = DecodeUtf8(_data, AHeaderSize + (int) targetOff, targetLen, $"record {i} '{path}': target");
                    error = ValidateSymlinkTarget(path, target);
                    if (error != null) throw Invalid($"record {i} '{path}' -> '{target}': {error}");
                    break;
                case ChunkFileKind.EmptyDir:
                    if (chunkCount != 0 || firstChunk != 0) throw Invalid($"record {i} '{path}': empty directory with chunks");
                    if (length != 0 || targetLen != 0) throw Invalid($"record {i} '{path}': empty directory with length or target");
                    break;
                }
            }

            if (nextChunk != ChunkCount) throw Invalid($"{ChunkCount - nextChunk} chunk records are not owned by any file");

            // no record may be a directory prefix of another (checked on the exact paths); this also rejects anything
            // under an EmptyDir record, in O(total path length) like the Rust reader
            foreach (var path in exact) {
                for (int slash = path.IndexOf('/'); slash > 0; slash = path.IndexOf('/', slash + 1)) {
                    if (exact.Contains(path.Substring(0, slash))) throw Invalid($"'{path.Substring(0, slash)}' is both a file and a directory");
                }
            }
        }

        private void ValidateTransportMap(long assetSize)
        {
            if (assetSize < 0) throw new ArgumentOutOfRangeException(nameof(assetSize));
            for (int c = 0; c < ChunkCount; c++) {
                int o = _bChunksOffset + (c * BChunkRecordSize);
                ulong offset = LittleEndian.ReadUInt64(_data, o);
                uint length = LittleEndian.ReadUInt32(_data, o + 8);
                if (length == 0) throw Invalid($"transport chunk {c} has zero length");
                if (offset > (ulong) assetSize || length > (ulong) assetSize - offset) throw Invalid($"transport chunk {c} is outside the package");
            }

            int nextChunk = 0;
            for (int i = 0; i < FileCount; i++) {
                int o = _bFilesOffset + (i * BFileRecordSize);
                byte method = _data[o];
                ulong dataOffset = LittleEndian.ReadUInt64(_data, o + 8);
                ulong compressedSize = LittleEndian.ReadUInt64(_data, o + 16);
                int a = _filesOffset + (i * FileRecordSize);
                byte kind = _data[a + 7];
                ulong length = LittleEndian.ReadUInt64(_data, a + 8);
                int chunkCount = (int) LittleEndian.ReadUInt32(_data, a + 20);

                if (kind != (byte) ChunkFileKind.File || length == 0) {
                    if (method != 0 || dataOffset != 0 || compressedSize != 0) throw Invalid($"transport record {i}: must be all zero");
                    continue;
                }

                if (method != MethodStored && method != MethodDeflate) throw Invalid($"transport record {i}: method {method}");
                if (dataOffset > (ulong) assetSize || compressedSize > (ulong) assetSize - dataOffset) {
                    throw Invalid($"transport record {i}: entry data outside the package");
                }

                if (method == MethodStored) {
                    if (compressedSize != length) throw Invalid($"transport record {i}: stored entry size != file length");
                    ulong plainOffset = 0;
                    for (int c = 0; c < chunkCount; c++) {
                        int b = _bChunksOffset + ((nextChunk + c) * BChunkRecordSize);
                        uint chunkLength = LittleEndian.ReadUInt32(_data, _chunksOffset + ((nextChunk + c) * ChunkRecordSize));
                        if (LittleEndian.ReadUInt64(_data, b) != dataOffset + plainOffset || LittleEndian.ReadUInt32(_data, b + 8) != chunkLength) {
                            throw Invalid($"transport chunk {nextChunk + c}: stored range does not map 1:1 onto the plaintext");
                        }
                        plainOffset += chunkLength;
                    }
                } else {
                    // must at least hold the 03 00 terminator; "smaller than length" is a writer rule (store decision), not a reader rule
                    if (compressedSize < 2) throw Invalid($"transport record {i}: deflate entry size {compressedSize} is too small");
                    ulong expected = dataOffset;
                    for (int c = 0; c < chunkCount; c++) {
                        int b = _bChunksOffset + ((nextChunk + c) * BChunkRecordSize);
                        if (LittleEndian.ReadUInt64(_data, b) != expected) {
                            throw Invalid($"transport chunk {nextChunk + c}: deflate ranges are not contiguous");
                        }
                        expected += LittleEndian.ReadUInt32(_data, b + 8);
                    }
                    if (expected != dataOffset + compressedSize - 2) throw Invalid($"transport record {i}: chunk ranges do not cover the entry data");
                }
                nextChunk += chunkCount;
            }
        }

        private static string DecodeUtf8(byte[] data, int offset, int length, string what)
        {
            for (int i = 0; i < length; i++) {
                byte b = data[offset + i];
                if (b < 0x20 || b == 0x7F) throw Invalid($"{what} contains a control character");
            }
            try {
                return StrictUtf8.GetString(data, offset, length);
            } catch (DecoderFallbackException) {
                throw Invalid($"{what} is not valid UTF-8");
            }
        }

        /// <summary>
        /// Checks a record path against the §2.5 rules 1–5 and 7 (the structural, per-path rules). Returns null when the
        /// path is acceptable, otherwise a short description of the violation. Rule 1 (UTF-8, control characters, length)
        /// is checked on the raw bytes by the parser; this method re-checks what it can on the decoded string.
        /// </summary>
        public static string? ValidatePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "empty path";
            if (path.Length > MaxPathLength) return "path too long";
            if (AsciiFold(path) == LocalFileName) return $"'{LocalFileName}' may not be a record";
            if (path[0] == '/' || path[path.Length - 1] == '/') return "leading or trailing '/'";
            int start = 0;
            while (start <= path.Length) {
                int end = path.IndexOf('/', start);
                if (end < 0) end = path.Length;
                string? err = ValidateSegment(path, start, end - start, false);
                if (err != null) return err;
                start = end + 1;
            }
            return null;
        }

        private static string? ValidateSegment(string path, int start, int length, bool allowDots)
        {
            if (length == 0) return "empty path segment";
            if (length == 1 && path[start] == '.') return allowDots ? null : "'.' segment";
            if (length == 2 && path[start] == '.' && path[start + 1] == '.') return allowDots ? null : "'..' segment";
            for (int i = start; i < start + length; i++) {
                char c = path[i];
                if (c < 0x20 || c == 0x7F) return "control character";
                switch (c) {
                case '\\':
                case ':':
                case '*':
                case '?':
                case '"':
                case '<':
                case '>':
                case '|':
                    return $"illegal character '{c}'";
                }
            }
            char last = path[start + length - 1];
            if (last == '.' || last == ' ') return "segment ends with '.' or space";
            // counted in UTF-16 code units, the unit NTFS/Win32 limit a name to (Rust: encode_utf16().count())
            if (length > MaxSegmentLength) return "segment longer than 255 UTF-16 code units";
            if (!allowDots) {
                int dot = path.IndexOf('.', start, length);
                int stemLength = (dot < 0 ? start + length : dot) - start;
                foreach (var reserved in ReservedStems) {
                    if (reserved.Length != stemLength) continue;
                    if (string.Compare(path, start, reserved, 0, stemLength, StringComparison.OrdinalIgnoreCase) == 0) {
                        return $"reserved name '{reserved}'";
                    }
                }
            }
            return null;
        }

        /// <summary>
        /// Checks a symlink target (§2.5 rule 9): rules 1 and 3 on each segment, <c>.</c>/<c>..</c> allowed, relative only,
        /// and lexically joining it to the link's parent directory must not escape the tree root. Returns null when OK.
        /// </summary>
        public static string? ValidateSymlinkTarget(string linkPath, string target)
        {
            if (string.IsNullOrEmpty(target)) return "empty symlink target";
            target = target.Replace('\\', '/');
            if (target[0] == '/') return "absolute symlink target";
            if (target.Length >= 2 && target[1] == ':') return "absolute symlink target";

            // depth of the link's parent directory inside the tree
            int depth = 0;
            for (int i = 0; i < linkPath.Length; i++) {
                if (linkPath[i] == '/') depth++;
            }

            int start = 0;
            while (start < target.Length) {
                int end = target.IndexOf('/', start);
                if (end < 0) end = target.Length;
                int length = end - start;
                if (length == 0) {
                    if (end == target.Length) break; // trailing '/' = directory link
                    return "empty symlink target segment";
                }
                string? err = ValidateSegment(target, start, length, true);
                if (err != null) return err;
                if (length == 2 && target[start] == '.' && target[start + 1] == '.') {
                    if (--depth < 0) return "symlink target escapes the tree root";
                } else if (!(length == 1 && target[start] == '.')) {
                    depth++;
                }
                start = end + 1;
            }
            return null;
        }

        /// <summary>Maps ASCII <c>A..Z</c> to <c>a..z</c>; every other character is unchanged (the index uniqueness fold).</summary>
        public static string AsciiFold(string s)
        {
            char[]? chars = null;
            for (int i = 0; i < s.Length; i++) {
                char c = s[i];
                if (c >= 'A' && c <= 'Z') {
                    chars ??= s.ToCharArray();
                    chars[i] = (char) (c + 32);
                }
            }
            return chars == null ? s : new string(chars);
        }

        private static int CompareBytes(byte[] a, int aOff, int aLen, byte[] b, int bOff, int bLen)
        {
            int n = Math.Min(aLen, bLen);
            for (int i = 0; i < n; i++) {
                int d = a[aOff + i] - b[bOff + i];
                if (d != 0) return d;
            }
            return aLen - bLen;
        }
    }
}
