namespace Velopack.Packaging.Tests;

public class ReleaseEntryHelperTests
{
    private static VelopackAsset Asset(string version, VelopackAssetType type, string fileName) => new() {
        PackageId = "MyApp",
        Version = SemanticVersion.Parse(version),
        Type = type,
        FileName = fileName,
        SHA1 = "sha1",
        SHA256 = "sha256",
        Size = 1,
    };

    [Fact]
    public void MergeAssetsKeepsLegacyDeltaEntriesFromRemoteFeed()
    {
        // feeds in the wild contain legacy delta assets; vpk no longer creates them but must not drop them
        var remote = new[] {
            Asset("1.0.0", VelopackAssetType.Full, "MyApp-1.0.0-full.nupkg"),
            Asset("2.0.0", VelopackAssetType.Full, "MyApp-2.0.0-full.nupkg"),
            Asset("2.0.0", VelopackAssetType.Delta, "MyApp-2.0.0-delta.nupkg"),
        };
        var local = new[] {
            Asset("3.0.0", VelopackAssetType.Full, "MyApp-3.0.0-full.nupkg"),
        };

        var merged = ReleaseEntryHelper.MergeAssets(local, remote).ToArray();

        Assert.Equal(4, merged.Length);
        var delta = Assert.Single(merged, a => a.Type == VelopackAssetType.Delta);
        Assert.Equal("MyApp-2.0.0-delta.nupkg", delta.FileName);
        Assert.Contains(merged, a => a.FileName == "MyApp-3.0.0-full.nupkg");
    }

    [Fact]
    public void LegacyMigrationFeedIgnoresLegacyDeltaEntries()
    {
        var feed = new VelopackAssetFeed {
            Assets = [
                Asset("1.0.0", VelopackAssetType.Full, "MyApp-1.0.0-full.nupkg"),
                Asset("2.0.0", VelopackAssetType.Delta, "MyApp-2.0.0-delta.nupkg"),
            ],
        };

        var releases = ReleaseEntryHelper.GetLegacyMigrationReleaseFeedString(feed);

        Assert.Contains("MyApp-1.0.0-full.nupkg", releases);
        Assert.DoesNotContain("delta", releases);
    }
}
