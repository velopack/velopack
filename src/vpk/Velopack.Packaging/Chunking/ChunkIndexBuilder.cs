#nullable enable
using System.IO.Hashing;
using System.Security.Cryptography;
using System.Text;
using Velopack.Chunks;
using Velopack.Core;

namespace Velopack.Packaging.Chunking;

/// <summary>
/// Pass 1 of a chunk-aligned pack (delta-v2 spec §4.1, §10.2): enumerates the pack directory, assigns dispositions,
/// validates every path against the index rules plus the stricter pack-time rules, and reads every file exactly once to
/// compute its CDC boundaries, per-chunk SHA-256, per-file SHA-256 and CRC-32. Nothing is compressed here.
/// </summary>
public static class ChunkIndexBuilder
{
    public const string SqChunksFileName = ChunkIndex.LocalFileName;
    public const string UpdaterFileName = ChunkIndex.UpdaterPath;
    public const string StubSuffix = ChunkIndex.StubSuffix;

    /// <summary>Builds the content index of <paramref name="packDir"/>. Violations are reported as <see cref="UserInfoException"/>.</summary>
    public static PackContentIndex Build(string packDir, Action<int>? progress = null)
    {
        progress ??= _ => { };
        var root = new DirectoryInfo(packDir);
        if (!root.Exists) throw new ArgumentException($"Pack directory does not exist: {packDir}", nameof(packDir));

        var sources = new List<SourceFile>();
        Enumerate(root, root.FullName, sources);

        var paths = sources.Select(s => s.Path).ToList();
        var dispositions = AssignDispositions(paths);

        // ordinal UTF-8 byte order is the record order of the index
        var order = Enumerable.Range(0, sources.Count).ToArray();
        var utf8 = sources.Select(s => Encoding.UTF8.GetBytes(s.Path)).ToArray();
        Array.Sort(order, (x, y) => PackContentIndex.CompareBytes(utf8[x], utf8[y]));

        var results = new FileResult[sources.Count];
        int done = 0;
        Parallel.ForEach(
            Enumerable.Range(0, sources.Count),
            new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount },
            i => {
                results[i] = HashAndChunk(sources[i]);
                int n = Interlocked.Increment(ref done);
                progress((int) ((double) n / sources.Count * 100));
            });

        var files = new List<PackFileEntry>(sources.Count);
        var chunks = new List<PackChunkEntry>();
        foreach (int i in order) {
            var r = results[i];
            int first = r.Chunks.Count > 0 ? chunks.Count : 0;
            files.Add(new PackFileEntry(sources[i].Path, dispositions[i], r.Length, r.Sha256, r.Crc32, first, r.Chunks.Count));
            chunks.AddRange(r.Chunks);
        }

        if (files.Count > ChunkIndex.MaxFileCount) {
            throw new UserInfoException($"The package contains too many files for a chunk index ({files.Count}).");
        }

        if (chunks.Count > ChunkIndex.MaxChunkCount) {
            throw new UserInfoException($"The package contains too many chunks for a chunk index ({chunks.Count}).");
        }

        progress(100);
        return new PackContentIndex(files, chunks);
    }

    /// <summary>Writes the index's Section A to <c>packDir/sq.chunks</c>.</summary>
    public static void WriteSqChunks(PackContentIndex index, string packDir)
    {
        File.WriteAllBytes(Path.Combine(packDir, SqChunksFileName), index.SerializeSectionA());
    }

    /// <summary>
    /// Assigns a disposition to every pack path (relative, <c>/</c> separated) and enforces the path rules of the index
    /// (spec §2.5) plus the pack-time rejections of spec §10.2. Throws <see cref="UserInfoException"/> naming the path.
    /// </summary>
    public static PackFileDisposition[] AssignDispositions(IReadOnlyList<string> paths)
    {
        var result = new PackFileDisposition[paths.Count];
        var seenIgnoreCase = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < paths.Count; i++) {
            string path = paths[i];
            string? error = ChunkIndex.ValidatePath(path);
            if (error == null && Encoding.UTF8.GetByteCount(path) > ChunkIndex.MaxPathLength) error = "path too long";
            if (error != null) {
                throw new UserInfoException($"The file path '{path}' cannot be packaged: {error}.");
            }

            if (!seenIgnoreCase.Add(path)) {
                throw new UserInfoException(
                    $"The pack directory contains two files whose paths differ only by case ('{path}'); " +
                    $"they would collide on a case-insensitive file system.");
            }

            bool topLevel = path.IndexOf('/') < 0;
            if (path.EndsWith(UpdaterFileName, StringComparison.Ordinal)) {
                if (path != UpdaterFileName) {
                    throw new UserInfoException(
                        $"The file '{path}' cannot be packaged: only the top-level '{UpdaterFileName}' may end with '{UpdaterFileName}', " +
                        $"because the update binary resolves the updater by that suffix.");
                }

                result[i] = PackFileDisposition.Updater;
            } else if (path.EndsWith(StubSuffix, StringComparison.Ordinal)) {
                if (!topLevel) {
                    throw new UserInfoException(
                        $"The file '{path}' cannot be packaged: '*{StubSuffix}' files must be at the top level of the pack directory, " +
                        $"because the update binary installs them as root launchers.");
                }

                result[i] = PackFileDisposition.Stub;
            } else {
                result[i] = PackFileDisposition.AppFile;
            }
        }

        return result;
    }

    private sealed class SourceFile
    {
        public SourceFile(string path, FileInfo info)
        {
            Path = path;
            Info = info;
        }

        public string Path { get; }
        public FileInfo Info { get; }
    }

    private sealed class FileResult
    {
        public long Length;
        public byte[] Sha256 = Array.Empty<byte>();
        public uint Crc32;
        public List<PackChunkEntry> Chunks = new();
    }

    private static void Enumerate(DirectoryInfo dir, string rootPath, List<SourceFile> sources)
    {
        foreach (var entry in dir.EnumerateFileSystemInfos("*", SearchOption.TopDirectoryOnly)) {
            string relative = entry.FullName.Substring(rootPath.Length).TrimStart('\\', '/').Replace('\\', '/');
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0) {
                throw new UserInfoException(
                    $"The pack directory contains a symbolic link or junction ('{relative}'), which cannot be packaged for Windows. " +
                    $"Replace it with a real copy of its target.");
            }

            if (entry is DirectoryInfo subDir) {
                Enumerate(subDir, rootPath, sources);
            } else if (entry is FileInfo file) {
                if (relative == SqChunksFileName) continue; // the index never describes itself
                sources.Add(new SourceFile(relative, file));
            }
        }
    }

    private static FileResult HashAndChunk(SourceFile source)
    {
        var result = new FileResult();
        var fileSha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        var crc = new Crc32();
        long expected = source.Info.Length;

        using (var stream = new FileStream(source.Info.FullName, FileMode.Open, FileAccess.Read, FileShare.Read, 1, FileOptions.SequentialScan)) {
            result.Length = FastCdc.ChunkStream(
                stream,
                (buffer, offset, length) => {
                    var span = new ReadOnlySpan<byte>(buffer, offset, length);
                    fileSha.AppendData(span);
                    crc.Append(span);
                    result.Chunks.Add(new PackChunkEntry(length, SHA256.HashData(span)));
                });
        }

        if (result.Length != expected) {
            throw new IOException($"'{source.Path}' changed while it was being indexed (expected {expected} bytes, read {result.Length}).");
        }

        result.Sha256 = fileSha.GetHashAndReset();
        result.Crc32 = crc.GetCurrentHashAsUInt32();
        return result;
    }
}
