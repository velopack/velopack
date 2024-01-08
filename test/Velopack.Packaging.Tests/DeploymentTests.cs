using System.Diagnostics;
using Velopack.Deployment;
using Velopack.Packaging.OSX.Commands;
using Velopack.Packaging.Windows.Commands;
using Velopack.Sources;
using Octokit;

namespace Velopack.Packaging.Tests
{
    public class DeploymentTests
    {
        public readonly string GITHUB_TOKEN = Environment.GetEnvironmentVariable("VELOPACK_GITHUB_TEST_TOKEN");
        public readonly string GITHUB_REPOURL = "https://github.com/caesay/VelopackGithubUpdateTest";

        private readonly ITestOutputHelper _output;

        public DeploymentTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void CanDeployAndUpdateFromGithub()
        {
            using var logger = _output.BuildLoggerFor<DeploymentTests>();
            var id = "GithubUpdateTest";
            var ci = !String.IsNullOrEmpty(Environment.GetEnvironmentVariable("CI"));
            using var _1 = Utility.GetTempDirectory(out var releaseDir);
            var uniqueSuffix = ci ? "ci-" : "local-" + VelopackRuntimeInfo.SystemOs.GetOsShortName();
            var releaseName = $"{VelopackRuntimeInfo.VelopackDisplayVersion}-{uniqueSuffix}";

            // delete release if already exists
            var client = new GitHubClient(new ProductHeaderValue("Velopack")) {
                Credentials = new Credentials(GITHUB_TOKEN)
            };
            var (repoOwner, repoName) = GitHubRepository.GetOwnerAndRepo(GITHUB_REPOURL);
            var existingRelease = client.Repository.Release.GetAll(repoOwner, repoName).GetAwaiterResult().SingleOrDefault(s => s.Name == releaseName);
            if (existingRelease != null) {
                client.Repository.Release.Delete(repoOwner, repoName, existingRelease.Id).GetAwaiterResult();
                logger.Info($"Deleted existing release '{releaseName}'");
            }

            // create releases
            var notesPath = Path.Combine(releaseDir, "NOTES");
            var notesContent = $"""
# Release {releaseName}
CI: {ci}
This is just a _test_!
""";
            File.WriteAllText(notesPath, notesContent);

            if (String.IsNullOrEmpty(GITHUB_TOKEN))
                throw new Exception("VELOPACK_GITHUB_TEST_TOKEN is not set.");

            PackTestApp(id, $"1.0.0-{uniqueSuffix}", "t1", releaseDir, logger, notesPath);
            PackTestApp(id, $"2.0.0-{uniqueSuffix}", "t2", releaseDir, logger, notesPath);

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
                Assert.Equal($"2.0.0-{uniqueSuffix}", r.Version.ToNormalizedString());
            }

            using var _2 = Utility.GetTempDirectory(out var releaseDirNew);
            gh.DownloadLatestFullPackageAsync(new GitHubDownloadOptions {
                Token = GITHUB_TOKEN,
                RepoUrl = GITHUB_REPOURL,
                ReleaseDir = new DirectoryInfo(releaseDirNew),
            }).GetAwaiterResult();

            var filename = $"{id}-2.0.0-{uniqueSuffix}-{VelopackRuntimeInfo.SystemOs.GetOsShortName()}-full.nupkg";
            Assert.True(File.Exists(Path.Combine(releaseDirNew, filename)));
        }

        private void PackTestApp(string id, string version, string testString, string releaseDir, ILogger logger, string releaseNotes)
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
                    };
                    var runner = new WindowsPackCommandRunner(logger);
                    runner.Pack(options);
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
                    };
                    var runner = new OsxPackCommandRunner(logger);
                    runner.Pack(options);
                } else {
                    throw new PlatformNotSupportedException();
                }
            } finally {
                File.WriteAllText(testStringFile, oldText);
            }
        }
    }
}
