using System.Diagnostics;
using Velopack.Deployment;
using Velopack.Packaging.Unix.Commands;
using Velopack.Packaging.Windows.Commands;
using Velopack.Sources;
using Octokit;
using Velopack.Packaging.Exceptions;

namespace Velopack.Packaging.Tests
{
    public class DeploymentTests
    {
        public readonly static string GITHUB_TOKEN = Environment.GetEnvironmentVariable("VELOPACK_GITHUB_TEST_TOKEN");
        public readonly static string GITHUB_REPOURL = "https://github.com/caesay/VelopackGithubUpdateTest";

        private readonly ITestOutputHelper _output;

        public DeploymentTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [SkippableFact]
        public void WillRefuseToUploadMultipleWithoutMergeArg()
        {
            Skip.If(String.IsNullOrWhiteSpace(GITHUB_TOKEN), "VELOPACK_GITHUB_TEST_TOKEN is not set.");
            using var logger = _output.BuildLoggerFor<DeploymentTests>();
            using var _1 = Utility.GetTempDirectory(out var releaseDir);
            using var _2 = Utility.GetTempDirectory(out var releaseDir2);
            using var ghvar = GitHubReleaseTest.Create("nomerge", logger);
            var id = "GithubUpdateTest";
            PackTestApp(id, $"0.0.1-{ghvar.UniqueSuffix}", "t1", releaseDir, logger);

            var gh = new GitHubRepository(logger);
            var options = new GitHubUploadOptions {
                ReleaseName = ghvar.ReleaseName,
                ReleaseDir = new DirectoryInfo(releaseDir),
                RepoUrl = GITHUB_REPOURL,
                Token = GITHUB_TOKEN,
                Prerelease = false,
                Publish = true,
            };

            gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

            PackTestApp(id, $"0.0.2-{ghvar.UniqueSuffix}", "t1", releaseDir2, logger);
            options.ReleaseDir = new DirectoryInfo(releaseDir2);

            Assert.ThrowsAny<UserInfoException>(() => gh.UploadMissingAssetsAsync(options).GetAwaiterResult());
        }

        [SkippableFact]
        public void WillNotMergeMixmatchedTag()
        {
            Skip.If(String.IsNullOrWhiteSpace(GITHUB_TOKEN), "VELOPACK_GITHUB_TEST_TOKEN is not set.");
            using var logger = _output.BuildLoggerFor<DeploymentTests>();
            using var _1 = Utility.GetTempDirectory(out var releaseDir);
            using var _2 = Utility.GetTempDirectory(out var releaseDir2);
            using var ghvar = GitHubReleaseTest.Create("mixmatched", logger);
            var id = "GithubUpdateTest";
            PackTestApp(id, $"0.0.1-{ghvar.UniqueSuffix}", "t1", releaseDir, logger);

            var gh = new GitHubRepository(logger);
            var options = new GitHubUploadOptions {
                ReleaseName = ghvar.ReleaseName,
                ReleaseDir = new DirectoryInfo(releaseDir),
                RepoUrl = GITHUB_REPOURL,
                Token = GITHUB_TOKEN,
                Prerelease = false,
                Publish = true,
                Merge = true,
            };

            gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

            PackTestApp(id, $"0.0.2-{ghvar.UniqueSuffix}", "t1", releaseDir2, logger);
            options.ReleaseDir = new DirectoryInfo(releaseDir2);

            Assert.ThrowsAny<UserInfoException>(() => gh.UploadMissingAssetsAsync(options).GetAwaiterResult());
        }

        [SkippableFact]
        public void WillMergeGithubReleases()
        {
            Skip.If(String.IsNullOrWhiteSpace(GITHUB_TOKEN), "VELOPACK_GITHUB_TEST_TOKEN is not set.");
            using var logger = _output.BuildLoggerFor<DeploymentTests>();
            using var _1 = Utility.GetTempDirectory(out var releaseDir);
            using var _2 = Utility.GetTempDirectory(out var releaseDir2);
            using var ghvar = GitHubReleaseTest.Create("yesmerge", logger);
            var id = "GithubUpdateTest";
            PackTestApp(id, $"0.0.1-{ghvar.UniqueSuffix}", "t1", releaseDir, logger);

            var gh = new GitHubRepository(logger);
            var options = new GitHubUploadOptions {
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

            PackTestApp(id, $"0.0.1-{ghvar.UniqueSuffix}", "t1", releaseDir2, logger, channel: "experimental");
            options.ReleaseDir = new DirectoryInfo(releaseDir2);
            options.Channel = "experimental";

            gh.UploadMissingAssetsAsync(options).GetAwaiterResult();
        }

        [SkippableFact]
        public void CanDeployAndUpdateFromGithub()
        {
            Skip.If(String.IsNullOrWhiteSpace(GITHUB_TOKEN), "VELOPACK_GITHUB_TEST_TOKEN is not set.");
            using var logger = _output.BuildLoggerFor<DeploymentTests>();
            var id = "GithubUpdateTest";
            using var _1 = Utility.GetTempDirectory(out var releaseDir);
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

            var newVer = $"{VelopackRuntimeInfo.VelopackNugetVersion}-{uniqueSuffix}";
            PackTestApp(id, $"0.0.1-{uniqueSuffix}", "t1", releaseDir, logger, notesPath);
            PackTestApp(id, newVer, "t2", releaseDir, logger, notesPath);

            // deploy
            var gh = new GitHubRepository(logger);
            var options = new GitHubUploadOptions {
                ReleaseName = releaseName,
                ReleaseDir = new DirectoryInfo(releaseDir),
                RepoUrl = GITHUB_REPOURL,
                Token = GITHUB_TOKEN,
                Prerelease = false,
                Publish = true,
            };
            gh.UploadMissingAssetsAsync(options).GetAwaiterResult();

            // check
            var newRelease = client.Repository.Release.GetAll(repoOwner, repoName).GetAwaiterResult().Single(s => s.Name == releaseName);
            Assert.False(newRelease.Draft);
            Assert.Equal(notesContent.Trim().ReplaceLineEndings("\n"), newRelease.Body.Trim());

            // update
            var source = new GithubSource(GITHUB_REPOURL, GITHUB_TOKEN, false, logger: logger);
            var releases = source.GetReleaseFeed().GetAwaiterResult();

            var ghrel = releases.Select(r => (GithubReleaseEntry) r).ToArray();
            Assert.Equal(2, ghrel.Length);
            foreach (var r in ghrel) {
                Assert.Equal(releaseName, r.Release.Name);
                Assert.Equal(id, r.PackageId);
                Assert.Equal(newVer, r.Version.ToNormalizedString());
            }

            using var _2 = Utility.GetTempDirectory(out var releaseDirNew);
            gh.DownloadLatestFullPackageAsync(new GitHubDownloadOptions {
                Token = GITHUB_TOKEN,
                RepoUrl = GITHUB_REPOURL,
                ReleaseDir = new DirectoryInfo(releaseDirNew),
            }).GetAwaiterResult();

            var filename = $"{id}-{newVer}-{VelopackRuntimeInfo.SystemOs.GetOsShortName()}-full.nupkg";
            Assert.True(File.Exists(Path.Combine(releaseDirNew, filename)));
        }

        private class GitHubReleaseTest : IDisposable
        {
            public string ReleaseName { get; }
            public string UniqueSuffix { get; }
            public GitHubClient Client { get; }
            public ILogger Logger { get; }

            public GitHubReleaseTest(string releaseName, string uniqueSuffix, GitHubClient client, ILogger logger)
            {
                ReleaseName = releaseName;
                UniqueSuffix = uniqueSuffix;
                Client = client;
                Logger = logger;
            }

            public static GitHubReleaseTest Create(string method, ILogger logger)
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
                    logger.Info("Deleted existing release: " + releaseName);
                }
                return new GitHubReleaseTest(releaseName, uniqueSuffix, client, logger);
            }

            public void Dispose()
            {
                var (repoOwner, repoName) = GitHubRepository.GetOwnerAndRepo(GITHUB_REPOURL);
                var finalRelease = Client.Repository.Release.GetAll(repoOwner, repoName).GetAwaiterResult().SingleOrDefault(s => s.Name == ReleaseName);
                if (finalRelease != null) {
                    Client.Repository.Release.Delete(repoOwner, repoName, finalRelease.Id).GetAwaiterResult();
                    Logger.Info($"Deleted final release '{ReleaseName}'");
                }
            }
        }

        private void PackTestApp(string id, string version, string testString, string releaseDir, ILogger logger,
            string releaseNotes = null, string channel = null)
        {
            var projDir = PathHelper.GetTestRootPath("TestApp");
            var testStringFile = Path.Combine(projDir, "Const.cs");
            var oldText = File.ReadAllText(testStringFile);

            try {
                File.WriteAllText(testStringFile, $"class Const {{ public const string TEST_STRING = \"{testString}\"; }}");

                var args = new string[] { "publish", "--no-self-contained", "-c", "Release", "-r", VelopackRuntimeInfo.SystemRid, "-o", "publish" };

                var psi = new ProcessStartInfo("dotnet");
                psi.WorkingDirectory = projDir;
                psi.AppendArgumentListSafe(args, out var debug);

                logger.Info($"TEST: Running {psi.FileName} {debug}");

                using var p = Process.Start(psi);
                p.WaitForExit();

                if (p.ExitCode != 0)
                    throw new Exception($"dotnet publish failed with exit code {p.ExitCode}");

                if (VelopackRuntimeInfo.IsWindows) {
                    var options = new WindowsPackOptions {
                        EntryExecutableName = "TestApp.exe",
                        ReleaseDir = new DirectoryInfo(releaseDir),
                        PackId = id,
                        TargetRuntime = RID.Parse(VelopackRuntimeInfo.SystemOs.GetOsShortName()),
                        PackVersion = version,
                        PackDirectory = Path.Combine(projDir, "publish"),
                        ReleaseNotes = releaseNotes,
                        Channel = channel,
                    };
                    var runner = new WindowsPackCommandRunner(logger);
                    runner.Run(options).GetAwaiterResult();
                } else if (VelopackRuntimeInfo.IsOSX) {
                    var options = new OsxPackOptions {
                        EntryExecutableName = "TestApp",
                        ReleaseDir = new DirectoryInfo(releaseDir),
                        PackId = id,
                        Icon = Path.Combine(PathHelper.GetProjectDir(), "examples", "AvaloniaCrossPlat", "Velopack.icns"),
                        TargetRuntime = RID.Parse(VelopackRuntimeInfo.SystemOs.GetOsShortName()),
                        PackVersion = version,
                        PackDirectory = Path.Combine(projDir, "publish"),
                        ReleaseNotes = releaseNotes,
                        Channel = channel,
                    };
                    var runner = new OsxPackCommandRunner(logger);
                    runner.Run(options).GetAwaiterResult();
                } else if (VelopackRuntimeInfo.IsLinux) {
                    var options = new LinuxPackOptions {
                        EntryExecutableName = "TestApp",
                        ReleaseDir = new DirectoryInfo(releaseDir),
                        PackId = id,
                        Icon = Path.Combine(PathHelper.GetProjectDir(), "examples", "AvaloniaCrossPlat", "Velopack.png"),
                        TargetRuntime = RID.Parse(VelopackRuntimeInfo.SystemOs.GetOsShortName()),
                        PackVersion = version,
                        PackDirectory = Path.Combine(projDir, "publish"),
                        ReleaseNotes = releaseNotes,
                        Channel = channel,
                    };
                    var runner = new LinuxPackCommandRunner(logger);
                    runner.Run(options).GetAwaiterResult();
                } else {
                    throw new PlatformNotSupportedException();
                }
            } finally {
                File.WriteAllText(testStringFile, oldText);
            }
        }
    }
}
