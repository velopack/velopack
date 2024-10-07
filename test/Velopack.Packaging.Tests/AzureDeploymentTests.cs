using NuGet.Versioning;
using Velopack.Deployment;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

public class AzureDeploymentTests
{
    public readonly static string AZ_ACCOUNT = "velopacktesting";
    public readonly static string AZ_KEY = Environment.GetEnvironmentVariable("VELOPACK_AZ_TEST_TOKEN");
    public readonly static string AZ_CONTAINER = "ci-deployment";
    public readonly static string AZ_ENDPOINT = "velopacktesting.blob.core.windows.net";

    private readonly ITestOutputHelper _output;

    public AzureDeploymentTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public void CanDeployToAzure()
    {
        Skip.If(String.IsNullOrWhiteSpace(AZ_KEY), "VELOPACK_AZ_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<AzureDeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);

        string channel = String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CI"))
            ? VelopackRuntimeInfo.SystemOs.GetOsShortName()
            : "ci-" + VelopackRuntimeInfo.SystemOs.GetOsShortName();

        // get latest version, and increment patch by one
        var updateUrl = $"https://{AZ_ENDPOINT}/{AZ_CONTAINER}";
        var source = new SimpleWebSource(updateUrl);
        VelopackAssetFeed feed = new VelopackAssetFeed();
        try {
            feed = source.GetReleaseFeed(logger, channel).GetAwaiterResult();
        } catch (Exception ex) {
            logger.Warn(ex, "Failed to fetch release feed.");
        }
        var latest = feed.Assets.Where(a => a.Version != null && a.Type == VelopackAssetType.Full)
            .OrderByDescending(a => a.Version)
            .FirstOrDefault();
        var newVer = latest != null ? new SemanticVersion(1, 0, latest.Version.Patch + 1) : new SemanticVersion(1, 0, 0);

        // create repo
        var repo = new AzureRepository(logger);
        var options = new AzureUploadOptions {
            ReleaseDir = new DirectoryInfo(releaseDir),
            Container = AZ_CONTAINER,
            Channel = channel,
            Account = AZ_ACCOUNT,
            Key = AZ_KEY,
            KeepMaxReleases = 4,
        };

        // download latest version and create delta
        repo.DownloadLatestFullPackageAsync(options).GetAwaiterResult();

        if (options.ReleaseDir.EnumerateFiles("*.incomplete").Any()) {
            logger.Warn("A previous package was not downloaded, probably due to invalid checksum. This is a race condition in this test.");
            latest = null;
        }
        
        var id = "AZTestApp";
        TestApp.PackTestApp(id, newVer.ToFullString(), $"az-{DateTime.UtcNow.ToLongDateString()}", releaseDir, logger, channel: channel);
        if (latest != null) {
            // check delta was created
            Assert.True(Directory.EnumerateFiles(releaseDir, "*-delta.nupkg").Any(), "No delta package was created.");
        }

        // upload new files
        repo.UploadMissingAssetsAsync(options).GetAwaiterResult();

        // verify that new version has been uploaded
        feed = source.GetReleaseFeed(logger, channel).GetAwaiterResult();
        latest = feed.Assets.Where(a => a.Version != null && a.Type == VelopackAssetType.Full)
            .OrderByDescending(a => a.Version)
            .FirstOrDefault();

        Assert.True(latest != null, "No latest version found.");
        Assert.Equal(newVer, latest.Version);
        Assert.True(feed.Assets.Count(x => x.Type == VelopackAssetType.Full) <= options.KeepMaxReleases, "Too many releases were kept.");
    }
}
