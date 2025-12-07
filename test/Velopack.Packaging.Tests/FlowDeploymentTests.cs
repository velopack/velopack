using Velopack.Core;
using Velopack.Deployment;
using Velopack.Util;
#nullable enable
namespace Velopack.Packaging.Tests;

public class FlowDeploymentTests
{
    private static string CHANNEL = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CI"))
        ? VelopackRuntimeInfo.SystemOs.GetOsShortName()
        : "ci-" + VelopackRuntimeInfo.SystemOs.GetOsShortName();

    // This token should be set to a valid API key for the test package
    private static readonly string? FLOW_PACKAGE_ID = Environment.GetEnvironmentVariable("VELOPACK_FLOW_TEST_PACKAGE_ID");

    private readonly ITestOutputHelper _output;

    public FlowDeploymentTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public async Task CanDownloadFromFlow()
    {
        Skip.If(string.IsNullOrWhiteSpace(FLOW_PACKAGE_ID), "VELOPACK_FLOW_TEST_PACKAGE_ID is not set.");
        using var logger = _output.BuildLoggerFor<FlowDeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);

        var repo = new FlowRepository(logger);
        var options = new FlowDownloadOptions {
            ReleaseDir = new DirectoryInfo(releaseDir),
            Channel = CHANNEL,
            PackageId = FLOW_PACKAGE_ID,
            Timeout = 30,
        };

        // download latest version
        await repo.DownloadLatestFullPackageAsync(options);

        // verify that a file was downloaded
        var downloadedFiles = Directory.GetFiles(releaseDir, "*.nupkg");
        Assert.True(downloadedFiles.Length > 0, "No package was downloaded.");

        logger.Info($"Downloaded {downloadedFiles.Length} file(s):");
        foreach (var file in downloadedFiles) {
            logger.Info($"  - {Path.GetFileName(file)}");
        }
    }

    [Fact]
    public void FlowRepositoryCreatesValidSource()
    {
        var options = new FlowDownloadOptions {
            ReleaseDir = new DirectoryInfo("."),
            Channel = "stable",
            PackageId = "test-package",
            VelopackBaseUrl = "https://api.velopack.io/",
        };

        var source = FlowRepository.CreateSource(options);

        Assert.NotNull(source);
        Assert.Equal("https://api.velopack.io/", source.BaseUri.ToString());
    }

    [Fact]
    public void FlowRepositoryUsesDefaultBaseUrl()
    {
        var options = new FlowDownloadOptions {
            ReleaseDir = new DirectoryInfo("."),
            Channel = "stable",
            PackageId = "test-package",
            // VelopackBaseUrl not set - should use default
        };

        var source = FlowRepository.CreateSource(options);

        Assert.NotNull(source);
        Assert.Equal("https://api.velopack.io/", source.BaseUri.ToString());
    }
}
