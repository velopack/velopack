using Neovolve.Logging.Xunit;
using Velopack.Core;
using Velopack.Locators;
using Velopack.Sources;
using Velopack.Sources.Tests.Infrastructure;

namespace Velopack.Sources.Tests;

[Collection("SourceTests")]
public class HttpSourceTests(DockerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void CSharp_CheckForUpdates()
    {
        Assert.SkipUnless(fixture.IsDockerAvailable, "Docker containers not running. Run 'docker compose up -d' first.");

        var source = new SimpleWebSource(fixture.HttpBaseUrl);
        var localPackage = new VelopackAsset {
            PackageId = TestFeedData.PackageId,
            Version = SemanticVersion.Parse(TestFeedData.CurrentVersion),
            Type = VelopackAssetType.Full,
            FileName = TestFeedData.FullFileName(TestFeedData.CurrentVersion),
        };
        var locator = new TestVelopackLocator(
            TestFeedData.PackageId, TestFeedData.CurrentVersion, fixture.PackagesDir,
            null, null, null, TestFeedData.Channel, localPackage: localPackage);
        var options = new UpdateOptions { ExplicitChannel = TestFeedData.Channel };
        var um = new UpdateManager(source, options, locator);

        var info = um.CheckForUpdates();

        Assert.NotNull(info);
        AssertTarget(info);
        AssertDeltas(info);
    }

    [Fact]
    public void Rust_CheckForUpdates()
    {
        Assert.SkipUnless(fixture.IsDockerAvailable, "Docker containers not running. Run 'docker compose up -d' first.");
        Assert.SkipUnless(!string.IsNullOrEmpty(fixture.RustHarnessPath), "Rust harness not built.");

        var result = HarnessRunner.RunRust(fixture, "http", fixture.HttpBaseUrl, null, output);

        AssertHarnessTarget(result);
        AssertHarnessDeltas(result);
    }

    [Fact]
    public void Cpp_CheckForUpdates()
    {
        Assert.SkipUnless(fixture.IsDockerAvailable, "Docker containers not running. Run 'docker compose up -d' first.");
        Assert.SkipUnless(!string.IsNullOrEmpty(fixture.CppHarnessPath), "C++ harness not built.");

        var result = HarnessRunner.RunCpp(fixture, "http", fixture.HttpBaseUrl, null, output);

        AssertHarnessTarget(result);
        AssertHarnessDeltas(result);
        AssertHarnessNotesHtml(result);
    }

    [Fact]
    public void Python_CheckForUpdates()
    {
        Assert.SkipUnless(fixture.IsDockerAvailable, "Docker containers not running. Run 'docker compose up -d' first.");
        Assert.SkipUnless(fixture.IsPythonAvailable, "Python velopack module not available.");

        var result = HarnessRunner.RunPython(fixture, "http", fixture.HttpBaseUrl, null, output);

        AssertHarnessTarget(result);
        AssertHarnessDeltas(result);
    }

    [Fact]
    public async Task CSharp_GetReleaseFeed()
    {
        Assert.SkipUnless(fixture.IsDockerAvailable, "Docker containers not running. Run 'docker compose up -d' first.");

        using var logger = output.BuildLoggerFor<HttpSourceTests>();
        var source = new SimpleWebSource(fixture.HttpBaseUrl);

        var feed = await source.GetReleaseFeed(logger.ToVelopackLogger(), TestFeedData.PackageId, TestFeedData.Channel);

        AssertFeed(feed);
    }

    [Fact]
    public void Rust_GetReleaseFeed()
    {
        Assert.SkipUnless(fixture.IsDockerAvailable, "Docker containers not running. Run 'docker compose up -d' first.");
        Assert.SkipUnless(!string.IsNullOrEmpty(fixture.RustHarnessPath), "Rust harness not built.");

        var result = HarnessRunner.RunRust(fixture, "http", fixture.HttpBaseUrl, null, output);

        AssertHarnessFeed(result.Feed);
    }

    private static void AssertTarget(UpdateInfo info)
    {
        Assert.Equal(TestFeedData.PackageId, info.TargetFullRelease.PackageId);
        Assert.Equal(TestFeedData.LatestVersion, info.TargetFullRelease.Version.ToString());
        Assert.Equal(TestFeedData.FullFileName(TestFeedData.LatestVersion), info.TargetFullRelease.FileName);
        Assert.False(info.IsDowngrade);
    }

    private static void AssertDeltas(UpdateInfo info)
    {
        Assert.Equal(3, info.DeltasToTarget.Length);
        foreach (var version in TestFeedData.AllVersions) {
            Assert.Contains(info.DeltasToTarget, d =>
                d.Version.ToString() == version && d.Type == VelopackAssetType.Delta);
        }
    }

    private static void AssertFeed(VelopackAssetFeed feed)
    {
        Assert.NotNull(feed);
        Assert.Equal(6, feed.Assets.Length);
        foreach (var version in TestFeedData.AllVersions) {
            Assert.Contains(feed.Assets, a =>
                a.Version.ToString() == version && a.Type == VelopackAssetType.Full);
            Assert.Contains(feed.Assets, a =>
                a.Version.ToString() == version && a.Type == VelopackAssetType.Delta);
        }
    }

    private static void AssertHarnessTarget(HarnessResult result)
    {
        Assert.NotNull(result.Target);
        Assert.Equal(TestFeedData.PackageId, result.Target.PackageId);
        Assert.Equal(TestFeedData.LatestVersion, result.Target.Version);
        Assert.Equal(TestFeedData.FullFileName(TestFeedData.LatestVersion), result.Target.FileName);
        Assert.Equal("Full", result.Target.Type);
        Assert.False(result.IsDowngrade);
    }

    private static void AssertHarnessDeltas(HarnessResult result)
    {
        Assert.NotNull(result.Deltas);
        Assert.Equal(3, result.Deltas.Length);
        foreach (var version in TestFeedData.AllVersions) {
            Assert.Contains(result.Deltas, d => d.Version == version && d.Type == "Delta");
        }
    }

    private static void AssertHarnessNotesHtml(HarnessResult result)
    {
        Assert.NotNull(result.Target);
        Assert.NotNull(result.Target.NotesHtml);
        Assert.NotEmpty(result.Target.NotesHtml);
        Assert.Contains("<h2>", result.Target.NotesHtml);
    }

    private static void AssertHarnessFeed(HarnessAsset[]? feed)
    {
        Assert.NotNull(feed);
        Assert.Equal(6, feed.Length);
        foreach (var version in TestFeedData.AllVersions) {
            Assert.Contains(feed, a => a.Version == version && a.Type == "Full");
            Assert.Contains(feed, a => a.Version == version && a.Type == "Delta");
        }
    }
}
