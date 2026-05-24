using Neovolve.Logging.Xunit;
using Velopack.Core;
using Velopack.Locators;
using Velopack.Sources;
using Velopack.Sources.Tests.Infrastructure;

namespace Velopack.Sources.Tests;

[Collection("SourceTests")]
public class GitLabSourceTests(DockerFixture fixture, ITestOutputHelper output)
{
    [Fact]
    public void CSharp_CheckForUpdates()
    {
        Assert.SkipUnless(fixture.IsGitLabAvailable, "GitLab is not available.");

        var source = new GitlabSource(fixture.GitLabApiUrl, fixture.GitLabToken, upcomingRelease: false);
        var locator = new TestVelopackLocator(
            TestFeedData.PackageId, TestFeedData.CurrentVersion, fixture.PackagesDir,
            null, null, null, TestFeedData.Channel);
        var options = new UpdateOptions { ExplicitChannel = TestFeedData.Channel };
        var um = new UpdateManager(source, options, locator);

        var info = um.CheckForUpdates();

        Assert.NotNull(info);
        Assert.Equal(TestFeedData.PackageId, info.TargetFullRelease.PackageId);
        Assert.Equal(TestFeedData.UpdateVersion, info.TargetFullRelease.Version.ToString());
        Assert.Equal(TestFeedData.FileName, info.TargetFullRelease.FileName);
    }

    [Fact]
    public void Rust_CheckForUpdates()
    {
        Assert.SkipUnless(fixture.IsGitLabAvailable, "GitLab is not available.");
        Assert.SkipUnless(!string.IsNullOrEmpty(fixture.RustHarnessPath), "Rust harness not built.");

        var result = HarnessRunner.RunRust(fixture, "gitlab", fixture.GitLabApiUrl, fixture.GitLabToken, output);

        AssertTarget(result.Target);
    }

    [Fact]
    public void Cpp_CheckForUpdates()
    {
        Assert.SkipUnless(fixture.IsGitLabAvailable, "GitLab is not available.");
        Assert.SkipUnless(!string.IsNullOrEmpty(fixture.CppHarnessPath), "C++ harness not built.");

        var result = HarnessRunner.RunCpp(fixture, "gitlab", fixture.GitLabApiUrl, fixture.GitLabToken, output);

        AssertTarget(result.Target);
    }

    [Fact]
    public void Python_CheckForUpdates()
    {
        Assert.SkipUnless(fixture.IsGitLabAvailable, "GitLab is not available.");
        Assert.SkipUnless(fixture.IsPythonAvailable, "Python velopack module not available.");

        var result = HarnessRunner.RunPython(fixture, "gitlab", fixture.GitLabApiUrl, fixture.GitLabToken, output);

        AssertTarget(result.Target);
    }

    [Fact]
    public async Task CSharp_GetReleaseFeed()
    {
        Assert.SkipUnless(fixture.IsGitLabAvailable, "GitLab is not available.");

        using var logger = output.BuildLoggerFor<GitLabSourceTests>();
        var source = new GitlabSource(fixture.GitLabApiUrl, fixture.GitLabToken, upcomingRelease: false);

        var feed = await source.GetReleaseFeed(logger.ToVelopackLogger(), TestFeedData.PackageId, TestFeedData.Channel);

        Assert.NotNull(feed);
        Assert.Single(feed.Assets);
        Assert.Equal(TestFeedData.PackageId, feed.Assets[0].PackageId);
        Assert.Equal(TestFeedData.UpdateVersion, feed.Assets[0].Version.ToString());
    }

    [Fact]
    public void Rust_GetReleaseFeed()
    {
        Assert.SkipUnless(fixture.IsGitLabAvailable, "GitLab is not available.");
        Assert.SkipUnless(!string.IsNullOrEmpty(fixture.RustHarnessPath), "Rust harness not built.");

        var result = HarnessRunner.RunRust(fixture, "gitlab", fixture.GitLabApiUrl, fixture.GitLabToken, output);

        AssertFeed(result.Feed);
    }

    private static void AssertTarget(HarnessAsset? target)
    {
        Assert.NotNull(target);
        Assert.Equal(TestFeedData.PackageId, target.PackageId);
        Assert.Equal(TestFeedData.UpdateVersion, target.Version);
        Assert.Equal(TestFeedData.FileName, target.FileName);
        Assert.Equal("Full", target.Type);
    }

    private static void AssertFeed(HarnessAsset[]? feed)
    {
        Assert.NotNull(feed);
        Assert.Single(feed);
        Assert.Equal(TestFeedData.PackageId, feed[0].PackageId);
        Assert.Equal(TestFeedData.UpdateVersion, feed[0].Version);
        Assert.Equal(TestFeedData.FileName, feed[0].FileName);
    }
}
