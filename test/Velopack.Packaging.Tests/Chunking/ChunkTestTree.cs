using System.Security.Cryptography;
using System.Text;
using Velopack.Chunks;
using Velopack.NuGet;
using Velopack.Packaging.Chunking;

namespace Velopack.Packaging.Tests.Chunking;

/// <summary>
/// Builds deterministic synthetic pack directories and staging directories (the layout <c>PackageBuilder.CreateReleasePackage</c>
/// produces) so the chunk index builder and the chunk-aligned zip writer can be exercised without a real app.
/// </summary>
internal static class ChunkTestTree
{
    private static readonly string[] Words = {
        "velopack", "update", "chunk", "deflate", "index", "release", "package", "install", "window", "stream",
        "buffer", "offset", "length", "digest", "verify", "assemble", "staged", "trailer", "record", "entry",
    };

    /// <summary>
    /// The SplitMix64 stream of <paramref name="seed"/> as little-endian u64s, truncated to <paramref name="size"/> (the
    /// spec's data generator).
    /// </summary>
    public static byte[] RandomBytes(ulong seed, int size)
    {
        var data = new byte[size];
        ulong state = seed;
        int pos = 0;
        while (pos < size) {
            ulong v = GearTable.SplitMix64Next(ref state);
            for (int i = 0; i < 8 && pos < size; i++) {
                data[pos++] = (byte) (v >> (8 * i));
            }
        }

        return data;
    }

    /// <summary>Deterministic text made of random words: compresses well but still has content-defined boundaries.</summary>
    public static byte[] CompressibleBytes(ulong seed, int size)
    {
        var sb = new StringBuilder(size + 16);
        ulong state = seed;
        while (sb.Length < size) {
            ulong v = GearTable.SplitMix64Next(ref state);
            sb.Append(Words[(int) (v % (ulong) Words.Length)]);
            sb.Append((v & 0xF00) == 0 ? '\n' : ' ');
        }

        return Encoding.ASCII.GetBytes(sb.ToString(0, size));
    }

    public static byte[] Sha256(byte[] data) => SHA256.HashData(data);

    public static string Hex(byte[] data) => Convert.ToHexString(data);

    public static string Nuspec(string id, string version, string channel = "win", string mainExe = "MyApp.exe")
    {
        return $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
            <metadata>
            <id>{id}</id>
            <title>{id}</title>
            <description>{id}</description>
            <authors>{id}</authors>
            <version>{version}</version>
            <channel>{channel}</channel>
            <mainExe>{mainExe}</mainExe>
            <os>win</os>
            <rid>win-x64</rid>
            <machineArchitecture>x64</machineArchitecture>
            </metadata>
            </package>
            """.Replace("\r\n", "\n");
    }

    public static void WriteFile(string dir, string relativePath, byte[] content)
    {
        var path = Path.Combine(dir, relativePath.Replace('/', Path.DirectorySeparatorChar));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    /// <summary>
    /// A typical Windows pack directory: a main exe, an updater placeholder, a stub, sq.version, nested files, an empty file.
    /// Returns the nuspec text written to sq.version.
    /// </summary>
    public static string CreateDefaultPackDir(string packDir, string id = "MyApp", string version = "1.0.0", ulong seed = 1)
    {
        var nuspec = Nuspec(id, version);
        WriteFile(packDir, "MyApp.exe", RandomBytes(seed, 300_000));
        WriteFile(packDir, "MyApp.dll", CompressibleBytes(seed + 1, 700_000));
        WriteFile(packDir, "Squirrel.exe", RandomBytes(seed + 2, 150_000));
        WriteFile(packDir, "MyApp_ExecutionStub.exe", CompressibleBytes(seed + 3, 40_000));
        WriteFile(packDir, "sq.version", Encoding.UTF8.GetBytes(nuspec));
        WriteFile(packDir, "data/words.txt", CompressibleBytes(seed + 4, 90_000));
        WriteFile(packDir, "data/nested/deep.bin", RandomBytes(seed + 5, 20_000));
        WriteFile(packDir, "data/tiny.txt", Encoding.ASCII.GetBytes("hello"));
        WriteFile(packDir, "empty.txt", Array.Empty<byte>());
        return nuspec;
    }

    /// <summary>
    /// Mirrors PackageBuilder.CreateReleasePackage: the nuspec at the root, the pack directory under lib/app,
    /// [Content_Types].xml (with the idx extension) and _rels/.rels.
    /// </summary>
    public static string CreateStagingDir(string stagingDir, string packDir, string nuspec, string id,
        Dictionary<string, byte[]> extraRootFiles = null)
    {
        Directory.CreateDirectory(stagingDir);
        var nuspecPath = Path.Combine(stagingDir, id + ".nuspec");
        File.WriteAllText(nuspecPath, nuspec);
        var appDir = Path.Combine(stagingDir, "lib", "app");
        CopyDirectory(packDir, appDir);
        if (extraRootFiles != null) {
            foreach (var kvp in extraRootFiles) WriteFile(stagingDir, kvp.Key, kvp.Value);
        }

        AddContentTypesAndRel(nuspecPath, ["idx"]);
        return nuspecPath;
    }

    public static void AddContentTypesAndRel(string nuspecPath, IEnumerable<string> extraExtensions)
    {
        var rootDirectory = Path.GetDirectoryName(nuspecPath)!;
        var extensions = Directory.EnumerateFiles(rootDirectory, "*", SearchOption.AllDirectories)
            .Select(p => Path.GetExtension(p).TrimStart('.').ToLower())
            .Concat(extraExtensions)
            .Distinct()
            .Where(ext => !String.IsNullOrWhiteSpace(ext))
            .Select(ext => $"""  <Default Extension="{ext}" ContentType="application/octet" />""")
            .ToArray();

        var contentType = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Types xmlns="http://schemas.openxmlformats.org/package/2006/content-types">
              <Default Extension="rels" ContentType="application/vnd.openxmlformats-package.relationships+xml" />
            {String.Join("\n", extensions)}
            </Types>
            """.Replace("\r\n", "\n");
        File.WriteAllText(Path.Combine(rootDirectory, NugetUtil.ContentTypeFileName), contentType);

        var relsDir = Path.Combine(rootDirectory, "_rels");
        Directory.CreateDirectory(relsDir);
        var rels = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <Relationships xmlns="http://schemas.openxmlformats.org/package/2006/relationships">
              <Relationship Type="http://schemas.microsoft.com/packaging/2010/07/manifest" Target="/{Path.GetFileName(nuspecPath)}" Id="R1" />
            </Relationships>
            """.Replace("\r\n", "\n");
        File.WriteAllText(Path.Combine(relsDir, ".rels"), rels);
    }

    public static void CopyDirectory(string source, string target)
    {
        Directory.CreateDirectory(target);
        foreach (var dir in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories)) {
            Directory.CreateDirectory(Path.Combine(target, Path.GetRelativePath(source, dir)));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories)) {
            File.Copy(file, Path.Combine(target, Path.GetRelativePath(source, file)), true);
        }
    }

    /// <summary>The result of a synthetic chunk-aligned pack.</summary>
    public sealed class Packed
    {
        public string PackDir;
        public string StagingDir;
        public string NupkgPath;
        public string TrailerPath;
        public PackContentIndex Index;
        public ChunkIndexPointer Pointer;
        public byte[] Trailer => File.ReadAllBytes(TrailerPath);
        public ChunkIndex ParsedTrailer => ChunkIndex.ParseTrailer(Trailer, new FileInfo(NupkgPath).Length);
    }

    /// <summary>Indexes <paramref name="packDir"/>, writes sq.chunks, stages it and writes the package.</summary>
    public static Packed Pack(string workDir, string packDir, string nuspec, string id, Dictionary<string, byte[]> extraRootFiles = null)
    {
        var index = ChunkIndexBuilder.Build(packDir);
        ChunkIndexBuilder.WriteSqChunks(index, packDir);
        var stagingDir = Path.Combine(workDir, "staging-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        CreateStagingDir(stagingDir, packDir, nuspec, id, extraRootFiles);
        var nupkgPath = Path.Combine(workDir, $"{id}-{Guid.NewGuid():N}-full.nupkg");
        var trailerPath = nupkgPath + ".trailer";
        var pointer = ChunkedZipWriter.Write(stagingDir, index, nupkgPath, trailerPath);
        return new Packed {
            PackDir = packDir,
            StagingDir = stagingDir,
            NupkgPath = nupkgPath,
            TrailerPath = trailerPath,
            Index = index,
            Pointer = pointer,
        };
    }

    /// <summary>
    /// Like <see cref="Pack"/>, but the pack directory already is <c>stagingDir/lib/app</c> (no copy), for trees with
    /// very many files.
    /// </summary>
    public static Packed PackInPlace(string workDir, string stagingDir, string nuspec, string id)
    {
        var packDir = Path.Combine(stagingDir, "lib", "app");
        var index = ChunkIndexBuilder.Build(packDir);
        ChunkIndexBuilder.WriteSqChunks(index, packDir);
        var nuspecPath = Path.Combine(stagingDir, id + ".nuspec");
        File.WriteAllText(nuspecPath, nuspec);
        AddContentTypesAndRel(nuspecPath, ["idx"]);
        var nupkgPath = Path.Combine(workDir, $"{id}-{Guid.NewGuid():N}-full.nupkg");
        var trailerPath = nupkgPath + ".trailer";
        var pointer = ChunkedZipWriter.Write(stagingDir, index, nupkgPath, trailerPath);
        return new Packed {
            PackDir = packDir,
            StagingDir = stagingDir,
            NupkgPath = nupkgPath,
            TrailerPath = trailerPath,
            Index = index,
            Pointer = pointer,
        };
    }

    public static Packed PackDefault(string workDir, string id = "MyApp", string version = "1.0.0", ulong seed = 1)
    {
        var packDir = Path.Combine(workDir, "pack-" + Guid.NewGuid().ToString("N").Substring(0, 8));
        var nuspec = CreateDefaultPackDir(packDir, id, version, seed);
        return Pack(workDir, packDir, nuspec, id);
    }
}

/// <summary>A minimal zip header parser so tests can compare what the writer put in local and central headers.</summary>
internal sealed class ZipHeaderInfo
{
    public string Name;
    public ushort VersionNeeded;
    public ushort VersionMadeBy; // central only
    public ushort Flags;
    public ushort Method;
    public ushort Time;
    public ushort Date;
    public uint Crc32;
    public long CompressedSize;
    public long UncompressedSize;
    public uint ExternalAttributes; // central only
    public long LocalHeaderOffset; // central: from the header; local: where it was read
    public long DataOffset; // local only
    public bool HasZip64Extra;
    public int ExtraLength;
    public bool RawSizesAreSentinels;

    public static List<ZipHeaderInfo> ReadCentralDirectory(byte[] zip)
    {
        int eocd = -1;
        for (int i = zip.Length - 22; i >= 0; i--) {
            if (LittleEndian.ReadUInt32(zip, i) == 0x06054B50) {
                eocd = i;
                break;
            }
        }

        Assert.True(eocd >= 0, "EOCD not found");
        long entries = LittleEndian.ReadUInt16(zip, eocd + 10);
        long cdOffset = LittleEndian.ReadUInt32(zip, eocd + 16);
        if (entries == 0xFFFF || cdOffset == 0xFFFFFFFF) {
            int locator = eocd - 20;
            Assert.Equal(0x07064B50u, LittleEndian.ReadUInt32(zip, locator));
            int z64 = (int) LittleEndian.ReadUInt64(zip, locator + 8);
            Assert.Equal(0x06064B50u, LittleEndian.ReadUInt32(zip, z64));
            entries = (long) LittleEndian.ReadUInt64(zip, z64 + 32);
            cdOffset = (long) LittleEndian.ReadUInt64(zip, z64 + 48);
        }

        var result = new List<ZipHeaderInfo>();
        int pos = (int) cdOffset;
        for (long e = 0; e < entries; e++) {
            Assert.Equal(0x02014B50u, LittleEndian.ReadUInt32(zip, pos));
            var h = new ZipHeaderInfo {
                VersionMadeBy = LittleEndian.ReadUInt16(zip, pos + 4),
                VersionNeeded = LittleEndian.ReadUInt16(zip, pos + 6),
                Flags = LittleEndian.ReadUInt16(zip, pos + 8),
                Method = LittleEndian.ReadUInt16(zip, pos + 10),
                Time = LittleEndian.ReadUInt16(zip, pos + 12),
                Date = LittleEndian.ReadUInt16(zip, pos + 14),
                Crc32 = LittleEndian.ReadUInt32(zip, pos + 16),
                CompressedSize = LittleEndian.ReadUInt32(zip, pos + 20),
                UncompressedSize = LittleEndian.ReadUInt32(zip, pos + 24),
                ExternalAttributes = LittleEndian.ReadUInt32(zip, pos + 38),
                LocalHeaderOffset = LittleEndian.ReadUInt32(zip, pos + 42),
            };
            int nameLen = LittleEndian.ReadUInt16(zip, pos + 28);
            int extraLen = LittleEndian.ReadUInt16(zip, pos + 30);
            int commentLen = LittleEndian.ReadUInt16(zip, pos + 32);
            Assert.Equal(0, commentLen);
            h.Name = Encoding.UTF8.GetString(zip, pos + 46, nameLen);
            h.ExtraLength = extraLen;
            h.RawSizesAreSentinels = h.CompressedSize == 0xFFFFFFFF || h.UncompressedSize == 0xFFFFFFFF || h.LocalHeaderOffset == 0xFFFFFFFF;
            ParseExtra(zip, pos + 46 + nameLen, extraLen, h, true);
            result.Add(h);
            pos += 46 + nameLen + extraLen + commentLen;
        }

        return result;
    }

    public static ZipHeaderInfo ReadLocalHeader(byte[] zip, long offset)
    {
        int pos = (int) offset;
        Assert.Equal(0x04034B50u, LittleEndian.ReadUInt32(zip, pos));
        var h = new ZipHeaderInfo {
            VersionNeeded = LittleEndian.ReadUInt16(zip, pos + 4),
            Flags = LittleEndian.ReadUInt16(zip, pos + 6),
            Method = LittleEndian.ReadUInt16(zip, pos + 8),
            Time = LittleEndian.ReadUInt16(zip, pos + 10),
            Date = LittleEndian.ReadUInt16(zip, pos + 12),
            Crc32 = LittleEndian.ReadUInt32(zip, pos + 14),
            CompressedSize = LittleEndian.ReadUInt32(zip, pos + 18),
            UncompressedSize = LittleEndian.ReadUInt32(zip, pos + 22),
            LocalHeaderOffset = offset,
        };
        int nameLen = LittleEndian.ReadUInt16(zip, pos + 26);
        int extraLen = LittleEndian.ReadUInt16(zip, pos + 28);
        h.Name = Encoding.UTF8.GetString(zip, pos + 30, nameLen);
        h.ExtraLength = extraLen;
        h.RawSizesAreSentinels = h.CompressedSize == 0xFFFFFFFF || h.UncompressedSize == 0xFFFFFFFF;
        ParseExtra(zip, pos + 30 + nameLen, extraLen, h, false);
        h.DataOffset = offset + 30 + nameLen + extraLen;
        return h;
    }

    private static void ParseExtra(byte[] zip, int start, int length, ZipHeaderInfo h, bool central)
    {
        int x = start;
        while (x + 4 <= start + length) {
            int id = LittleEndian.ReadUInt16(zip, x);
            int size = LittleEndian.ReadUInt16(zip, x + 2);
            int p = x + 4;
            if (id == 0x0001) {
                h.HasZip64Extra = true;
                if (h.UncompressedSize == 0xFFFFFFFF) {
                    h.UncompressedSize = (long) LittleEndian.ReadUInt64(zip, p);
                    p += 8;
                }

                if (h.CompressedSize == 0xFFFFFFFF) {
                    h.CompressedSize = (long) LittleEndian.ReadUInt64(zip, p);
                    p += 8;
                }

                if (central && h.LocalHeaderOffset == 0xFFFFFFFF) {
                    h.LocalHeaderOffset = (long) LittleEndian.ReadUInt64(zip, p);
                    p += 8;
                }

                Assert.Equal(x + 4 + size, p);
            } else {
                Assert.Fail($"unexpected extra field 0x{id:X4} in '{h.Name}'");
            }

            x += 4 + size;
        }
    }
}
