using Gitea.Net.Api;
using Gitea.Net.Client;
using Gitea.Net.Model;
using Velopack.Deployment;
using Velopack.Packaging.Exceptions;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack.Packaging.Tests;
public class GiteaDeploymentTests
{
    public readonly static string GITEA_TOKEN = Environment.GetEnvironmentVariable("VELOPACK_GITEA_TEST_TOKEN");
    public readonly static string GITEA_REPOURL = "https://gitea.com/remco1271/VelopackGithubUpdateTest.git";

    private readonly ITestOutputHelper _output;

    public GiteaDeploymentTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public void WillRefuseToUploadMultipleWithoutMergeArg()
    {
        Skip.If(String.IsNullOrWhiteSpace(GITEA_TOKEN), "VELOPACK_GITEA_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<GiteaDeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var releaseDir2);
        using var ghvar = GiteaReleaseTest.Create("nomerge", logger);
        var id = "GiteaUpdateTest";
        TestApp.PackTestApp(id, $"0.0.1-{ghvar.UniqueSuffix}", "t1", releaseDir, logger);

        var gh = new GiteaRepository(logger);
        var options = new GiteaUploadOptions {
            ReleaseName = ghvar.ReleaseName,
            ReleaseDir = new DirectoryInfo(releaseDir),
            RepoUrl = GITEA_REPOURL,
            Token = GITEA_TOKEN,
            Prerelease = false,
            Publish = true,
            TargetOs = RuntimeOs.Windows,
        };

        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

        TestApp.PackTestApp(id, $"0.0.2-{ghvar.UniqueSuffix}", "t1", releaseDir2, logger);
        options.ReleaseDir = new DirectoryInfo(releaseDir2);

        Assert.ThrowsAny<UserInfoException>(() => gh.UploadMissingAssetsAsync(options).GetAwaiterResult());
    }

    [SkippableFact]
    public void WillNotMergeMixmatchedTag()
    {
        Skip.If(String.IsNullOrWhiteSpace(GITEA_TOKEN), "VELOPACK_GITEA_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<GiteaDeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var releaseDir2);
        using var ghvar = GiteaReleaseTest.Create("mixmatched", logger);
        var id = "GiteaUpdateTest";
        TestApp.PackTestApp(id, $"0.0.1-{ghvar.UniqueSuffix}", "t1", releaseDir, logger);

        var gh = new GiteaRepository(logger);
        var options = new GiteaUploadOptions {
            ReleaseName = ghvar.ReleaseName,
            ReleaseDir = new DirectoryInfo(releaseDir),
            RepoUrl = GITEA_REPOURL,
            Token = GITEA_TOKEN,
            Prerelease = false,
            Publish = true,
            Merge = true,
            TargetOs = RuntimeOs.Windows,
        };

        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

        TestApp.PackTestApp(id, $"0.0.2-{ghvar.UniqueSuffix}", "t1", releaseDir2, logger);
        options.ReleaseDir = new DirectoryInfo(releaseDir2);

        Assert.ThrowsAny<UserInfoException>(() => gh.UploadMissingAssetsAsync(options).GetAwaiterResult());
    }

    [SkippableFact]
    public void WillMergeGiteaReleases()
    {
        Skip.If(String.IsNullOrWhiteSpace(GITEA_TOKEN), "VELOPACK_GITEA_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<GiteaDeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var releaseDir2);
        using var ghvar = GiteaReleaseTest.Create("yesmerge", logger);
        var id = "GiteaUpdateTest";
        TestApp.PackTestApp(id, $"0.0.1-{ghvar.UniqueSuffix}", "t1", releaseDir, logger);

        var gh = new GiteaRepository(logger);
        var options = new GiteaUploadOptions {
            ReleaseName = ghvar.ReleaseName,
            ReleaseDir = new DirectoryInfo(releaseDir),
            RepoUrl = GITEA_REPOURL,
            Token = GITEA_TOKEN,
            TagName = $"0.0.1-{ghvar.UniqueSuffix}",
            Prerelease = false,
            Publish = true,
            Merge = true,
            TargetOs = RuntimeOs.Windows,
        };

        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

        TestApp.PackTestApp(id, $"0.0.1-{ghvar.UniqueSuffix}", "t1", releaseDir2, logger, channel: "experimental");
        options.ReleaseDir = new DirectoryInfo(releaseDir2);
        options.Channel = "experimental";

        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();
    }

    [SkippableFact]
    public void CanDeployAndUpdateFromGitea()
    {
        Skip.If(String.IsNullOrWhiteSpace(GITEA_TOKEN), "VELOPACK_GITEA_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<GiteaDeploymentTests>();
        var id = "GiteaUpdateTest";
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        var (repoOwner, repoName) = GiteaRepository.GetOwnerAndRepo(GITEA_REPOURL);
        using var ghvar = GiteaReleaseTest.Create("integration", logger);
        var releaseName = ghvar.ReleaseName;
        var uniqueSuffix = ghvar.UniqueSuffix;
        var client = ghvar.Client;

        //Setup Gitea
        Configuration config = new Configuration();
        if (!string.IsNullOrWhiteSpace(GITEA_TOKEN)) {
            config.ApiKey.Add("token", GITEA_TOKEN);
        }
        var uri = new Uri(GITEA_REPOURL);
        var baseUri = uri.GetLeftPart(System.UriPartial.Authority);
        config.BasePath = baseUri + "/api/v1";
        var apiInstance = new RepositoryApi(config);

        // create releases
        var notesPath = Path.Combine(releaseDir, "NOTES");
        var notesContent = $"""
# Release {releaseName}
This is just a _test_!
""";
        File.WriteAllText(notesPath, notesContent);

        if (String.IsNullOrEmpty(GITEA_TOKEN))
            throw new Exception("VELOPACK_GITEA_TEST_TOKEN is not set.");

        var newVer = $"{VelopackRuntimeInfo.VelopackNugetVersion}";
        TestApp.PackTestApp(id, $"0.0.1", "t1", releaseDir, logger, notesPath, channel: uniqueSuffix);
        TestApp.PackTestApp(id, newVer, "t2", releaseDir, logger, notesPath, channel: uniqueSuffix);

        // deploy
        var gh = new GiteaRepository(logger);
        var options = new GiteaUploadOptions {
            ReleaseName = releaseName,
            ReleaseDir = new DirectoryInfo(releaseDir),
            RepoUrl = GITEA_REPOURL,
            Token = GITEA_TOKEN,
            Prerelease = false,
            Publish = true,
            Channel = uniqueSuffix,
        };
        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

        // check
        ApiResponse<Repository> repositoryInfo = apiInstance.RepoGetWithHttpInfoAsync(repoOwner, repoName).GetAwaiterResult();
        var newRelease = apiInstance.RepoListReleasesWithHttpInfoAsync(repoOwner, repoName, page: 1, limit: (int) repositoryInfo.Data.ReleaseCounter).GetAwaiterResult().Data.Single(s => s.Name == releaseName);
        Assert.False(newRelease.Draft);
        Assert.Equal(notesContent.Trim().ReplaceLineEndings("\n"), newRelease.Body.Trim());

        // update
        var source = new GiteaSource(GITEA_REPOURL, GITEA_TOKEN, false);
        var releases = source.GetReleaseFeed(channel: uniqueSuffix, logger: logger).GetAwaiterResult();

        var ghrel = releases.Assets.Select(r => (GiteaSource.GitBaseAsset) r).ToArray();
        foreach (var g in ghrel) {
            logger.Info($"Found asset: ({g.Release.Name}) {g.FileName}");
        }

        var assetsInThisRelease = ghrel.Where(r => r.Release.Name == releaseName).ToArray();

        Assert.Equal(2, assetsInThisRelease.Length);
        foreach (var r in assetsInThisRelease) {
            Assert.Equal(releaseName, r.Release.Name);
            Assert.Equal(id, r.PackageId);
            Assert.Equal(newVer, r.Version.ToNormalizedString());
        }

        using var _2 = TempUtil.GetTempDirectory(out var releaseDirNew);
        gh.DownloadLatestFullPackageAsync(new GiteaDownloadOptions {
            Token = GITEA_TOKEN,
            RepoUrl = GITEA_REPOURL,
            ReleaseDir = new DirectoryInfo(releaseDirNew),
            Channel = uniqueSuffix,
        }).GetAwaiterResult();

        var filename = $"{id}-{newVer}-{uniqueSuffix}-full.nupkg";
        Assert.True(File.Exists(Path.Combine(releaseDirNew, filename)));
    }

    [SkippableFact]
    public void WillCreateTagOnDefaultBranchIfTargetCommitishNotSet()
    {
        Skip.If(String.IsNullOrWhiteSpace(GITEA_TOKEN), "VELOPACK_GITEA_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<GiteaDeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var ghvar = GiteaReleaseTest.Create("targetCommitish", logger, true);
        var (repoOwner, repoName) = GiteaRepository.GetOwnerAndRepo(GITEA_REPOURL);
        var id = "GiteaUpdateTest";
        var releaseName = ghvar.ReleaseName;
        var client = ghvar.Client;
        var uniqueSuffix = ghvar.UniqueSuffix;
        var version = $"0.0.1-{uniqueSuffix}";
        TestApp.PackTestApp(id, version, "t1", releaseDir, logger, channel: uniqueSuffix);

        //Setup Gitea
        Configuration config = new Configuration();
        if (!string.IsNullOrWhiteSpace(GITEA_TOKEN)) {
            config.ApiKey.Add("token", GITEA_TOKEN);
        }
        var uri = new Uri(GITEA_REPOURL);
        var baseUri = uri.GetLeftPart(System.UriPartial.Authority);
        config.BasePath = baseUri + "/api/v1";
        var apiInstance = new RepositoryApi(config);

        var gh = new GiteaRepository(logger);
        var options = new GiteaUploadOptions {
            ReleaseName = releaseName,
            ReleaseDir = new DirectoryInfo(releaseDir),
            RepoUrl = GITEA_REPOURL,
            Token = GITEA_TOKEN,
            Prerelease = false,
            Publish = true,
            Channel = uniqueSuffix,
            TagName = version
        };

        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

        var expected = "main"; //Default branch in VelopackGiteaUpdateTest repo
        ApiResponse<Repository> repositoryInfo = apiInstance.RepoGetWithHttpInfoAsync(repoOwner, repoName).GetAwaiterResult();
        var newRelease = apiInstance.RepoListReleasesWithHttpInfoAsync(repoOwner, repoName, page: 1, limit: (int) repositoryInfo.Data.ReleaseCounter).GetAwaiterResult().Data.Single(s => s.Name == releaseName);
        Assert.Equal(expected, newRelease.TargetCommitish);
    }

    [SkippableTheory]
    [InlineData("main")]
    [InlineData("efad3d96421c50737abd1bcfeccd90019d6541ed")] //Commit SHA in VelopackGiteaUpdateTest repo
    public void WillCreateTagUsingTargetCommitish(string targetCommitish)
    {
        Skip.If(String.IsNullOrWhiteSpace(GITEA_TOKEN), "VELOPACK_GITEA_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<GiteaDeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var ghvar = GiteaReleaseTest.Create("targetCommitish", logger, true);
        var (repoOwner, repoName) = GiteaRepository.GetOwnerAndRepo(GITEA_REPOURL);
        var id = "GiteaUpdateTest";
        var releaseName = ghvar.ReleaseName;
        var client = ghvar.Client;
        var uniqueSuffix = ghvar.UniqueSuffix;
        var version = $"0.0.1-{uniqueSuffix}";
        TestApp.PackTestApp(id, version, "t1", releaseDir, logger, channel: uniqueSuffix);

        //Setup Gitea
        Configuration config = new Configuration();
        if (!string.IsNullOrWhiteSpace(GITEA_TOKEN)) {
            config.ApiKey.Add("token", GITEA_TOKEN);
        }
        var uri = new Uri(GITEA_REPOURL);
        var baseUri = uri.GetLeftPart(System.UriPartial.Authority);
        config.BasePath = baseUri + "/api/v1";
        var apiInstance = new RepositoryApi(config);

        var gh = new GiteaRepository(logger);
        var options = new GiteaUploadOptions {
            ReleaseName = releaseName,
            ReleaseDir = new DirectoryInfo(releaseDir),
            RepoUrl = GITEA_REPOURL,
            Token = GITEA_TOKEN,
            Prerelease = false,
            Publish = true,
            Channel = uniqueSuffix,
            TagName = version,
            TargetCommitish = targetCommitish
        };

        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();
        ApiResponse<Repository> repositoryInfo = apiInstance.RepoGetWithHttpInfoAsync(repoOwner, repoName).GetAwaiterResult();
        var newRelease = apiInstance.RepoListReleasesWithHttpInfoAsync(repoOwner, repoName, page: 1, limit: (int) repositoryInfo.Data.ReleaseCounter).GetAwaiterResult().Data.Single(s => s.Name == releaseName);
        Assert.Equal(targetCommitish, newRelease.TargetCommitish);
    }

    private class GiteaReleaseTest : IDisposable
    {
        private readonly bool deleteTagOnDispose;

        public string ReleaseName { get; }
        public string UniqueSuffix { get; }
        public Configuration Client { get; }
        public ILogger Logger { get; }

        public GiteaReleaseTest(string releaseName, string uniqueSuffix, Configuration client, ILogger logger, bool deleteTagOnDispose = false)
        {
            ReleaseName = releaseName;
            UniqueSuffix = uniqueSuffix;
            Client = client;
            Logger = logger;
            this.deleteTagOnDispose = deleteTagOnDispose;
        }

        public static GiteaReleaseTest Create(string method, ILogger logger, bool deleteTagOnDispose = false)
        {
            var ci = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));
            var uniqueSuffix = (ci ? "ci-" : "local-") + VelopackRuntimeInfo.SystemOs.GetOsShortName();
            var releaseName = $"{VelopackRuntimeInfo.VelopackNugetVersion}-{uniqueSuffix}-{method}";
            var (repoOwner, repoName) = GiteaRepository.GetOwnerAndRepo(GITEA_REPOURL);

            //Setup Gitea
            Configuration config = new Configuration();
            if (!string.IsNullOrWhiteSpace(GITEA_TOKEN)) {
                config.ApiKey.Add("token", GITEA_TOKEN);
            }
            var uri = new Uri(GITEA_REPOURL);
            var baseUri = uri.GetLeftPart(System.UriPartial.Authority);
            config.BasePath = baseUri + "/api/v1";
            var apiInstance = new RepositoryApi(config);

            //Get repo info
            ApiResponse<Repository> repositoryInfo = apiInstance.RepoGetWithHttpInfoAsync(repoOwner, repoName).GetAwaiterResult();

            // delete release if already exists
            var existingRelease = apiInstance.RepoListReleasesWithHttpInfoAsync(repoOwner, repoName, page: 1, limit: (int) repositoryInfo.Data.ReleaseCounter).GetAwaiterResult().Data.SingleOrDefault(s => s.Name == releaseName);
            if (existingRelease != null) {
                apiInstance.RepoDeleteReleaseAsync(repoOwner, repoName, existingRelease.Id).GetAwaiterResult();
                if (deleteTagOnDispose) {
                    apiInstance.RepoDeleteTagAsync(repoOwner, repoName, existingRelease.TagName).GetAwaiterResult();
                }
                logger.Info("Deleted existing release: " + releaseName);
            }
            return new GiteaReleaseTest(releaseName, uniqueSuffix, config, logger, deleteTagOnDispose);
        }

        public void Dispose()
        {
            var (repoOwner, repoName) = GiteaRepository.GetOwnerAndRepo(GITEA_REPOURL);
            var apiInstance = new RepositoryApi(Client);
            //Get repo info
            ApiResponse<Repository> repositoryInfo = apiInstance.RepoGetWithHttpInfoAsync(repoOwner, repoName).GetAwaiterResult();

            var finalRelease = apiInstance.RepoListReleasesWithHttpInfoAsync(repoOwner, repoName, page: 1, limit: (int) repositoryInfo.Data.ReleaseCounter).GetAwaiterResult().Data.SingleOrDefault(s => s.Name == ReleaseName);
            if (finalRelease != null) {
                apiInstance.RepoDeleteReleaseAsync(repoOwner, repoName, finalRelease.Id).GetAwaiterResult();
                if (deleteTagOnDispose) {
                    apiInstance.RepoDeleteTagAsync(repoOwner, repoName, finalRelease.TagName).GetAwaiterResult();
                }
                Logger.Info($"Deleted final release '{ReleaseName}'");
            }
        }
    }
}
