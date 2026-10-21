#nullable enable
using Velopack.Chunks;
using Velopack.Locators;
using Velopack.Util;

namespace Velopack.Tests.Chunks;

public class StagedUpdateTests
{
    private static readonly string IndexSha = new string('A', 64);

    private static TestVelopackLocator Locator(string packagesDir) => new("App", "1.0.0", packagesDir);

    private static StagedReadyMarker Marker(string version, string indexSha, string? stagedId = null)
    {
        return new StagedReadyMarker {
            Format = StagedReadyMarker.CurrentFormat,
            StagedId = stagedId ?? ChunkIndex.StagedIdOf(indexSha),
            IndexSHA256 = indexSha,
            UpdaterSHA256 = new string('B', 64),
            Asset = new VelopackAsset {
                PackageId = "App",
                Version = SemanticVersion.Parse(version),
                Type = VelopackAssetType.Full,
                FileName = $"App-{version}-full.nupkg",
                Size = 10,
                ChunkIndexOffset = 1,
                ChunkIndexLength = 2,
                ChunkIndexSHA256 = indexSha,
            },
            CreatedUtc = "2026-10-17T12:00:00Z",
        };
    }

    internal static string WriteReady(string packagesDir, string version, string indexSha, string? dirName = null, string? stagedId = null)
    {
        var id = dirName ?? ChunkIndex.StagedIdOf(indexSha);
        var staged = new StagedUpdate(Path.Combine(packagesDir, "staged", id), id);
        staged.WriteReadyMarker(Marker(version, indexSha, stagedId));
        return staged.Dir;
    }

    [Fact]
    public void FindReadyStagedValidatesMarker()
    {
        using var _ = TempUtil.GetTempDirectory(out var packages);
        var locator = Locator(packages);
        Assert.Null(StagedUpdate.FindReadyStaged(locator));

        var shaA = new string('A', 64);
        var shaB = new string('B', 64);
        var shaC = new string('C', 64);
        var shaD = new string('D', 64);
        var good = WriteReady(packages, "1.1.0", shaA);
        WriteReady(packages, "1.2.0", shaB, dirName: "wrong-dir-name");          // StagedId != directory name
        // id != StagedIdOf(IndexSHA256)
        WriteReady(packages, "1.3.0", shaC, stagedId: ChunkIndex.StagedIdOf(shaD), dirName: ChunkIndex.StagedIdOf(shaD));
        Directory.CreateDirectory(Path.Combine(packages, "staged", "incomplete")); // no ready.json
        File.WriteAllText(Path.Combine(packages, "staged", "incomplete", "ready.json.tmp"), "{}");

        var ready = StagedUpdate.FindReadyStaged(locator);
        Assert.NotNull(ready);
        Assert.Equal(good, ready!.Dir);
        Assert.Equal(SemanticVersion.Parse("1.1.0"), ready.Asset.Version);

        var asset = new VelopackAsset { ChunkIndexLength = 2, ChunkIndexSHA256 = shaA.ToLowerInvariant() };
        Assert.NotNull(StagedUpdate.FindReadyStagedFor(locator, asset));
        Assert.Null(StagedUpdate.FindReadyStagedFor(locator, asset with { ChunkIndexSHA256 = shaB }));
        Assert.Null(StagedUpdate.FindReadyStagedFor(locator, asset with { ChunkIndexSHA256 = null }));

        // a marker whose format is unknown is ignored
        var marker = Marker("1.1.0", shaA);
        marker.Format = 2;
        new StagedUpdate(good, Path.GetFileName(good)).WriteReadyMarker(marker);
        Assert.Null(StagedUpdate.FindReadyStaged(locator));
    }

    [Fact]
    public void FindReadyStagedPicksHighestVersion()
    {
        using var _ = TempUtil.GetTempDirectory(out var packages);
        var locator = Locator(packages);
        WriteReady(packages, "1.1.0", new string('A', 64));
        var highest = WriteReady(packages, "2.0.0", new string('B', 64));
        WriteReady(packages, "1.5.0", new string('C', 64));
        Assert.Equal(highest, StagedUpdate.FindReadyStaged(locator)!.Dir);
    }

    [Fact]
    public void CleanupOnStartupKeepsHighestReadyAndFreshIncomplete()
    {
        using var _ = TempUtil.GetTempDirectory(out var packages);
        var locator = Locator(packages); // installed 1.0.0
        var above = WriteReady(packages, "1.1.0", new string('A', 64));
        var highest = WriteReady(packages, "2.0.0", new string('B', 64));
        var downgrade = WriteReady(packages, "0.9.0", new string('C', 64));
        var installed = WriteReady(packages, "1.0.0", new string('D', 64));

        var stale = Path.Combine(packages, "staged", "stale");
        Directory.CreateDirectory(stale);
        File.WriteAllBytes(Path.Combine(stale, "index.bin"), new byte[10]);
        File.WriteAllBytes(Path.Combine(stale, "chunks.bin"), new byte[10]);
        var old = DateTime.UtcNow - TimeSpan.FromDays(8);
        File.SetLastWriteTimeUtc(Path.Combine(stale, "index.bin"), old);
        File.SetLastWriteTimeUtc(Path.Combine(stale, "chunks.bin"), old);

        var fresh = Path.Combine(packages, "staged", "fresh");
        Directory.CreateDirectory(fresh);
        File.WriteAllBytes(Path.Combine(fresh, "index.bin"), new byte[10]);
        File.SetLastWriteTimeUtc(Path.Combine(fresh, "index.bin"), old);
        File.WriteAllBytes(Path.Combine(fresh, "chunks.bin"), new byte[10]); // written now: the newest artifact counts

        StagedUpdate.CleanupOnStartup(locator);

        Assert.True(Directory.Exists(highest));
        // §6.6: only ready trees at or below the installed version are deleted (Rust gc_staged_for_run agrees)
        Assert.True(Directory.Exists(above));
        Assert.False(Directory.Exists(downgrade));
        Assert.False(Directory.Exists(installed));
        Assert.False(Directory.Exists(stale));
        Assert.True(Directory.Exists(fresh));
    }

    [Fact]
    public void CleanupOnStartupKeepsHighestReadyEvenAsDowngrade()
    {
        using var _ = TempUtil.GetTempDirectory(out var packages);
        var locator = Locator(packages); // installed 1.0.0
        var highest = WriteReady(packages, "0.9.0", new string('A', 64));
        var lower = WriteReady(packages, "0.8.0", new string('B', 64));

        StagedUpdate.CleanupOnStartup(locator);

        // parity with GetLatestLocalFullPackage, which keeps a staged downgrade
        Assert.True(Directory.Exists(highest));
        Assert.False(Directory.Exists(lower));
    }

    [Fact]
    public void DeleteStagedExceptKeepsOnlyTheGivenId()
    {
        using var _ = TempUtil.GetTempDirectory(out var packages);
        var locator = Locator(packages);
        var keep = WriteReady(packages, "1.1.0", new string('A', 64));
        var other = WriteReady(packages, "1.2.0", new string('B', 64));
        StagedUpdate.DeleteStagedExcept(locator, Path.GetFileName(keep));
        Assert.True(Directory.Exists(keep));
        Assert.False(Directory.Exists(other));
        StagedUpdate.DeleteStagedExcept(locator, null);
        Assert.False(Directory.Exists(keep));
    }

    [Fact]
    public void MissingHashesRoundTrip()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var staged = new StagedUpdate(dir, "id");
        Assert.Empty(staged.ReadMissingHashes());
        var h1 = Hash256.Parse(new string('1', 64));
        var h2 = Hash256.Parse(new string('2', 64));
        var bytes = new byte[64 + 5]; // a trailing partial record is ignored
        h1.CopyTo(bytes, 0);
        h2.CopyTo(bytes, 32);
        File.WriteAllBytes(staged.MissingPath, bytes);
        Assert.Equal(new[] { h1, h2 }, staged.ReadMissingHashes());
    }

    [Fact]
    public void LocatorStagedPaths()
    {
        using var _ = TempUtil.GetTempDirectory(out var packages);
        var locator = Locator(packages);
        Assert.Equal(Path.Combine(packages, "staged"), locator.GetStagedRootDir());
        Assert.Equal(Path.Combine(packages, "staged", "abc"), locator.GetStagedDir("abc"));
    }
}
