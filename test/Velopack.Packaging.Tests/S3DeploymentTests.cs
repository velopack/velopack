using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NuGet.Versioning;
using Velopack.Deployment;
using Velopack.Packaging.Exceptions;
using Velopack.Sources;

namespace Velopack.Packaging.Tests
{
    public class S3DeploymentTests
    {
        public readonly static string B2_KEYID = "0035016844a4188000000000a";
        public readonly static string B2_SECRET = Environment.GetEnvironmentVariable("VELOPACK_B2_TEST_TOKEN") ?? "K003jlDxnA1m3HAvNsyqzHIUmRuSdbE";
        public readonly static string B2_BUCKET = "velopack-testing";
        public readonly static string B2_ENDPOINT = "s3.eu-central-003.backblazeb2.com";

        private readonly ITestOutputHelper _output;

        public S3DeploymentTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [SkippableFact]
        public void CanDeployToBackBlazeB2()
        {
            Skip.If(String.IsNullOrWhiteSpace(B2_SECRET), "VELOPACK_B2_TEST_TOKEN is not set.");
            using var logger = _output.BuildLoggerFor<S3DeploymentTests>();
            using var _1 = Utility.GetTempDirectory(out var releaseDir);

            string channel = String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CI"))
                ? VelopackRuntimeInfo.SystemOs.GetOsShortName()
                : "ci-" + VelopackRuntimeInfo.SystemOs.GetOsShortName();

            // get latest version, and increment patch by one
            var updateUrl = $"https://{B2_BUCKET}.{B2_ENDPOINT}/";
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
            var repo = new S3Repository(logger);
            var options = new S3UploadOptions {
                ReleaseDir = new DirectoryInfo(releaseDir),
                Bucket = B2_BUCKET,
                Channel = channel,
                Endpoint = "https://" + B2_ENDPOINT,
                KeyId = B2_KEYID,
                Secret = B2_SECRET,
                KeepMaxReleases = 4,
            };

            // download latest version and create delta
            repo.DownloadLatestFullPackageAsync(options).GetAwaiterResult();
            var id = "B2TestApp";
            TestApp.PackTestApp(id, newVer.ToFullString(), $"b2-{DateTime.UtcNow.ToLongDateString()}", releaseDir, logger, channel: channel);
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
}
