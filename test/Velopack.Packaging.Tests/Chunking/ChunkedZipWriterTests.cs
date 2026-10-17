using System.IO.Compression;
using System.IO.Packaging;
using System.Security.Cryptography;
using System.Text;
using NuGet.Packaging;
using SharpCompress.Readers;
using Velopack.Chunks;
using Velopack.Core;
using Velopack.Packaging.Chunking;
using Velopack.Util;

namespace Velopack.Packaging.Tests.Chunking;

public class ChunkedZipWriterTests
{
    private readonly ITestOutputHelper _output;

    public ChunkedZipWriterTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static byte[] ReadEntry(ZipArchive zip, string name)
    {
        var entry = zip.GetEntry(name);
        Assert.NotNull(entry);
        using var s = entry.Open();
        var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static byte[] Slice(byte[] data, long offset, long length)
    {
        var b = new byte[length];
        Buffer.BlockCopy(data, (int) offset, b, 0, (int) length);
        return b;
    }

    [Fact]
    public void WriterOutputIsReadableByZipArchive()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packed = ChunkTestTree.PackDefault(workDir);

        using var zip = ZipFile.OpenRead(packed.NupkgPath);
        foreach (var file in Directory.EnumerateFiles(packed.StagingDir, "*", SearchOption.AllDirectories)) {
            var name = Path.GetRelativePath(packed.StagingDir, file).Replace('\\', '/');
            Assert.Equal(File.ReadAllBytes(file), ReadEntry(zip, name));
        }

        Assert.Equal(packed.Trailer, ReadEntry(zip, ChunkIndex.TrailerEntryName));
        Assert.Equal(EasyZip.ZipFormatMinDate, zip.Entries[0].LastWriteTime.UtcDateTime);

        // and the lib-csharp package reader sees a normal package with the trailer filtered out
        var pkg = new Velopack.NuGet.ZipPackage(packed.NupkgPath, loadUpdateExe: true);
        Assert.Equal("MyApp", pkg.Id);
        Assert.Equal(File.ReadAllBytes(Path.Combine(packed.PackDir, "Squirrel.exe")), pkg.UpdateExeBytes);
        Assert.DoesNotContain(pkg.Files, f => f.Path.Contains("chunks.idx"));
        Assert.Contains(pkg.Files, f => f.Path.EndsWith("sq.chunks"));

        // EasyZip (the legacy extraction path of lib-csharp) extracts the whole tree
        using var _2 = TempUtil.GetTempDirectory(out var extractDir);
        EasyZip.ExtractZipToDirectory(NullLogger.Instance.ToVelopackLogger(), packed.NupkgPath, extractDir);
        foreach (var file in packed.Index.Files) {
            var extracted = Path.Combine(extractDir, "lib", "app", file.Path);
            Assert.Equal(File.ReadAllBytes(Path.Combine(packed.PackDir, file.Path)), File.ReadAllBytes(extracted));
        }
    }

    [Fact]
    public void WriterOutputIsReadableBySystemIoPackaging()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packed = ChunkTestTree.PackDefault(workDir);

        using var package = Package.Open(packed.NupkgPath, FileMode.Open, FileAccess.Read);
        var parts = package.GetParts().ToDictionary(p => p.Uri.OriginalString, p => p);
        foreach (var file in Directory.EnumerateFiles(packed.StagingDir, "*", SearchOption.AllDirectories)) {
            var name = "/" + Path.GetRelativePath(packed.StagingDir, file).Replace('\\', '/');
            if (name == "/[Content_Types].xml") continue;
            Assert.True(parts.ContainsKey(name), $"part {name} missing");
            var expectedType = name.EndsWith(".rels") ? "application/vnd.openxmlformats-package.relationships+xml" : "application/octet";
            Assert.Equal(expectedType, parts[name].ContentType);
            using var s = parts[name].GetStream();
            var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal(File.ReadAllBytes(file), ms.ToArray());
        }

        Assert.True(parts.ContainsKey("/" + ChunkIndex.TrailerEntryName));
        Assert.Equal("application/octet", parts["/" + ChunkIndex.TrailerEntryName].ContentType);
        Assert.True(parts.ContainsKey("/lib/app/sq.chunks"));
        Assert.Equal("application/octet", parts["/lib/app/sq.chunks"].ContentType);
        var manifest = package.GetRelationshipsByType("http://schemas.microsoft.com/packaging/2010/07/manifest").Single();
        Assert.Equal("/MyApp.nuspec", manifest.TargetUri.OriginalString);
    }

    [Fact]
    public void WriterOutputIsReadableByNuGetPackageArchiveReader()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packed = ChunkTestTree.PackDefault(workDir);

        using var reader = new PackageArchiveReader(packed.NupkgPath);
        var identity = reader.GetIdentity();
        Assert.Equal("MyApp", identity.Id);
        Assert.Equal("1.0.0", identity.Version.ToNormalizedString());
        var files = reader.GetFiles().ToList();
        foreach (var file in packed.Index.Files) {
            Assert.Contains("lib/app/" + file.Path, files);
            using var s = reader.GetStream("lib/app/" + file.Path);
            var ms = new MemoryStream();
            s.CopyTo(ms);
            Assert.Equal(File.ReadAllBytes(Path.Combine(packed.PackDir, file.Path)), ms.ToArray());
        }

        // NuGet's package-file filter excludes the package/ metadata directory
        Assert.Contains(ChunkIndex.TrailerEntryName, files);
        Assert.False(PackageHelper.IsPackageFile(ChunkIndex.TrailerEntryName, PackageSaveMode.Files));
        Assert.True(PackageHelper.IsPackageFile("lib/app/sq.chunks", PackageSaveMode.Files));
        Assert.Contains("lib/app/sq.chunks", reader.GetFiles("lib"));
        Assert.DoesNotContain(ChunkIndex.TrailerEntryName, reader.GetFiles("lib"));
    }

    [Fact]
    public void WriterOutputIsReadableBySharpCompress()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packed = ChunkTestTree.PackDefault(workDir);
        var expected = Directory.EnumerateFiles(packed.StagingDir, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(packed.StagingDir, f).Replace('\\', '/'), File.ReadAllBytes);
        expected[ChunkIndex.TrailerEntryName] = packed.Trailer;

        // streaming reader: walks local headers only, so sizes and CRC must be right in the local header (no bit 3)
        var seen = new HashSet<string>();
        using (var stream = File.OpenRead(packed.NupkgPath))
        using (var reader = ReaderFactory.Open(stream)) {
            while (reader.MoveToNextEntry()) {
                if (reader.Entry.IsDirectory) continue;
                using var es = reader.OpenEntryStream();
                var ms = new MemoryStream();
                es.CopyTo(ms);
                Assert.Equal(expected[reader.Entry.Key], ms.ToArray());
                seen.Add(reader.Entry.Key);
            }
        }

        Assert.Equal(expected.Keys.OrderBy(k => k), seen.OrderBy(k => k));

        // archive reader: central directory
        using var archive = SharpCompress.Archives.Zip.ZipArchive.Open(packed.NupkgPath);
        foreach (var entry in archive.Entries.Where(e => !e.IsDirectory)) {
            using var es = entry.OpenEntryStream();
            var ms = new MemoryStream();
            es.CopyTo(ms);
            Assert.Equal(expected[entry.Key], ms.ToArray());
        }
    }

    [Fact]
    public void LocalHeadersEqualCentralDirectory()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packed = ChunkTestTree.PackDefault(workDir);
        var zip = File.ReadAllBytes(packed.NupkgPath);
        var central = ZipHeaderInfo.ReadCentralDirectory(zip);
        Assert.True(central.Count > 5);

        foreach (var c in central) {
            var l = ZipHeaderInfo.ReadLocalHeader(zip, c.LocalHeaderOffset);
            Assert.Equal(c.Name, l.Name);
            Assert.Equal(c.VersionNeeded, l.VersionNeeded);
            Assert.Equal(c.Flags, l.Flags);
            Assert.Equal(c.Method, l.Method);
            Assert.Equal(c.Time, l.Time);
            Assert.Equal(c.Date, l.Date);
            Assert.Equal(c.Crc32, l.Crc32);
            Assert.Equal(c.CompressedSize, l.CompressedSize);
            Assert.Equal(c.UncompressedSize, l.UncompressedSize);
            Assert.Equal(c.HasZip64Extra, l.HasZip64Extra);
            Assert.Equal(c.HasZip64Extra ? 28 : 0, c.ExtraLength);
            Assert.Equal(l.HasZip64Extra ? 20 : 0, l.ExtraLength); // the only permitted difference: the central offset field

            Assert.Equal(0x0800, c.Flags); // UTF-8 names, never bit 3
            Assert.Equal(c.HasZip64Extra ? 45 : 20, c.VersionNeeded);
            Assert.Equal(20, c.VersionMadeBy);
            Assert.Equal(0, c.Time);
            Assert.Equal(0x0021, c.Date); // 1980-01-01
            Assert.Equal(0u, c.ExternalAttributes);
            Assert.True(c.Method == 0 || c.Method == 8);
            Assert.False(c.HasZip64Extra);
            if (c.Method == 0) Assert.Equal(c.UncompressedSize, c.CompressedSize);
            else Assert.True(c.CompressedSize < c.UncompressedSize);

            // the local header is immediately followed by exactly compressed_size bytes of data, then the next header
            long endOfData = l.DataOffset + l.CompressedSize;
            var next = central.FirstOrDefault(n => n.LocalHeaderOffset > c.LocalHeaderOffset && n.LocalHeaderOffset == endOfData);
            if (c != central[^1]) Assert.NotNull(next);
        }
    }

    [Fact]
    public void EveryChunkInflatesAloneWithTerminator()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packed = ChunkTestTree.PackDefault(workDir);
        var zip = File.ReadAllBytes(packed.NupkgPath);
        var index = packed.ParsedTrailer;
        int deflateChunks = 0, storedChunks = 0;

        for (int i = 0; i < index.FileCount; i++) {
            var file = index.GetFile(i);
            var transport = index.GetTransportFile(i);
            var plain = File.ReadAllBytes(Path.Combine(packed.PackDir, file.Path));
            long offset = 0;
            for (int c = 0; c < file.ChunkCount; c++) {
                var chunk = index.GetChunk(file.FirstChunk + c);
                var range = index.GetTransportChunk(file.FirstChunk + c);
                var bytes = Slice(zip, range.Offset, range.Length);
                var decoded = ChunkInflate.TryDecodeChunk(bytes, 0, bytes.Length, transport.Method, chunk.Length, chunk.Sha256);
                Assert.NotNull(decoded);
                Assert.Equal(Slice(plain, offset, chunk.Length), decoded);
                if (transport.IsDeflate) {
                    Assert.Equal(ChunkDeflate.SyncFlushMarker, bytes[^4..]);
                    deflateChunks++;
                } else {
                    storedChunks++;
                }

                offset += chunk.Length;
            }
        }

        Assert.True(deflateChunks > 10);
        Assert.True(storedChunks > 1);
    }

    [Fact]
    public void ConcatenatedChunksEqualPlaintext()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packed = ChunkTestTree.PackDefault(workDir);
        var zip = File.ReadAllBytes(packed.NupkgPath);
        var index = packed.ParsedTrailer;

        for (int i = 0; i < index.FileCount; i++) {
            var file = index.GetFile(i);
            var transport = index.GetTransportFile(i);
            if (!transport.IsDeflate) continue;
            var ms = new MemoryStream();
            for (int c = 0; c < file.ChunkCount; c++) {
                var range = index.GetTransportChunk(file.FirstChunk + c);
                ms.Write(zip, (int) range.Offset, range.Length);
            }

            ms.Write(ChunkDeflate.FinalBlock);
            Assert.Equal(transport.CompressedSize, ms.Length);
            Assert.Equal(Slice(zip, transport.DataOffset, transport.CompressedSize), ms.ToArray());

            ms.Position = 0;
            using var inflater = new DeflateStream(ms, CompressionMode.Decompress);
            var outMs = new MemoryStream();
            inflater.CopyTo(outMs);
            Assert.Equal(File.ReadAllBytes(Path.Combine(packed.PackDir, file.Path)), outMs.ToArray());
        }
    }

    [Fact]
    public void ChunkRangesAreContiguousAndCoverEntryData()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packed = ChunkTestTree.PackDefault(workDir);
        var index = packed.ParsedTrailer; // ParseTrailer already enforces contiguity; re-check explicitly
        long packageSize = new FileInfo(packed.NupkgPath).Length;

        for (int i = 0; i < index.FileCount; i++) {
            var file = index.GetFile(i);
            var transport = index.GetTransportFile(i);
            if (file.Length == 0) {
                Assert.Equal(0, transport.DataOffset);
                Assert.Equal(0, transport.CompressedSize);
                continue;
            }

            long expected = transport.DataOffset;
            for (int c = 0; c < file.ChunkCount; c++) {
                var range = index.GetTransportChunk(file.FirstChunk + c);
                Assert.Equal(expected, range.Offset);
                Assert.True(range.Length > 0);
                Assert.True(range.Offset + range.Length <= packageSize);
                expected += range.Length;
                if (!transport.IsDeflate) Assert.Equal(index.GetChunkLength(file.FirstChunk + c), range.Length);
            }

            Assert.Equal(transport.DataOffset + transport.CompressedSize - (transport.IsDeflate ? 2 : 0), expected);
        }
    }

    [Fact]
    public void BRecordsMatchActualHeaders()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packed = ChunkTestTree.PackDefault(workDir);
        var zip = File.ReadAllBytes(packed.NupkgPath);
        var central = ZipHeaderInfo.ReadCentralDirectory(zip).ToDictionary(c => c.Name);
        var index = packed.ParsedTrailer;

        for (int i = 0; i < index.FileCount; i++) {
            var file = index.GetFile(i);
            var transport = index.GetTransportFile(i);
            var header = ZipHeaderInfo.ReadLocalHeader(zip, central["lib/app/" + file.Path].LocalHeaderOffset);
            Assert.Equal(file.Length, header.UncompressedSize);
            if (file.Length == 0) {
                Assert.Equal(0, header.Method);
                Assert.Equal(0, header.CompressedSize);
                continue;
            }

            Assert.Equal(transport.Method, header.Method);
            Assert.Equal(transport.DataOffset, header.DataOffset);
            Assert.Equal(transport.CompressedSize, header.CompressedSize);
        }

        var trailer = ZipHeaderInfo.ReadLocalHeader(zip, central[ChunkIndex.TrailerEntryName].LocalHeaderOffset);
        Assert.Equal(packed.Pointer.Offset, trailer.DataOffset);
        Assert.Equal(packed.Pointer.Length, trailer.CompressedSize);
        Assert.Equal(0, trailer.Method);
    }

    [Fact]
    public void StoredVsDeflateDecisionUsesSummedChunkSizes()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packed = ChunkTestTree.PackDefault(workDir);
        var index = packed.ParsedTrailer;
        bool sawStored = false, sawDeflate = false;

        for (int i = 0; i < index.FileCount; i++) {
            var file = index.GetFile(i);
            if (file.Length == 0) continue;
            var transport = index.GetTransportFile(i);
            var plain = File.ReadAllBytes(Path.Combine(packed.PackDir, file.Path));
            long sum = 2;
            int offset = 0;
            for (int c = 0; c < file.ChunkCount; c++) {
                int len = index.GetChunkLength(file.FirstChunk + c);
                sum += ChunkDeflate.CompressChunk(plain, offset, len).Length;
                offset += len;
            }

            bool expectDeflate = sum < file.Length;
            Assert.Equal(expectDeflate, transport.IsDeflate);
            if (expectDeflate) {
                Assert.Equal(sum, transport.CompressedSize);
                sawDeflate = true;
            } else {
                Assert.Equal(file.Length, transport.CompressedSize);
                sawStored = true;
            }
        }

        Assert.True(sawStored, "expected an incompressible (stored) file");
        Assert.True(sawDeflate, "expected a compressible (deflate) file");
    }

    [Fact]
    public void EmptyFilesAreStoredZeroBytes()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packed = ChunkTestTree.PackDefault(workDir);
        var zip = File.ReadAllBytes(packed.NupkgPath);
        var central = ZipHeaderInfo.ReadCentralDirectory(zip).Single(c => c.Name == "lib/app/empty.txt");
        Assert.Equal(0, central.Method);
        Assert.Equal(0, central.CompressedSize);
        Assert.Equal(0, central.UncompressedSize);
        Assert.Equal(0u, central.Crc32);

        var index = packed.ParsedTrailer;
        var record = index.GetFile(index.FindFile("empty.txt"));
        Assert.Equal(0, record.ChunkCount);
        var transport = index.GetTransportFile(index.FindFile("empty.txt"));
        Assert.Equal(0, transport.Method);
        Assert.Equal(0, transport.DataOffset);
        Assert.Equal(0, transport.CompressedSize);
    }

    [Fact]
    public void EmptyDirectoriesGetDirEntriesButNoRecords()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packDir = Path.Combine(workDir, "pack");
        var nuspec = ChunkTestTree.CreateDefaultPackDir(packDir);
        Directory.CreateDirectory(Path.Combine(packDir, "plugins"));
        Directory.CreateDirectory(Path.Combine(packDir, "data", "cache", "deeper"));
        var packed = ChunkTestTree.Pack(workDir, packDir, nuspec, "MyApp");

        using var zip = ZipFile.OpenRead(packed.NupkgPath);
        var names = zip.Entries.Select(e => e.FullName).ToList();
        Assert.Contains("lib/app/plugins/", names);
        // EasyZip parity: a directory whose only content is another directory is not "empty", so the entry is the innermost one
        Assert.Contains("lib/app/data/cache/deeper/", names);
        Assert.DoesNotContain("lib/app/data/cache/", names);
        Assert.DoesNotContain("lib/app/data/", names); // non-empty directories get no entry
        var dirEntry = zip.GetEntry("lib/app/plugins/")!;
        Assert.Equal(0, dirEntry.Length);
        Assert.Equal(0, dirEntry.CompressedLength);

        var index = packed.ParsedTrailer;
        Assert.Equal(-1, index.FindFile("plugins"));
        Assert.Equal(-1, index.FindFile("data/cache"));
        for (int i = 0; i < index.FileCount; i++) Assert.Equal(ChunkFileKind.File, index.GetFileKind(i));

        // the classic extractor skips "name/" entries, so an extracted tree has the file set of the index only
        using var _2 = TempUtil.GetTempDirectory(out var extractDir);
        EasyZip.ExtractZipToDirectory(NullLogger.Instance.ToVelopackLogger(), packed.NupkgPath, extractDir);
        Assert.True(Directory.Exists(Path.Combine(extractDir, "lib", "app", "plugins")));
        Assert.True(Directory.Exists(Path.Combine(extractDir, "lib", "app", "data", "cache", "deeper")));
    }

    [Fact]
    public void StreamingAndBatchedEntriesAgree()
    {
        // a file with more chunks than one batch takes the write-then-patch path; both an incompressible one (stored after
        // seek-back) and a compressible one (header patched) must produce the same bytes as a single-batch entry would
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packDir = Path.Combine(workDir, "pack");
        var nuspec = ChunkTestTree.CreateDefaultPackDir(packDir);
        var random = ChunkTestTree.RandomBytes(21, 64 * FastCdc.AvgSize);
        var text = ChunkTestTree.CompressibleBytes(22, 64 * FastCdc.AvgSize);
        ChunkTestTree.WriteFile(packDir, "big-random.bin", random);
        ChunkTestTree.WriteFile(packDir, "big-text.txt", text);
        var packed = ChunkTestTree.Pack(workDir, packDir, nuspec, "MyApp");
        var index = packed.ParsedTrailer;
        var zip = File.ReadAllBytes(packed.NupkgPath);
        var central = ZipHeaderInfo.ReadCentralDirectory(zip).ToDictionary(c => c.Name);

        int randomIdx = index.FindFile("big-random.bin");
        Assert.True(index.GetFile(randomIdx).ChunkCount > 32);
        var randomTransport = index.GetTransportFile(randomIdx);
        Assert.False(randomTransport.IsDeflate);
        Assert.Equal(random, Slice(zip, randomTransport.DataOffset, randomTransport.CompressedSize));
        var randomLocal = ZipHeaderInfo.ReadLocalHeader(zip, central["lib/app/big-random.bin"].LocalHeaderOffset);
        Assert.Equal(0, randomLocal.Method);
        Assert.Equal(random.Length, randomLocal.CompressedSize);

        int textIdx = index.FindFile("big-text.txt");
        Assert.True(index.GetFile(textIdx).ChunkCount > 32);
        var textTransport = index.GetTransportFile(textIdx);
        Assert.True(textTransport.IsDeflate);
        var textLocal = ZipHeaderInfo.ReadLocalHeader(zip, central["lib/app/big-text.txt"].LocalHeaderOffset);
        Assert.Equal(8, textLocal.Method);
        Assert.Equal(textTransport.CompressedSize, textLocal.CompressedSize);
        long expected = 2;
        int offset = 0;
        for (int c = 0; c < index.GetFile(textIdx).ChunkCount; c++) {
            int len = index.GetChunkLength(index.GetFile(textIdx).FirstChunk + c);
            expected += ChunkDeflate.CompressChunk(text, offset, len).Length;
            offset += len;
        }

        Assert.Equal(expected, textTransport.CompressedSize);

        using var archive = ZipFile.OpenRead(packed.NupkgPath);
        Assert.Equal(text, ReadEntry(archive, "lib/app/big-text.txt"));
        Assert.Equal(random, ReadEntry(archive, "lib/app/big-random.bin"));
    }

    [Fact]
    public void EntryOrderIsRootThenUpdaterThenOrdinalWithTrailerLast()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packDir = Path.Combine(workDir, "pack");
        var nuspec = ChunkTestTree.CreateDefaultPackDir(packDir);
        ChunkTestTree.WriteFile(packDir, "Zed.dll", new byte[100]);
        ChunkTestTree.WriteFile(packDir, "aaa.dll", new byte[100]);
        ChunkTestTree.WriteFile(packDir, "Ümlaut.txt", new byte[100]);
        var packed = ChunkTestTree.Pack(
            workDir,
            packDir,
            nuspec,
            "MyApp",
            new Dictionary<string, byte[]> { ["setup.ico"] = new byte[10], ["splashimage.png"] = new byte[20] });

        var zip = File.ReadAllBytes(packed.NupkgPath);
        var names = ZipHeaderInfo.ReadCentralDirectory(zip).Select(c => c.Name).ToList();
        var local = ZipHeaderInfo.ReadCentralDirectory(zip).OrderBy(c => c.LocalHeaderOffset).Select(c => c.Name).ToList();
        Assert.Equal(names, local); // central directory order == local header order

        int firstLib = names.FindIndex(n => n.StartsWith("lib/"));
        var rootNames = names.Take(firstLib).ToList();
        Assert.Equal(["MyApp.nuspec", "[Content_Types].xml", "_rels/.rels", "setup.ico", "splashimage.png"], rootNames);
        Assert.Equal("lib/app/Squirrel.exe", names[firstLib]);
        var libNames = names.Skip(firstLib + 1).Take(names.Count - firstLib - 2).ToList();
        Assert.All(libNames, n => Assert.StartsWith("lib/", n));
        Assert.Equal(libNames.OrderBy(n => Encoding.UTF8.GetBytes(n), new ByteArrayComparer()).ToList(), libNames);
        Assert.Equal(ChunkIndex.TrailerEntryName, names[^1]);

        // ordinal means upper-case before lower-case, and non-ASCII last
        Assert.True(libNames.IndexOf("lib/app/Zed.dll") < libNames.IndexOf("lib/app/aaa.dll"));
        Assert.True(libNames.IndexOf("lib/app/aaa.dll") < libNames.IndexOf("lib/app/Ümlaut.txt"));
    }

    private sealed class ByteArrayComparer : IComparer<byte[]>
    {
        public int Compare(byte[] x, byte[] y) => PackContentIndex.CompareBytes(x, y);
    }

    [Fact]
    public void FirstSuffixMatchesAreRootNuspecAndLibAppSquirrelExe()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packDir = Path.Combine(workDir, "pack");
        var nuspec = ChunkTestTree.CreateDefaultPackDir(packDir);

        // a nested Squirrel.exe decoy is rejected at index time (old extractors take the first suffix match)
        ChunkTestTree.WriteFile(packDir, "Plugins/Squirrel.exe", new byte[10]);
        Assert.Throws<UserInfoException>(() => ChunkIndexBuilder.Build(packDir));
        File.Delete(Path.Combine(packDir, "Plugins", "Squirrel.exe"));

        // a nested *.nuspec is allowed, and the root nuspec is still the first .nuspec match in central directory order
        ChunkTestTree.WriteFile(packDir, "Plugins/AAA.nuspec", Encoding.UTF8.GetBytes("<package/>"));
        ChunkTestTree.WriteFile(packDir, "00.nuspec", Encoding.UTF8.GetBytes("<package/>"));
        var packed = ChunkTestTree.Pack(workDir, packDir, nuspec, "MyApp");

        var names = ZipHeaderInfo.ReadCentralDirectory(File.ReadAllBytes(packed.NupkgPath)).Select(c => c.Name).ToList();
        Assert.Equal("MyApp.nuspec", names.First(n => n.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal("lib/app/Squirrel.exe", names.First(n => n.EndsWith("Squirrel.exe")));

        using var zip = ZipFile.OpenRead(packed.NupkgPath);
        Assert.Equal("MyApp.nuspec", zip.Entries.First(e => e.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)).FullName);
        Assert.Equal("lib/app/Squirrel.exe", zip.Entries.First(e => e.FullName.EndsWith("Squirrel.exe")).FullName);
        var pkg = new Velopack.NuGet.ZipPackage(packed.NupkgPath);
        Assert.Equal("MyApp", pkg.Id);
    }

#if NET8_0
    [Fact]
    public void RepackIsByteIdenticalOnNet8()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packDir = Path.Combine(workDir, "pack");
        var nuspec = ChunkTestTree.CreateDefaultPackDir(packDir);
        var first = ChunkTestTree.Pack(workDir, packDir, nuspec, "MyApp");
        var second = ChunkTestTree.Pack(workDir, packDir, nuspec, "MyApp");
        Assert.Equal(File.ReadAllBytes(first.NupkgPath), File.ReadAllBytes(second.NupkgPath));
        Assert.Equal(first.Pointer.Sha256, second.Pointer.Sha256);
        Assert.Equal(first.Pointer.Offset, second.Pointer.Offset);
    }
#endif

    [Fact]
    public void Zip64ManyEntries()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var stagingDir = Path.Combine(workDir, "staging");
        var packDir = Path.Combine(stagingDir, "lib", "app");
        var nuspec = ChunkTestTree.CreateDefaultPackDir(packDir);
        const int count = 70_000;
        var sw = System.Diagnostics.Stopwatch.StartNew();
        for (int i = 0; i < count; i++) {
            var dir = Path.Combine(packDir, "many", (i / 1000).ToString());
            if (i % 1000 == 0) Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, $"f{i}.txt"), i.ToString());
        }

        _output.WriteLine($"created {count} files in {sw.Elapsed}");
        sw.Restart();
        var packed = ChunkTestTree.PackInPlace(workDir, stagingDir, nuspec, "MyApp");
        _output.WriteLine($"indexed and packed in {sw.Elapsed}");
        var zip = File.ReadAllBytes(packed.NupkgPath);
        Assert.Equal(0xFFFF, LittleEndian.ReadUInt16(zip, zip.Length - 22 + 10)); // EOCD entry count sentinel
        var central = ZipHeaderInfo.ReadCentralDirectory(zip);
        Assert.True(central.Count > count);
        Assert.All(central, c => Assert.False(c.HasZip64Extra)); // only the EOCD is Zip64, not the entries

        using (var archive = ZipFile.OpenRead(packed.NupkgPath)) {
            Assert.Equal(central.Count, archive.Entries.Count);
            Assert.Equal("12345", Encoding.ASCII.GetString(ReadEntry(archive, "lib/app/many/12/f12345.txt")));
        }

        using (var archive = SharpCompress.Archives.Zip.ZipArchive.Open(packed.NupkgPath)) {
            Assert.Equal(central.Count, archive.Entries.Count);
        }

        var index = packed.ParsedTrailer;
        Assert.Equal(central.Count - 4 - 1, index.FileCount); // minus nuspec, content types, rels, trailer
        var asset = VelopackAsset.FromNupkgNoChecksum(packed.NupkgPath);
        Assert.Equal(packed.Pointer.Offset, asset.ChunkIndexOffset);
    }

    [Fact(Explicit = true)]
    public void Zip64LargeEntry()
    {
        // one entry over 4 GiB (zeros, so it deflates to a few MiB): Zip64 local/central extras on the entry itself
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packDir = Path.Combine(workDir, "pack");
        var nuspec = ChunkTestTree.CreateDefaultPackDir(packDir);
        var bigPath = Path.Combine(packDir, "big.bin");
        using (var fs = new FileStream(bigPath, FileMode.Create)) fs.SetLength(4L * 1024 * 1024 * 1024 + 1234);

        var packed = ChunkTestTree.Pack(workDir, packDir, nuspec, "MyApp");
        using var fileStream = File.OpenRead(packed.NupkgPath);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);
        var entry = archive.GetEntry("lib/app/big.bin")!;
        Assert.Equal(4L * 1024 * 1024 * 1024 + 1234, entry.Length);
        var index = ChunkIndex.ParseTrailer(packed.Trailer, fileStream.Length);
        Assert.Equal(entry.Length, index.GetFileLength(index.FindFile("big.bin")));
        var asset = VelopackAsset.FromNupkgNoChecksum(packed.NupkgPath);
        Assert.Equal(packed.Pointer.Sha256, asset.ChunkIndexSHA256);
    }

    [Fact(Explicit = true)]
    public void Zip64LargeArchive()
    {
        // incompressible entries pushing the trailer past 4 GiB: Zip64 offsets in the central directory and EOCD
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packDir = Path.Combine(workDir, "pack");
        var nuspec = ChunkTestTree.CreateDefaultPackDir(packDir);
        var rng = new Random(1);
        var buffer = new byte[16 * 1024 * 1024];
        for (int i = 0; i < 260; i++) {
            rng.NextBytes(buffer);
            File.WriteAllBytes(Path.Combine(packDir, $"big{i}.bin"), buffer);
        }

        var packed = ChunkTestTree.Pack(workDir, packDir, nuspec, "MyApp");
        Assert.True(packed.Pointer.Offset > 0xFFFFFFFFL);
        using var fileStream = File.OpenRead(packed.NupkgPath);
        using var archive = new ZipArchive(fileStream, ZipArchiveMode.Read);
        Assert.Equal(packed.Trailer.Length, archive.GetEntry(ChunkIndex.TrailerEntryName)!.Length);
        var asset = VelopackAsset.FromNupkgNoChecksum(packed.NupkgPath);
        Assert.Equal(packed.Pointer.Offset, asset.ChunkIndexOffset);
        Assert.Equal(packed.Pointer.Sha256, asset.ChunkIndexSHA256);
    }

    [Fact]
    public void FeedPointerFromFileMatchesWriterResult()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packed = ChunkTestTree.PackDefault(workDir);
        var asset = VelopackAsset.FromNupkgNoChecksum(packed.NupkgPath);
        Assert.True(asset.HasChunkIndex);
        Assert.Equal(packed.Pointer.Offset, asset.ChunkIndexOffset);
        Assert.Equal(packed.Pointer.Length, asset.ChunkIndexLength);
        Assert.Equal(packed.Pointer.Sha256, asset.ChunkIndexSHA256);
        Assert.Equal(Convert.ToHexString(SHA256.HashData(packed.Trailer)), asset.ChunkIndexSHA256);
        Assert.Equal(packed.Trailer, Slice(File.ReadAllBytes(packed.NupkgPath), packed.Pointer.Offset, packed.Pointer.Length));
        Assert.Equal(new FileInfo(packed.NupkgPath).Length, asset.Size);
        Assert.Equal(VelopackAssetType.Full, asset.Type);

        // the trailer's Section A is byte-identical to lib/app/sq.chunks
        using var zip = ZipFile.OpenRead(packed.NupkgPath);
        Assert.Equal(ReadEntry(zip, "lib/app/sq.chunks"), Slice(packed.Trailer, 0, packed.Index.SectionALength));
        var feedJson = ReleaseEntryHelper.GetAssetFeedJson(new VelopackAssetFeed { Assets = [asset] });
        Assert.Contains("\"ChunkIndexOffset\"", feedJson);
        Assert.Contains("\"ChunkIndexLength\"", feedJson);
        Assert.Contains("\"ChunkIndexSHA256\"", feedJson);
    }

    [Fact]
    public void NonAsciiPathsRoundTrip()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var packDir = Path.Combine(workDir, "pack");
        var nuspec = ChunkTestTree.CreateDefaultPackDir(packDir);
        var content = ChunkTestTree.CompressibleBytes(99, 70_000);
        ChunkTestTree.WriteFile(packDir, "Übersicht/日本語.txt", content);
        ChunkTestTree.WriteFile(packDir, "émoji 🎉.bin", ChunkTestTree.RandomBytes(98, 5000));
        var packed = ChunkTestTree.Pack(workDir, packDir, nuspec, "MyApp");

        using (var zip = ZipFile.OpenRead(packed.NupkgPath)) {
            Assert.Equal(content, ReadEntry(zip, "lib/app/Übersicht/日本語.txt"));
            Assert.Equal(5000, ReadEntry(zip, "lib/app/émoji 🎉.bin").Length);
        }

        using (var archive = SharpCompress.Archives.Zip.ZipArchive.Open(packed.NupkgPath)) {
            var entry = archive.Entries.Single(e => e.Key == "lib/app/Übersicht/日本語.txt");
            using var es = entry.OpenEntryStream();
            var ms = new MemoryStream();
            es.CopyTo(ms);
            Assert.Equal(content, ms.ToArray());
        }

        var index = packed.ParsedTrailer;
        Assert.True(index.FindFile("Übersicht/日本語.txt") >= 0);
        Assert.True(index.FindFile("émoji 🎉.bin") >= 0);

        using var _2 = TempUtil.GetTempDirectory(out var extractDir);
        EasyZip.ExtractZipToDirectory(NullLogger.Instance.ToVelopackLogger(), packed.NupkgPath, extractDir);
        Assert.Equal(content, File.ReadAllBytes(Path.Combine(extractDir, "lib", "app", "Übersicht", "日本語.txt")));
    }
}
