using System.IO.Compression;
using System.Text;
using Velopack.Chunks;
using Velopack.Packaging.Chunking;
using Velopack.Util;

namespace Velopack.Packaging.Tests.Chunking;

/// <summary>
/// Produces the packed golden fixtures <c>test/fixtures/delta-v2/ChunkTestApp-{1.0.0,1.1.0}-win-full.nupkg</c> and
/// <c>releases.win.json</c> with the chunk-aligned writer, from a synthetic tree (a modified large file, a renamed file, a
/// deleted file, an added file, a stub, a Squirrel.exe placeholder; no empty directories, no symlinks). Compressed
/// bytes depend on the runtime's zlib, so <see cref="ChunkTestAppFixturesMatchGenerator"/> checks the runtime-independent
/// parts (Section A, the file contents, the feed pointers) and <see cref="RegenerateChunkTestAppFixtures"/> (explicit)
/// rewrites the files.
/// </summary>
public class DeltaV2PackageFixtures
{
    public const string Id = "ChunkTestApp";
    public static readonly string[] Versions = { "1.0.0", "1.1.0" };

    public static string FixtureDir => PathHelper.GetFixture("delta-v2");

    public static string NupkgName(string version) => $"{Id}-{version}-win-full.nupkg";

    /// <summary>Writes the synthetic pack directory of one version and returns its nuspec.</summary>
    public static string CreateTree(string packDir, string version)
    {
        var nuspec = ChunkTestTree.Nuspec(Id, version, mainExe: Id + ".exe");
        ChunkTestTree.WriteFile(packDir, Id + ".exe", ChunkTestTree.CompressibleBytes(100, 200_000));
        ChunkTestTree.WriteFile(packDir, "Squirrel.exe", ChunkTestTree.RandomBytes(101, 120_000)); // updater placeholder
        ChunkTestTree.WriteFile(packDir, Id + "_ExecutionStub.exe", ChunkTestTree.CompressibleBytes(102, 30_000));
        ChunkTestTree.WriteFile(packDir, "sq.version", Encoding.UTF8.GetBytes(nuspec));
        ChunkTestTree.WriteFile(packDir, "assets/random.bin", ChunkTestTree.RandomBytes(108, 100_000)); // incompressible, stored
        ChunkTestTree.WriteFile(packDir, "empty.txt", Array.Empty<byte>());

        var large = ChunkTestTree.CompressibleBytes(103, 1_500_000);
        if (version == "1.1.0") {
            // the modified large file: 1 KiB inserted in the middle, so most chunks are shared with 1.0.0
            var insert = ChunkTestTree.CompressibleBytes(104, 1024);
            var modified = new byte[large.Length + insert.Length];
            Buffer.BlockCopy(large, 0, modified, 0, 500_000);
            Buffer.BlockCopy(insert, 0, modified, 500_000, insert.Length);
            Buffer.BlockCopy(large, 500_000, modified, 500_000 + insert.Length, large.Length - 500_000);
            large = modified;
        }

        ChunkTestTree.WriteFile(packDir, "data/large.bin", large);

        var renamed = ChunkTestTree.CompressibleBytes(105, 50_000);
        if (version == "1.0.0") {
            ChunkTestTree.WriteFile(packDir, "data/renamed-v1.txt", renamed);
            ChunkTestTree.WriteFile(packDir, "data/deleted.txt", ChunkTestTree.CompressibleBytes(106, 40_000));
        } else {
            ChunkTestTree.WriteFile(packDir, "data/renamed-v2.txt", renamed);
            ChunkTestTree.WriteFile(packDir, "data/added.txt", ChunkTestTree.CompressibleBytes(107, 45_000));
        }

        return nuspec;
    }

    /// <summary>Packs both versions into <paramref name="outputDir"/> and writes releases.win.json there.</summary>
    public static async Task GenerateAsync(string outputDir, string workDir)
    {
        Directory.CreateDirectory(outputDir);
        var assets = new List<VelopackAsset>();
        foreach (var version in Versions) {
            var packDir = Path.Combine(workDir, "pack-" + version);
            var nuspec = CreateTree(packDir, version);
            var packed = ChunkTestTree.Pack(workDir, packDir, nuspec, Id);
            var target = Path.Combine(outputDir, NupkgName(version));
            File.Copy(packed.NupkgPath, target, true);
            assets.Add(await VelopackAsset.FromNupkgGenerateChecksumAsync(target));
        }

        var feed = new VelopackAssetFeed { Assets = assets.OrderByDescending(a => a.Version).ToArray() };
        File.WriteAllText(Path.Combine(outputDir, "releases.win.json"), ReleaseEntryHelper.GetAssetFeedJson(feed).Replace("\r\n", "\n") + "\n");
    }

    [Fact(Explicit = true)]
    public async Task RegenerateChunkTestAppFixtures()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        await GenerateAsync(FixtureDir, workDir);
    }

    [Fact]
    public async Task ChunkTestAppFixturesMatchGenerator()
    {
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var feed = VelopackAssetFeed.FromJson(File.ReadAllText(Path.Combine(FixtureDir, "releases.win.json")));
        Assert.Equal(Versions.Length, feed.Assets.Length);

        foreach (var version in Versions) {
            var nupkg = Path.Combine(FixtureDir, NupkgName(version));
            Assert.True(File.Exists(nupkg), $"fixture missing: {nupkg} (run RegenerateChunkTestAppFixtures)");

            // the content index of the fixture equals the one the generator's tree produces today (runtime independent)
            var packDir = Path.Combine(workDir, "pack-" + version);
            var nuspec = CreateTree(packDir, version);
            var index = ChunkIndexBuilder.Build(packDir);
            var asset = VelopackAsset.FromNupkgNoChecksum(nupkg);
            Assert.True(asset.HasChunkIndex);
            var zipBytes = File.ReadAllBytes(nupkg);
            var trailer = zipBytes.AsSpan((int) asset.ChunkIndexOffset!.Value, (int) asset.ChunkIndexLength!.Value).ToArray();
            var parsed = ChunkIndex.ParseTrailer(trailer, zipBytes.Length);
            Assert.True(
                parsed.CopySectionA().AsSpan().SequenceEqual(index.SerializeSectionA()),
                $"{NupkgName(version)} is out of date (run RegenerateChunkTestAppFixtures)");

            using var zip = ZipFile.OpenRead(nupkg);
            Assert.Equal(Encoding.UTF8.GetBytes(nuspec), ReadEntry(zip, Id + ".nuspec"));
            Assert.Equal(index.SerializeSectionA(), ReadEntry(zip, "lib/app/sq.chunks"));
            foreach (var file in index.Files) {
                Assert.Equal(File.ReadAllBytes(Path.Combine(packDir, file.Path)), ReadEntry(zip, "lib/app/" + file.Path));
            }

            Assert.Equal(ChunkIndex.TrailerEntryName, zip.Entries[^1].FullName);

            // the feed carries the pointer and checksums of exactly this file
            var feedAsset = feed.Assets.Single(a => a.Version.ToString() == version);
            Assert.Equal(NupkgName(version), feedAsset.FileName);
            Assert.Equal(asset.ChunkIndexOffset, feedAsset.ChunkIndexOffset);
            Assert.Equal(asset.ChunkIndexLength, feedAsset.ChunkIndexLength);
            Assert.Equal(asset.ChunkIndexSHA256, feedAsset.ChunkIndexSHA256);
            Assert.Equal(zipBytes.Length, feedAsset.Size);
            var (sha1, sha256) = await IoUtil.CalculateFileSHA1AndSHA256Async(nupkg);
            Assert.Equal(sha256, feedAsset.SHA256, ignoreCase: true);
            Assert.Equal(sha1, feedAsset.SHA1, ignoreCase: true);
        }

        // the two versions share most chunks of the large file: that is what makes the pair a useful delta fixture
        var v1 = ChunkIndex.ParseLocal(ChunkIndexBuilder.Build(Path.Combine(workDir, "pack-1.0.0")).SerializeSectionA());
        var v2 = ChunkIndex.ParseLocal(ChunkIndexBuilder.Build(Path.Combine(workDir, "pack-1.1.0")).SerializeSectionA());
        var v1Hashes = new HashSet<Hash256>();
        for (int c = 0; c < v1.ChunkCount; c++) v1Hashes.Add(v1.GetChunkHash(c));
        int shared = 0;
        for (int c = 0; c < v2.ChunkCount; c++) {
            if (v1Hashes.Contains(v2.GetChunkHash(c))) shared++;
        }

        Assert.True(shared >= v2.ChunkCount - 6, $"only {shared} of {v2.ChunkCount} chunks are shared between the fixture versions");
        Assert.True(v2.FindFile("data/added.txt") >= 0 && v2.FindFile("data/deleted.txt") < 0);
        Assert.True(v1.FindFile("data/renamed-v1.txt") >= 0 && v2.FindFile("data/renamed-v2.txt") >= 0);
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
}
