using System;
using System.Collections.Generic;
using System.IO;

namespace Velopack.Chunks
{
    /// <summary>A remote chunk that has to be fetched, with its transport range.</summary>
    internal sealed class NeededChunk
    {
        /// <summary>Index of the chunk record in the remote index.</summary>
        public int ChunkIndex { get; }
        /// <summary>Index of the file record owning the chunk in the remote index.</summary>
        public int FileIndex { get; }
        public Hash256 Hash { get; }
        /// <summary>Zip entry method of the owning file (0 stored, 8 deflate).</summary>
        public byte Method { get; }
        /// <summary>Byte offset of the chunk's data in the .nupkg.</summary>
        public long Offset { get; }
        /// <summary>Bytes to fetch (compressed size for deflate entries).</summary>
        public int Length { get; }
        /// <summary>Plaintext length of the chunk.</summary>
        public int PlainLength { get; }

        public long End => Offset + Length;

        public NeededChunk(int chunkIndex, int fileIndex, Hash256 hash, byte method, long offset, int length, int plainLength)
        {
            ChunkIndex = chunkIndex;
            FileIndex = fileIndex;
            Hash = hash;
            Method = method;
            Offset = offset;
            Length = length;
            PlainLength = plainLength;
        }
    }

    /// <summary>One ranged request: a contiguous byte range of the .nupkg carrying one or more needed chunks.</summary>
    internal sealed class RangeRequest
    {
        public long Offset { get; private set; }
        public long Length { get; private set; }
        public List<NeededChunk> Chunks { get; } = new List<NeededChunk>();

        public long End => Offset + Length;

        public RangeRequest(NeededChunk first)
        {
            Offset = first.Offset;
            Length = first.Length;
            Chunks.Add(first);
        }

        internal void Add(NeededChunk chunk)
        {
            Chunks.Add(chunk);
            Length = Math.Max(End, chunk.End) - Offset;
        }
    }

    /// <summary>The installed view the planner and assembler read local chunks from.</summary>
    internal sealed class LocalRoots
    {
        /// <summary><c>current/</c>: destination of <see cref="ChunkFileDisposition.AppFile"/> records.</summary>
        public string CurrentDir { get; }
        /// <summary>Destination of the <see cref="ChunkFileDisposition.Updater"/> record.</summary>
        public string UpdateExePath { get; }
        /// <summary>Directory holding the installed <see cref="ChunkFileDisposition.Stub"/> executables.</summary>
        public string RootDir { get; }

        public LocalRoots(string currentDir, string updateExePath, string rootDir)
        {
            CurrentDir = currentDir ?? throw new ArgumentNullException(nameof(currentDir));
            UpdateExePath = updateExePath ?? throw new ArgumentNullException(nameof(updateExePath));
            RootDir = rootDir ?? throw new ArgumentNullException(nameof(rootDir));
        }

        /// <summary>The installed path of a record, by disposition.</summary>
        public string Resolve(ChunkFileRecord file)
        {
            switch (file.Disposition) {
            case ChunkFileDisposition.Updater:
                return UpdateExePath;
            case ChunkFileDisposition.Stub:
                return Path.Combine(RootDir, ChunkIndex.GetStubInstalledName(file.Path));
            default:
                return Path.Combine(CurrentDir, file.Path.Replace('/', Path.DirectorySeparatorChar));
            }
        }
    }

    /// <summary>
    /// The download plan (§5.3) and range coalescing (§5.4): which remote chunks cannot be sourced from the installed
    /// view (declared by the local index and sanity-checked) or the staged blob, and how to fetch them.
    /// The plan is an estimate; assembly may still discover local mismatches and ask for more hashes.
    /// </summary>
    internal sealed class ChunkPlan
    {
        public const int Parallelism = 4;
        public const long MaxRequestLength = 8L * 1024 * 1024;
        public const int MaxGap = 32 * 1024;
        /// <summary>A local file this small is hashed during planning rather than trusted by length alone.</summary>
        public const int PlanningHashThreshold = 64 * 1024;
        /// <summary>The chunk path is abandoned when the needed transport bytes reach this fraction of the package.</summary>
        public const double TransportCeiling = 0.9;

        /// <summary>Chunks to fetch, in remote file order, deduplicated by hash.</summary>
        public List<NeededChunk> Needed { get; }
        /// <summary>Σ transport bytes of <see cref="Needed"/>.</summary>
        public long NeededCompressedBytes { get; }
        /// <summary>Σ plaintext bytes of <see cref="Needed"/>.</summary>
        public long NeededPlaintextBytes { get; }
        /// <summary>Σ plaintext bytes of every File record in the remote index (the size of the assembled tree).</summary>
        public long RemoteFileBytes { get; }
        /// <summary>Number of remote files that can be copied whole from a usable local file.</summary>
        public int WholeFileCopies { get; }

        private ChunkPlan(List<NeededChunk> needed, long neededCompressed, long neededPlain, long remoteFileBytes, int wholeFileCopies)
        {
            Needed = needed;
            NeededCompressedBytes = neededCompressed;
            NeededPlaintextBytes = neededPlain;
            RemoteFileBytes = remoteFileBytes;
            WholeFileCopies = wholeFileCopies;
        }

        /// <summary>True when fetching the plan would cost at least 90 % of the package: take the classic path instead.</summary>
        public bool ExceedsTransportCeiling(long assetSize)
        {
            // integer form of NeededCompressedBytes >= 0.9 * assetSize
            return NeededCompressedBytes * 10 >= assetSize * 9;
        }

        /// <summary>Plans against the installed view on disk: local files are checked by length (and SHA-256 when small).</summary>
        public static ChunkPlan Create(ChunkIndex remote, ChunkIndex? local, LocalRoots roots, ChunkBlob? blob)
        {
            if (roots == null) throw new ArgumentNullException(nameof(roots));
            return Create(
                remote,
                local,
                i => IsLocalFileUsable(local!, i, roots),
                h => blob != null && blob.Contains(h));
        }

        /// <summary>
        /// Plans with injected local-usability and blob membership predicates. <paramref name="isLocalFileUsable"/> is
        /// asked once per File record of <paramref name="local"/> (never called when <paramref name="local"/> is null).
        /// </summary>
        public static ChunkPlan Create(ChunkIndex remote, ChunkIndex? local, Func<int, bool> isLocalFileUsable, Func<Hash256, bool> blobContains)
        {
            if (remote == null) throw new ArgumentNullException(nameof(remote));
            if (isLocalFileUsable == null) throw new ArgumentNullException(nameof(isLocalFileUsable));
            if (blobContains == null) throw new ArgumentNullException(nameof(blobContains));
            if (!remote.HasTransportMap) throw new ArgumentException("The remote index must carry a transport map.", nameof(remote));

            var haveChunks = new HashSet<Hash256>();
            var haveFiles = new HashSet<Hash256>();
            if (local != null) {
                for (int i = 0; i < local.FileCount; i++) {
                    if (local.GetFileKind(i) != ChunkFileKind.File) continue;
                    if (!isLocalFileUsable(i)) continue;
                    var file = local.GetFile(i);
                    haveFiles.Add(file.Sha256);
                    for (int c = 0; c < file.ChunkCount; c++) {
                        haveChunks.Add(local.GetChunkHash(file.FirstChunk + c));
                    }
                }
            }

            var needed = new List<NeededChunk>();
            var neededHashes = new HashSet<Hash256>();
            long neededCompressed = 0, neededPlain = 0, remoteBytes = 0;
            int wholeCopies = 0;

            for (int i = 0; i < remote.FileCount; i++) {
                var file = remote.GetFile(i);
                if (file.Kind != ChunkFileKind.File || file.Length == 0) continue;
                remoteBytes += file.Length;
                if (haveFiles.Contains(file.Sha256)) {
                    wholeCopies++;
                    continue;
                }
                var transport = remote.GetTransportFile(i);
                for (int c = 0; c < file.ChunkCount; c++) {
                    int chunkIndex = file.FirstChunk + c;
                    var chunk = remote.GetChunk(chunkIndex);
                    if (haveChunks.Contains(chunk.Sha256) || neededHashes.Contains(chunk.Sha256) || blobContains(chunk.Sha256)) continue;
                    var range = remote.GetTransportChunk(chunkIndex);
                    var nc = new NeededChunk(chunkIndex, i, chunk.Sha256, transport.Method, range.Offset, range.Length, chunk.Length);
                    needed.Add(nc);
                    neededHashes.Add(chunk.Sha256);
                    neededCompressed += range.Length;
                    neededPlain += chunk.Length;
                }
            }

            return new ChunkPlan(needed, neededCompressed, neededPlain, remoteBytes, wholeCopies);
        }

        /// <summary>
        /// Resolves the transport ranges of specific chunk hashes (the <c>missing.bin</c> round, §7.1). Hashes the remote
        /// index does not contain are ignored; each hash yields one chunk.
        /// </summary>
        public static List<NeededChunk> ForHashes(ChunkIndex remote, IEnumerable<Hash256> hashes)
        {
            if (remote == null) throw new ArgumentNullException(nameof(remote));
            if (hashes == null) throw new ArgumentNullException(nameof(hashes));
            if (!remote.HasTransportMap) throw new ArgumentException("The remote index must carry a transport map.", nameof(remote));

            var wanted = new HashSet<Hash256>(hashes);
            var result = new List<NeededChunk>();
            if (wanted.Count == 0) return result;
            for (int i = 0; i < remote.FileCount && wanted.Count > 0; i++) {
                var file = remote.GetFile(i);
                if (!file.HasChunks) continue;
                var transport = remote.GetTransportFile(i);
                for (int c = 0; c < file.ChunkCount && wanted.Count > 0; c++) {
                    int chunkIndex = file.FirstChunk + c;
                    var chunk = remote.GetChunk(chunkIndex);
                    if (!wanted.Remove(chunk.Sha256)) continue;
                    var range = remote.GetTransportChunk(chunkIndex);
                    result.Add(new NeededChunk(chunkIndex, i, chunk.Sha256, transport.Method, range.Offset, range.Length, chunk.Length));
                }
            }
            return result;
        }

        /// <summary>
        /// A local File record is usable as a chunk/file source iff its installed file exists with exactly the recorded
        /// length and, for files of at most 64 KiB, its SHA-256 matches the record.
        /// </summary>
        public static bool IsLocalFileUsable(ChunkIndex local, int fileIndex, LocalRoots roots)
        {
            if (local == null) throw new ArgumentNullException(nameof(local));
            if (roots == null) throw new ArgumentNullException(nameof(roots));
            try {
                var file = local.GetFile(fileIndex);
                if (file.Kind != ChunkFileKind.File) return false;
                var path = roots.Resolve(file);
                var info = new FileInfo(path);
                if (!info.Exists || info.Length != file.Length) return false;
                if (file.Length <= PlanningHashThreshold && ChunkInflate.Sha256File(path) != file.Sha256) return false;
                return true;
            } catch (Exception) {
                return false;
            }
        }

        /// <summary>
        /// Sorts the chunks by offset and merges a chunk into the current request when the gap to the previous range
        /// is at most <see cref="MaxGap"/> and the merged request stays within <see cref="MaxRequestLength"/>. A request
        /// always carries at least one chunk, so a lone chunk larger than the cap is scheduled on its own.
        /// </summary>
        public static List<RangeRequest> Coalesce(IEnumerable<NeededChunk> needed)
        {
            if (needed == null) throw new ArgumentNullException(nameof(needed));
            var sorted = new List<NeededChunk>(needed);
            sorted.Sort((a, b) => a.Offset != b.Offset ? a.Offset.CompareTo(b.Offset) : a.ChunkIndex.CompareTo(b.ChunkIndex));

            var requests = new List<RangeRequest>();
            RangeRequest? current = null;
            foreach (var chunk in sorted) {
                bool merge = current != null && chunk.Offset - current.End <= MaxGap
                    && Math.Max(current.End, chunk.End) - current.Offset <= MaxRequestLength;
                if (merge) {
                    current!.Add(chunk);
                } else {
                    current = new RangeRequest(chunk);
                    requests.Add(current);
                }
            }
            return requests;
        }
    }
}
