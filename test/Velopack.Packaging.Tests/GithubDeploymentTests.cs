using Velopack.Deployment;
using Velopack.Sources;
using Octokit;
using Velopack.Core;
using Velopack.Locators;
using Velopack.Packaging.Exceptions;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

public class GithubDeploymentTests
{
    public readonly static string GITHUB_TOKEN = Environment.GetEnvironmentVariable("VELOPACK_GITHUB_TEST_TOKEN");
    public readonly static string GITHUB_REPOURL = "https://github.com/caesay/VelopackGithubUpdateTest";

    private readonly ITestOutputHelper _output;

    public GithubDeploymentTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public void WillRefuseToUploadMultipleWithoutMergeArg()
    {
        Skip.If(String.IsNullOrWhiteSpace(GITHUB_TOKEN), "VELOPACK_GITHUB_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<GithubDeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var releaseDir2);
        using var ghvar = GitHubReleaseTest.Create("nomerge", logger);
        var id = "GithubUpdateTest";
        var uniqueSuffix = ghvar.UniqueSuffix;

        TestApp.PackTestApp(id, $"0.0.1-{ghvar.UniqueSuffix}", "t1", releaseDir, logger, channel: uniqueSuffix);

        var gh = new GitHubRepository(logger);
        var options = new GitHubUploadOptions {
            ReleaseName = ghvar.ReleaseName,
            ReleaseDir = new DirectoryInfo(releaseDir),
            RepoUrl = GITHUB_REPOURL,
            Token = GITHUB_TOKEN,
            Prerelease = false,
            Publish = true,
            Channel = uniqueSuffix,
        };

        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

        TestApp.PackTestApp(id, $"0.0.2-{ghvar.UniqueSuffix}", "t1", releaseDir2, logger);
        options.ReleaseDir = new DirectoryInfo(releaseDir2);

        Assert.ThrowsAny<UserInfoException>(() => gh.UploadMissingAssetsAsync(options).GetAwaiterResult());
    }

    [SkippableFact]
    public void WillNotMergeMixmatchedTag()
    {
        Skip.If(String.IsNullOrWhiteSpace(GITHUB_TOKEN), "VELOPACK_GITHUB_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<GithubDeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var releaseDir2);
        using var ghvar = GitHubReleaseTest.Create("mixmatched", logger);
        var id = "GithubUpdateTest";
        var uniqueSuffix = ghvar.UniqueSuffix;
        
        TestApp.PackTestApp(id, $"0.0.1-{ghvar.UniqueSuffix}", "t1", releaseDir, logger, channel: uniqueSuffix);

        var gh = new GitHubRepository(logger);
        var options = new GitHubUploadOptions {
            Channel = uniqueSuffix,
            ReleaseName = ghvar.ReleaseName,
            ReleaseDir = new DirectoryInfo(releaseDir),
            RepoUrl = GITHUB_REPOURL,
            Token = GITHUB_TOKEN,
            Prerelease = false,
            Publish = true,
            Merge = true,
        };

        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

        TestApp.PackTestApp(id, $"0.0.2-{ghvar.UniqueSuffix}", "t1", releaseDir2, logger);
        options.ReleaseDir = new DirectoryInfo(releaseDir2);

        Assert.ThrowsAny<UserInfoException>(() => gh.UploadMissingAssetsAsync(options).GetAwaiterResult());
    }

    [SkippableFact]
    public void WillMergeGithubReleases()
    {
        Skip.If(String.IsNullOrWhiteSpace(GITHUB_TOKEN), "VELOPACK_GITHUB_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<GithubDeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var releaseDir2);
        using var ghvar = GitHubReleaseTest.Create("yesmerge", logger);
        var id = "GithubUpdateTest";
        var uniqueSuffix = ghvar.UniqueSuffix;

        TestApp.PackTestApp(id, $"0.0.1-{ghvar.UniqueSuffix}", "t1", releaseDir, logger, channel: uniqueSuffix);

        var gh = new GitHubRepository(logger);
        var options = new GitHubUploadOptions {
            Channel = uniqueSuffix,
            ReleaseName = ghvar.ReleaseName,
            ReleaseDir = new DirectoryInfo(releaseDir),
            RepoUrl = GITHUB_REPOURL,
            Token = GITHUB_TOKEN,
            TagName = $"0.0.1-{ghvar.UniqueSuffix}",
            Prerelease = false,
            Publish = true,
            Merge = true,
        };

        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

        TestApp.PackTestApp(id, $"0.0.1-{ghvar.UniqueSuffix}", "t1", releaseDir2, logger, channel: "experimental");
        options.ReleaseDir = new DirectoryInfo(releaseDir2);
        options.Channel = "experimental";

        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();
    }

    [SkippableFact]
    public void CanDeployAndUpdateFromGithub()
    {
        Skip.If(String.IsNullOrWhiteSpace(GITHUB_TOKEN), "VELOPACK_GITHUB_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<GithubDeploymentTests>();
        var id = "GithubUpdateTest";
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        var (repoOwner, repoName) = GitHubRepository.GetOwnerAndRepo(GITHUB_REPOURL);
        using var ghvar = GitHubReleaseTest.Create("integration", logger);
        var releaseName = ghvar.ReleaseName;
        var uniqueSuffix = ghvar.UniqueSuffix;
        var client = ghvar.Client;

        // create releases
        var notesPath = Path.Combine(releaseDir, "NOTES");
        var notesContent = $"""
            # Release {releaseName}
            This is just a _test_!
            """;
        File.WriteAllText(notesPath, notesContent);

        if (String.IsNullOrEmpty(GITHUB_TOKEN))
            throw new Exception("VELOPACK_GITHUB_TEST_TOKEN is not set.");

        var newVer = $"{VelopackRuntimeInfo.VelopackNugetVersion}";
        TestApp.PackTestApp(id, $"0.0.1", "t1", releaseDir, logger, notesPath, channel: uniqueSuffix);
        TestApp.PackTestApp(id, newVer, "t2", releaseDir, logger, notesPath, channel: uniqueSuffix);

        // deploy
        var gh = new GitHubRepository(logger);
        var options = new GitHubUploadOptions {
            ReleaseName = releaseName,
            ReleaseDir = new DirectoryInfo(releaseDir),
            RepoUrl = GITHUB_REPOURL,
            Token = GITHUB_TOKEN,
            Prerelease = false,
            Publish = true,
            Channel = uniqueSuffix,
        };
        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

        // check
        var newRelease = client.Repository.Release.GetAll(repoOwner, repoName).GetAwaiterResult().Single(s => s.Name == releaseName);
        Assert.False(newRelease.Draft);
        Assert.Equal(notesContent.Trim().ReplaceLineEndings("\n"), newRelease.Body.Trim());

        // update
        var source = new GithubSource(GITHUB_REPOURL, GITHUB_TOKEN, false);
        var releases = source.GetReleaseFeed(logger.ToVelopackLogger(), null, channel: uniqueSuffix).GetAwaiterResult();

        var ghrel = releases.Assets.Select(r => (GithubSource.GitBaseAsset) r).ToArray();
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
        gh.DownloadLatestFullPackageAsync(
            new GitHubDownloadOptions {
                Token = GITHUB_TOKEN,
                RepoUrl = GITHUB_REPOURL,
                ReleaseDir = new DirectoryInfo(releaseDirNew),
                Channel = uniqueSuffix,
            }).GetAwaiterResult();

        var filename = $"{id}-{newVer}-{uniqueSuffix}-full.nupkg";
        Assert.True(File.Exists(Path.Combine(releaseDirNew, filename)));
    }

    [SkippableFact]
    public void WillCreateTagOnDefaultBranchIfTargetCommitishNotSet()
    {
        Skip.If(String.IsNullOrWhiteSpace(GITHUB_TOKEN), "VELOPACK_GITHUB_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<GithubDeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var ghvar = GitHubReleaseTest.Create("targetCommitish", logger, true);
        var (repoOwner, repoName) = GitHubRepository.GetOwnerAndRepo(GITHUB_REPOURL);
        var id = "GithubUpdateTest";
        var releaseName = ghvar.ReleaseName;
        var client = ghvar.Client;
        var uniqueSuffix = ghvar.UniqueSuffix;
        var version = $"0.0.1-{uniqueSuffix}";
        TestApp.PackTestApp(id, version, "t1", releaseDir, logger, channel: uniqueSuffix);

        var gh = new GitHubRepository(logger);
        var options = new GitHubUploadOptions {
            ReleaseName = releaseName,
            ReleaseDir = new DirectoryInfo(releaseDir),
            RepoUrl = GITHUB_REPOURL,
            Token = GITHUB_TOKEN,
            Prerelease = false,
            Publish = true,
            Channel = uniqueSuffix,
            TagName = version
        };

        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

        var expected = "main"; //Default branch in VelopackGithubUpdateTest repo
        var newRelease = client.Repository.Release.GetAll(repoOwner, repoName).GetAwaiterResult().Single(s => s.Name == releaseName);
        Assert.Equal(expected, newRelease.TargetCommitish);
    }

    [SkippableTheory]
    [InlineData("main")]
    [InlineData("31fca3d97e657a4fbee076462c5efb271f074656")] //Commit SHA in VelopackGithubUpdateTest repo
    public void WillCreateTagUsingTargetCommitish(string targetCommitish)
    {
        Skip.If(String.IsNullOrWhiteSpace(GITHUB_TOKEN), "VELOPACK_GITHUB_TEST_TOKEN is not set.");
        using var logger = _output.BuildLoggerFor<GithubDeploymentTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var ghvar = GitHubReleaseTest.Create("targetCommitish", logger, true);
        var (repoOwner, repoName) = GitHubRepository.GetOwnerAndRepo(GITHUB_REPOURL);
        var id = "GithubUpdateTest";
        var releaseName = ghvar.ReleaseName;
        var client = ghvar.Client;
        var uniqueSuffix = ghvar.UniqueSuffix;
        var version = $"0.0.1-{uniqueSuffix}";
        TestApp.PackTestApp(id, version, "t1", releaseDir, logger, channel: uniqueSuffix);

        var gh = new GitHubRepository(logger);
        var options = new GitHubUploadOptions {
            ReleaseName = releaseName,
            ReleaseDir = new DirectoryInfo(releaseDir),
            RepoUrl = GITHUB_REPOURL,
            Token = GITHUB_TOKEN,
            Prerelease = false,
            Publish = true,
            Channel = uniqueSuffix,
            TagName = version,
            TargetCommitish = targetCommitish
        };

        gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

        var newRelease = client.Repository.Release.GetAll(repoOwner, repoName).GetAwaiterResult().Single(s => s.Name == releaseName);
        Assert.Equal(targetCommitish, newRelease.TargetCommitish);
    }

    private class GitHubReleaseTest : IDisposable
    {
        private readonly bool deleteTagOnDispose;

        public string ReleaseName { get; }
        public string UniqueSuffix { get; }
        public GitHubClient Client { get; }
        public ILogger Logger { get; }

        public GitHubReleaseTest(string releaseName, string uniqueSuffix, GitHubClient client, ILogger logger, bool deleteTagOnDispose = false)
        {
            ReleaseName = releaseName;
            UniqueSuffix = uniqueSuffix;
            Client = client;
            Logger = logger;
            this.deleteTagOnDispose = deleteTagOnDispose;
        }

        public static GitHubReleaseTest Create(string method, ILogger logger, bool deleteTagOnDispose = false)
        {
            var ci = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));
            var uniqueSuffix = (ci ? "ci-" : "local-") + VelopackRuntimeInfo.SystemOs.GetOsShortName();
            var releaseName = $"{VelopackRuntimeInfo.VelopackNugetVersion}-{uniqueSuffix}-{method}";
            var (repoOwner, repoName) = GitHubRepository.GetOwnerAndRepo(GITHUB_REPOURL);

            // delete release if already exists
            var client = new GitHubClient(new ProductHeaderValue("Velopack")) {
                Credentials = new Credentials(GITHUB_TOKEN)
            };
            var existingRelease = client.Repository.Release.GetAll(repoOwner, repoName).GetAwaiterResult().SingleOrDefault(s => s.Name == releaseName);
            if (existingRelease != null) {
                client.Repository.Release.Delete(repoOwner, repoName, existingRelease.Id).GetAwaiterResult();
                if (deleteTagOnDispose) {
                    client.Git.Reference.Delete(repoOwner, repoName, $"tags/{existingRelease.TagName}").GetAwaiterResult();
                }

                logger.Info("Deleted existing release: " + releaseName);
            }

            return new GitHubReleaseTest(releaseName, uniqueSuffix, client, logger, deleteTagOnDispose);
        }

        public void Dispose()
        {
            var (repoOwner, repoName) = GitHubRepository.GetOwnerAndRepo(GITHUB_REPOURL);
            var finalRelease = Client.Repository.Release.GetAll(repoOwner, repoName).GetAwaiterResult().SingleOrDefault(s => s.Name == ReleaseName);
            if (finalRelease != null) {
                Client.Repository.Release.Delete(repoOwner, repoName, finalRelease.Id).GetAwaiterResult();
                if (deleteTagOnDispose) {
                    Client.Git.Reference.Delete(repoOwner, repoName, $"tags/{finalRelease.TagName}").GetAwaiterResult();
                }

                Logger.Info($"Deleted final release '{ReleaseName}'");
            }
        }
    }
}