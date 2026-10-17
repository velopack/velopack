#nullable enable
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Velopack.Chunks;

namespace Velopack.Tests.Chunks;

/// <summary>
/// A synthetic chunked package: real per-chunk deflate data (§4.1) laid out like the zip writer would, with a real
/// A‖B trailer and a feed asset carrying the pointer. It is not a valid zip (no headers/central directory), which
/// is enough for everything that reads it by range.
/// </summary>
internal sealed class TestPackage
{
    public byte[] Bytes = Array.Empty<byte>();
    public byte[] Trailer = Array.Empty<byte>();
    public long TrailerOffset;
    public VelopackAsset Asset = new();
    public ChunkIndex Remote = null!;
    public SortedDictionary<string, byte[]> Files = new(StringComparer.Ordinal);
    public TestIndexBuilder Builder = new();

    public byte[] SectionA => Remote.CopySectionA();

    public string StagedId => ChunkIndex.StagedIdOf(Asset.ChunkIndexSHA256);

    /// <summary>All chunk hashes with their plaintext, in index order.</summary>
    public List<(Hash256 hash, byte[] plain)> Chunks()
    {
        var result = new List<(Hash256, byte[])>();
        for (int i = 0; i < Remote.FileCount; i++) {
            var f = Remote.GetFile(i);
            if (!f.HasChunks) continue;
            var content = Files[f.Path];
            int pos = 0;
            for (int c = 0; c < f.ChunkCount; c++) {
                var chunk = Remote.GetChunk(f.FirstChunk + c);
                var plain = new byte[chunk.Length];
                Buffer.BlockCopy(content, pos, plain, 0, chunk.Length);
                result.Add((chunk.Sha256, plain));
                pos += chunk.Length;
            }
        }
        return result;
    }

    /// <summary>Every remote chunk as its own range request (no coalescing), in offset order.</summary>
    public List<RangeRequest> PerChunkRequests()
    {
        var plan = ChunkPlan.Create(Remote, null, _ => false, _ => false);
        return plan.Needed.OrderBy(c => c.Offset).Select(c => new RangeRequest(c)).ToList();
    }

    /// <summary>
    /// Writes the installed view of this package under <paramref name="rootDir"/>: <c>current/&lt;files&gt;</c>,
    /// <c>current/sq.chunks</c>, the root <c>Update.exe</c> and root stubs.
    /// </summary>
    public void Install(string rootDir)
    {
        var current = Path.Combine(rootDir, "current");
        Directory.CreateDirectory(current);
        for (int i = 0; i < Remote.FileCount; i++) {
            var f = Remote.GetFile(i);
            string dest;
            switch (f.Disposition) {
            case ChunkFileDisposition.Updater:
                dest = Path.Combine(rootDir, "Update.exe");
                break;
            case ChunkFileDisposition.Stub:
                dest = Path.Combine(rootDir, ChunkIndex.GetStubInstalledName(f.Path));
                break;
            default:
                dest = Path.Combine(current, f.Path.Replace('/', Path.DirectorySeparatorChar));
                break;
            }
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.WriteAllBytes(dest, Files[f.Path]);
        }
        File.WriteAllBytes(Path.Combine(current, ChunkIndex.LocalFileName), SectionA);
    }
}

internal static class TestPackageBuilder
{
    public const int DefaultChunkSize = 64 * 1024;

    /// <summary>Compressible deterministic content (repeating pseudo-random words).</summary>
    public static byte[] Text(ulong seed, int length)
    {
        var rnd = TestIndexBuilder.DeterministicBytes(seed, length / 4 + 8);
        var sb = new StringBuilder(length + 16);
        int i = 0;
        while (sb.Length < length) {
            sb.Append("word").Append(rnd[i % rnd.Length] % 37).Append(' ');
            i++;
        }
        return Encoding.ASCII.GetBytes(sb.ToString(0, length));
    }

    /// <summary>Incompressible deterministic content.</summary>
    public static byte[] Random(ulong seed, int length) => TestIndexBuilder.DeterministicBytes(seed, length);

    public static TestPackage Build(IDictionary<string, byte[]> files, string packageId = "ChunkApp", string version = "1.1.0",
        int chunkSize = DefaultChunkSize)
    {
        var pkg = new TestPackage();
        foreach (var kv in files) pkg.Files[kv.Key] = kv.Value;
        var b = pkg.Builder;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);

        // something that stands in for the nuspec / content types entries before lib/app
        w.Write(Random(99, 300));

        foreach (var kv in pkg.Files) {
            var path = kv.Key;
            var content = kv.Value;
            var disposition = path == ChunkIndex.UpdaterPath ? ChunkFileDisposition.Updater
                : path.EndsWith(ChunkIndex.StubSuffix, StringComparison.Ordinal) ? ChunkFileDisposition.Stub
                : ChunkFileDisposition.AppFile;
            var entry = b.AddFile(path, content, disposition, chunkSize: chunkSize);
            if (content.Length == 0) {
                entry.Method = 0;
                entry.DataOffset = 0;
                entry.CompressedSize = 0;
                continue;
            }

            // local header stand-in: 30 bytes + name
            w.Write(new byte[30 + Encoding.UTF8.GetByteCount("lib/app/" + path)]);
            entry.DataOffset = ms.Position;

            var compressed = new List<byte[]>();
            int pos = 0;
            long sum = 0;
            foreach (var chunk in entry.Chunks) {
                var c = DeflateChunk(content, pos, chunk.Length);
                compressed.Add(c);
                sum += c.Length;
                pos += chunk.Length;
            }

            if (sum + 2 < content.Length) {
                entry.Method = ChunkIndex.MethodDeflate;
                for (int i = 0; i < entry.Chunks.Count; i++) {
                    entry.Chunks[i].Offset = ms.Position;
                    entry.Chunks[i].TransportLength = compressed[i].Length;
                    w.Write(compressed[i]);
                }
                w.Write(ChunkInflate.FinalBlock);
                entry.CompressedSize = sum + 2;
            } else {
                entry.Method = ChunkIndex.MethodStored;
                long plain = 0;
                foreach (var chunk in entry.Chunks) {
                    chunk.Offset = ms.Position + plain;
                    chunk.TransportLength = chunk.Length;
                    plain += chunk.Length;
                }
                w.Write(content);
                entry.CompressedSize = content.Length;
            }
        }

        // the trailer is the last entry; a little filler stands in for its local header and the central directory
        w.Write(new byte[30 + ChunkIndex.TrailerEntryName.Length]);
        pkg.Trailer = b.BuildTrailer();
        pkg.TrailerOffset = ms.Position;
        w.Write(pkg.Trailer);
        w.Write(Random(98, 120));
        pkg.Bytes = ms.ToArray();

        pkg.Remote = ChunkIndex.ParseTrailer(pkg.Trailer, pkg.Bytes.Length);
        pkg.Asset = new VelopackAsset {
            PackageId = packageId,
            Version = SemanticVersion.Parse(version),
            Type = VelopackAssetType.Full,
            FileName = $"{packageId}-{version}-full.nupkg",
            Size = pkg.Bytes.Length,
            SHA1 = Convert.ToHexString(SHA1.HashData(pkg.Bytes)),
            SHA256 = Convert.ToHexString(SHA256.HashData(pkg.Bytes)),
            ChunkIndexOffset = pkg.TrailerOffset,
            ChunkIndexLength = pkg.Trailer.Length,
            ChunkIndexSHA256 = Convert.ToHexString(SHA256.HashData(pkg.Trailer)),
        };
        return pkg;
    }

    /// <summary>One chunk compressed per §4.1: a fresh DeflateStream, one Write, a sync flush, the stream's own tail discarded.</summary>
    public static byte[] DeflateChunk(byte[] content, int offset, int count)
    {
        var ms = new MemoryStream();
        using (var ds = new DeflateStream(ms, CompressionLevel.Optimal, leaveOpen: true)) {
            ds.Write(content.AsSpan(offset, count));
            ds.Flush();
            var result = ms.ToArray();
            if (result.Length < 4 || result[^4] != 0 || result[^3] != 0 || result[^2] != 0xFF || result[^1] != 0xFF) {
                throw new InvalidOperationException("DeflateStream.Flush did not produce a sync flush block.");
            }
            return result;
        }
    }
}
