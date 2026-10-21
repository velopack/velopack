#nullable enable
using System.IO.Compression;
using System.Text;
using Velopack.Chunks;
using Velopack.Tests.Chunks;
using Velopack.Util;

namespace Velopack.Tests;

public class ZipCentralDirectoryTests
{
    private static byte[] Trailer => TestIndexBuilder.DeterministicBytes(77, 5000);

    private static byte[] ReadAt(Stream s, ZipEntryLocation loc)
    {
        var buf = new byte[loc.Length];
        s.Seek(loc.Offset, SeekOrigin.Begin);
        int total = 0;
        while (total < buf.Length) total += s.Read(buf, total, buf.Length - total);
        return buf;
    }

    private static MemoryStream BuildArchive(CompressionLevel trailerLevel, bool includeTrailer = true, string? comment = null)
    {
        var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true)) {
            using (var s = zip.CreateEntry("App.nuspec", CompressionLevel.Optimal).Open()) s.Write(Encoding.UTF8.GetBytes(new string('x', 2000)));
            using (var s = zip.CreateEntry("lib/app/MyApp.exe", CompressionLevel.Optimal).Open()) s.Write(new byte[10_000]);
            using (var s = zip.CreateEntry("package/velopack/decoy.idx", CompressionLevel.NoCompression).Open()) s.Write(new byte[10]);
            if (includeTrailer) {
                using var s = zip.CreateEntry(ChunkIndex.TrailerEntryName, trailerLevel).Open();
                s.Write(Trailer);
            }
            using (var s = zip.CreateEntry("lib/app/zzz.txt", CompressionLevel.NoCompression).Open()) s.Write(new byte[3]);
            if (comment != null) zip.Comment = comment;
        }
        ms.Position = 0;
        return ms;
    }

    [Fact]
    public void FindsTrailerInPlainArchive()
    {
        using var ms = BuildArchive(CompressionLevel.NoCompression);
        var loc = ZipCentralDirectory.FindEntryData(ms, ChunkIndex.TrailerEntryName);
        Assert.NotNull(loc);
        Assert.Equal(Trailer.Length, loc!.Length);
        Assert.Equal(Trailer, ReadAt(ms, loc));

        // the data offset sits right after the local header (including its name and any extra field) of that entry
        var header = new byte[30];
        var extra = 0;
        var all = ms.ToArray();
        int local = -1;
        for (int i = 0; i < all.Length - 4; i++) {
            if (all[i] == 0x50 && all[i + 1] == 0x4B && all[i + 2] == 0x03 && all[i + 3] == 0x04) {
                int nameLen = BitConverter.ToUInt16(all, i + 26);
                if (nameLen == ChunkIndex.TrailerEntryName.Length && Encoding.UTF8.GetString(all, i + 30, nameLen) == ChunkIndex.TrailerEntryName) {
                    local = i;
                    extra = BitConverter.ToUInt16(all, i + 28);
                    Array.Copy(all, i, header, 0, 30);
                    break;
                }
            }
        }
        Assert.True(local >= 0);
        Assert.Equal(local + 30 + ChunkIndex.TrailerEntryName.Length + extra, loc.Offset);
        Assert.Equal(0, BitConverter.ToUInt16(header, 8)); // method stored
    }

    [Fact]
    public void FindsTrailerWithArchiveComment()
    {
        using var ms = BuildArchive(CompressionLevel.NoCompression, comment: "a comment that pushes the EOCD record away from the end of the file");
        var loc = ZipCentralDirectory.FindEntryData(ms, ChunkIndex.TrailerEntryName);
        Assert.NotNull(loc);
        Assert.Equal(Trailer, ReadAt(ms, loc!));
    }

    [Fact]
    public void ReturnsNullWhenAbsent()
    {
        using var ms = BuildArchive(CompressionLevel.NoCompression, includeTrailer: false);
        Assert.Null(ZipCentralDirectory.FindEntryData(ms, ChunkIndex.TrailerEntryName));
        Assert.Null(ZipCentralDirectory.FindEntryData(ms, "PACKAGE/velopack/chunks.idx"));
    }

    [Fact]
    public void RejectsDeflatedTrailer()
    {
        using var ms = BuildArchive(CompressionLevel.Optimal);
        var ex = Assert.Throws<InvalidDataException>(() => ZipCentralDirectory.FindEntryData(ms, ChunkIndex.TrailerEntryName));
        Assert.Contains("method 8", ex.Message);
    }

    [Fact]
    public void RejectsNonZip()
    {
        Assert.Throws<InvalidDataException>(() => ZipCentralDirectory.FindEntryData(new MemoryStream(new byte[10]), "x"));
        Assert.Throws<InvalidDataException>(() => ZipCentralDirectory.FindEntryData(new MemoryStream(new byte[100_000]), "x"));
    }

    [Fact]
    public void FindsTrailerWithZip64()
    {
        // a hand-written archive using Zip64 for the entry (sizes and offset in the extra field) and for the EOCD
        var trailer = Trailer;
        var ms = new MemoryStream();
        var w = new BinaryWriter(ms);
        var first = Encoding.UTF8.GetBytes("first.txt");
        var firstData = Encoding.UTF8.GetBytes("hello");
        var name = Encoding.UTF8.GetBytes(ChunkIndex.TrailerEntryName);

        long firstOffset = ms.Position;
        WriteLocalHeader(w, first, firstData, zip64: false);
        w.Write(firstData);

        long trailerOffset = ms.Position;
        WriteLocalHeader(w, name, trailer, zip64: true);
        long dataOffset = ms.Position;
        w.Write(trailer);

        long cdOffset = ms.Position;
        WriteCentralHeader(w, first, firstData, firstOffset, zip64: false);
        WriteCentralHeader(w, name, trailer, trailerOffset, zip64: true);
        long cdSize = ms.Position - cdOffset;

        long zip64EocdOffset = ms.Position;
        w.Write(0x06064B50u);
        w.Write(44UL);
        w.Write((ushort) 45);
        w.Write((ushort) 45);
        w.Write(0u);
        w.Write(0u);
        w.Write(2UL);
        w.Write(2UL);
        w.Write((ulong) cdSize);
        w.Write((ulong) cdOffset);
        w.Write(0x07064B50u);
        w.Write(0u);
        w.Write((ulong) zip64EocdOffset);
        w.Write(1u);
        w.Write(0x06054B50u);
        w.Write((ushort) 0);
        w.Write((ushort) 0);
        w.Write((ushort) 0xFFFF);
        w.Write((ushort) 0xFFFF);
        w.Write(0xFFFFFFFFu);
        w.Write(0xFFFFFFFFu);
        w.Write((ushort) 0);
        w.Flush();

        ms.Position = 0;
        var loc = ZipCentralDirectory.FindEntryData(ms, ChunkIndex.TrailerEntryName);
        Assert.NotNull(loc);
        Assert.Equal(dataOffset, loc!.Offset);
        Assert.Equal(trailer.Length, loc.Length);
        Assert.Equal(trailer, ReadAt(ms, loc));

        // and the archive is a real zip: .NET reads it back
        ms.Position = 0;
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);
        Assert.Equal(2, zip.Entries.Count);
        using var es = zip.GetEntry(ChunkIndex.TrailerEntryName)!.Open();
        var back = new MemoryStream();
        es.CopyTo(back);
        Assert.Equal(trailer, back.ToArray());
    }

    private static void WriteLocalHeader(BinaryWriter w, byte[] name, byte[] data, bool zip64)
    {
        w.Write(0x04034B50u);
        w.Write((ushort) (zip64 ? 45 : 20));
        w.Write((ushort) 0x0800);
        w.Write((ushort) 0);
        w.Write((ushort) 0);
        w.Write((ushort) 0x0021);
        w.Write(Crc32(data));
        w.Write(zip64 ? 0xFFFFFFFFu : (uint) data.Length);
        w.Write(zip64 ? 0xFFFFFFFFu : (uint) data.Length);
        w.Write((ushort) name.Length);
        w.Write((ushort) (zip64 ? 20 : 0));
        w.Write(name);
        if (zip64) {
            w.Write((ushort) 0x0001);
            w.Write((ushort) 16);
            w.Write((ulong) data.Length);
            w.Write((ulong) data.Length);
        }
    }

    private static void WriteCentralHeader(BinaryWriter w, byte[] name, byte[] data, long localOffset, bool zip64)
    {
        w.Write(0x02014B50u);
        w.Write((ushort) (zip64 ? 45 : 20));
        w.Write((ushort) (zip64 ? 45 : 20));
        w.Write((ushort) 0x0800);
        w.Write((ushort) 0);
        w.Write((ushort) 0);
        w.Write((ushort) 0x0021);
        w.Write(Crc32(data));
        w.Write(zip64 ? 0xFFFFFFFFu : (uint) data.Length);
        w.Write(zip64 ? 0xFFFFFFFFu : (uint) data.Length);
        w.Write((ushort) name.Length);
        w.Write((ushort) (zip64 ? 28 : 0));
        w.Write((ushort) 0);
        w.Write((ushort) 0);
        w.Write((ushort) 0);
        w.Write(0u);
        w.Write(zip64 ? 0xFFFFFFFFu : (uint) localOffset);
        w.Write(name);
        if (zip64) {
            w.Write((ushort) 0x0001);
            w.Write((ushort) 24);
            w.Write((ulong) data.Length);
            w.Write((ulong) data.Length);
            w.Write((ulong) localOffset);
        }
    }

    private static uint Crc32(byte[] data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (var b in data) {
            crc ^= b;
            for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320 : crc >> 1;
        }
        return ~crc;
    }

    [Fact]
    public void FeedPointerIsFilledFromNupkg()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var plain = PathHelper.CopyFixtureTo("AvaloniaCrossPlat-1.0.11-win-full.nupkg", dir);
        var withTrailer = Path.Combine(dir, "AvaloniaCrossPlat-1.0.12-win-full.nupkg");
        File.Copy(plain, withTrailer);
        var trailer = File.ReadAllBytes(Path.Combine(DeltaV2Fixtures.FixtureDir, "index_sample.bin"));
        using (var zip = ZipFile.Open(withTrailer, ZipArchiveMode.Update)) {
            using var s = zip.CreateEntry(ChunkIndex.TrailerEntryName, CompressionLevel.NoCompression).Open();
            s.Write(trailer);
        }

        var asset = VelopackAsset.FromNupkgNoChecksum(withTrailer);
        Assert.True(asset.HasChunkIndex);
        Assert.Equal(trailer.Length, asset.ChunkIndexLength);
        Assert.Equal(ChunkInflate.Sha256(trailer).ToHex(), asset.ChunkIndexSHA256);
        using (var fs = File.OpenRead(withTrailer)) {
            Assert.Equal(trailer, ReadAt(fs, new ZipEntryLocation(asset.ChunkIndexOffset!.Value, asset.ChunkIndexLength!.Value)));
        }
        Assert.True(asset.ChunkIndexOffset + asset.ChunkIndexLength <= asset.Size);
        Assert.Equal(new FileInfo(withTrailer).Length, asset.Size);
        var json = CompiledJson.SerializeVelopackAsset(asset);
        Assert.Contains("\"ChunkIndexSHA256\"", json);

        var checksummed = VelopackAsset.FromNupkgGenerateChecksumAsync(withTrailer).GetAwaiterResult();
        Assert.Equal(asset.ChunkIndexSHA256, checksummed.ChunkIndexSHA256);
        Assert.NotNull(checksummed.SHA256);

        var noTrailer = VelopackAsset.FromNupkgNoChecksum(plain);
        Assert.False(noTrailer.HasChunkIndex);
        Assert.Null(noTrailer.ChunkIndexOffset);
        Assert.Null(noTrailer.ChunkIndexLength);
        Assert.Null(noTrailer.ChunkIndexSHA256);
        Assert.DoesNotContain("ChunkIndex", CompiledJson.SerializeVelopackAsset(noTrailer));
    }
}
