using Azure.Storage.Blobs;
using NuGet.Versioning;
using Velopack.Deployment;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

public class DeploymentTests
{
    private static string CHANNEL = String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CI"))
        ? VelopackRuntimeInfo.SystemOs.GetOsShortName()
        : "ci-" + VelopackRuntimeInfo.SystemOs.GetOsShortName();

    private static readonly string B2_KEYID = "0035016844a4188000000000b";
    private static readonly string B2_SECRET = Environment.GetEnvironmentVariable("VELOPACK_B2_TEST_TOKEN");
    private static readonly string B2_BUCKET = "velopack-testing";
    private static readonly string B2_ENDPOINT = "s3.eu-central-003.backblazeb2.com";

    private static readonly string AZ_ACCOUNT = "velopacktesting";
    private static readonly string AZ_KEY = Environment.GetEnvironmentVariable("VELOPACK_AZ_TEST_TOKEN");
    private static readonly string AZ_CONTAINER = "ci-deployment";
    private static readonly string AZ_ENDPOINT = "velopacktesting.blob.core.windows.net";

    private readonly ITestOutputHelper _output;

    public DeploymentTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public async Task CanDeployToBackBlazeB2()
    {
        Skip.If(String.IsNullOrWhiteSpace(B2_SECRET), "VELOPACK_B2_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<DeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);

        var repo = new S3Repository(logger);
        var options = new S3UploadOptions {
            ReleaseDir = new DirectoryInfo(releaseDir),
            Bucket = B2_BUCKET,
            Channel = CHANNEL,
            Endpoint = "https://" + B2_ENDPOINT,
            KeyId = B2_KEYID,
            Secret = B2_SECRET,
            KeepMaxReleases = 4,
        };

        var updateUrl = $"https://{B2_BUCKET}.{B2_ENDPOINT}/";
        await Deploy<S3Repository, S3DownloadOptions, S3UploadOptions, S3BucketClient>("B2TestApp", repo, options, releaseDir, updateUrl, logger);
    }

    [SkippableFact]
    public async Task CanDeployToAzure()
    {
        Skip.If(String.IsNullOrWhiteSpace(AZ_KEY), "VELOPACK_AZ_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<DeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);

        var repo = new AzureRepository(logger);
        var options = new AzureUploadOptions {
            ReleaseDir = new DirectoryInfo(releaseDir),
            Container = AZ_CONTAINER,
            Channel = CHANNEL,
            Account = AZ_ACCOUNT,
            Key = AZ_KEY,
            KeepMaxReleases = 4,
        };

        var updateUrl = $"https://{AZ_ENDPOINT}/{AZ_CONTAINER}";
        await Deploy<AzureRepository, AzureDownloadOptions, AzureUploadOptions, BlobContainerClient>("AZTestApp", repo, options, releaseDir, updateUrl, logger);
    }

    static SemanticVersion GenerateSemverFromDateTime()
    {
        DateTime now = DateTime.Now;
        int major = now.Year; // YYYY
        int minor = now.Month * 100 + now.Day; // MMDD
        int patch = now.Hour * 3600 + now.Minute * 60 + now.Second; // Seconds of the day
        return new SemanticVersion(major, minor, patch);
    }

    private async Task Deploy<TRepo, TDown, TUp, TClient>(string id, TRepo repo, TUp options, string releaseDir, string updateUrl, ILogger logger)
        where TDown : RepositoryOptions, IObjectDownloadOptions
        where TUp : IObjectUploadOptions, TDown
        where TRepo : ObjectRepository<TDown, TUp, TClient>
    {
        var targetVer = GenerateSemverFromDateTime();
        logger.Info($"Target version: {targetVer}");

        // get the latest 
        var source = new SimpleWebSource(updateUrl);
        VelopackAssetFeed feed = new VelopackAssetFeed();
        try {
            feed = await source.GetReleaseFeed(logger, CHANNEL);
        } catch (Exception ex) {
            logger.Warn(ex, "Failed to fetch release feed.");
        }

        var latestOnline = feed.Assets.Where(a => a.Version != null && a.Type == VelopackAssetType.Full).MaxBy(a => a.Version);
        if (latestOnline != null) {
            logger.Info($"Latest online version: {latestOnline.Version}");
            Assert.True(targetVer > latestOnline.Version, "New version is not greater than the latest online version.");
        }

        // download latest version and create delta
        await repo.DownloadLatestFullPackageAsync(options);
        TestApp.PackTestApp(id, targetVer.ToFullString(), $"b2-{DateTime.UtcNow.ToLongDateString()}", releaseDir, logger, channel: CHANNEL);
        if (latestOnline != null) {
            // check delta was created
            Assert.True(Directory.EnumerateFiles(releaseDir, "*-delta.nupkg").Any(), "No delta package was created.");
        }

        // upload new files
        await repo.UploadMissingAssetsAsync(options);

        // verify that new version has been uploaded
        feed = await source.GetReleaseFeed(logger, CHANNEL);
        latestOnline = feed.Assets.Where(a => a.Version != null && a.Type == VelopackAssetType.Full).MaxBy(a => a.Version);

        Assert.True(latestOnline != null, "No latest version found.");
        Assert.Equal(targetVer, latestOnline.Version);
        Assert.True(feed.Assets.Count(x => x.Type == VelopackAssetType.Full) <= options.KeepMaxReleases, "Too many releases were kept.");
    }
}