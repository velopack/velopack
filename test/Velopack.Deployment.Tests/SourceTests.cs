using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Velopack.Core;
using Velopack.Deployment;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack.Deployment.Tests;

/// <summary>
/// Shared plumbing for the cross-language update-source tests. Every source kind is exercised through every
/// client library (C# in-process for coverage; Rust/C++/Python/Node.js via the external harnesses run by
/// <see cref="HarnessRunner"/>). All rows share the same expectations: an installed 1.0.0 app sees a 2.0.0
/// update on the 'stable' channel, downloads it, and the SHA256 matches the packed nupkg. The source-kind
/// classes below are split by target so different backing services parallelize; tests hitting the same
/// service share a [Collection] and stay serial. The destination (gitea repo / gitlab release / static
/// server / github repo) is arranged fresh per test row.
/// </summary>
internal static class SourceTestHelpers
{
    /// <summary>
    /// Runs one (source kind × language) cell: check for updates, assert the 2.0.0 target, download,
    /// and assert the SHA256 of the downloaded full package.
    /// </summary>
    public static async Task RunRowAsync(
        HarnessLang lang, string kind, string url, string? token, bool prerelease, InstalledAppFixture fixture, ILogger logger)
    {
        if (lang == HarnessLang.CSharp) {
            await RunCSharpRowAsync(kind, url, token, prerelease, fixture, logger);
            return;
        }

        var spec = fixture.CreateInstalledLayout(out var rootDir);
        using var _1 = TempUtil.GetTempDirectory(out var cfgDir);
        try {
            var config = new JsonObject {
                ["source"] = new JsonObject {
                    ["kind"] = kind,
                    ["url"] = url,
                    ["token"] = token,
                    ["prerelease"] = prerelease,
                },
                ["locator"] = JsonNode.Parse(spec.ToJson()),
                ["channel"] = InstalledAppFixture.Channel,
                ["action"] = "download",
                ["downloadDir"] = spec.PackagesDir,
            };
            var configPath = Path.Combine(cfgDir, "config.json");
            File.WriteAllText(configPath, config.ToJsonString());

            var result = await HarnessRunner.RunAsync(lang, configPath, logger);
            Assert.True(result.Ok, $"{lang} harness reported a failure: {result.Error}");
            Assert.True(result.UpdateAvailable, $"{lang} harness did not report an available update.");
            Assert.Equal(InstalledAppFixture.LatestVersion, result.TargetVersion);
            Assert.Equal(fixture.Sha256OfLatestFullPackage, result.Sha256);
        } finally {
            TryDelete(rootDir);
        }
    }

    private static async Task RunCSharpRowAsync(string kind, string url, string? token, bool prerelease, InstalledAppFixture fixture, ILogger logger)
    {
        var locator = fixture.CreateCSharpLocator(out var rootDir, logger.ToVelopackLogger());
        try {
            var source = CreateCSharpSource(kind, url, token, prerelease);
            var options = new UpdateOptions { ExplicitChannel = InstalledAppFixture.Channel, AllowVersionDowngrade = false };
            var um = new UpdateManager(source, options, locator);

            var info = await um.CheckForUpdatesAsync();
            Assert.NotNull(info);
            Assert.Equal(InstalledAppFixture.LatestVersion, info.TargetFullRelease.Version?.ToString());

            await um.DownloadUpdatesAsync(info);
            var downloaded = Path.Combine(locator.PackagesDir!, info.TargetFullRelease.FileName);
            Assert.True(File.Exists(downloaded), $"Downloaded package not found at '{downloaded}'.");
            Assert.Equal(fixture.Sha256OfLatestFullPackage, Sha256Upper(downloaded));
        } finally {
            TryDelete(rootDir);
        }
    }

    private static IUpdateSource CreateCSharpSource(string kind, string url, string? token, bool prerelease)
    {
        return kind switch {
            "file" => new SimpleFileSource(new DirectoryInfo(url)),
            "http" => new SimpleWebSource(url),
            "gitea" => new GiteaSource(url, token, prerelease),
            "gitlab" => new GitlabSource(url, token ?? "", prerelease),
            "github" => new GithubSource(url, token, prerelease),
            _ => throw new ArgumentException($"Unknown source kind '{kind}'."),
        };
    }

    public static string Sha256Upper(string filePath)
    {
        using var stream = File.OpenRead(filePath);
        using var sha256 = SHA256.Create();
        return BitConverter.ToString(sha256.ComputeHash(stream)).Replace("-", String.Empty);
    }

    public static void TryDelete(string dir)
    {
        try {
            Directory.Delete(dir, true);
        } catch { /* best effort */ }
    }
}

/// <summary>
/// File source (<see cref="SimpleFileSource"/>) across all five client languages. Uses only the shared
/// in-process fixture feed and per-test temp install dirs, so it needs no external target and runs in its
/// own (implicit per-class) collection.
/// </summary>
public class FileSourceTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Theory]
    [InlineData(HarnessLang.CSharp)]
    [InlineData(HarnessLang.Rust)]
    [InlineData(HarnessLang.Cpp)]
    [InlineData(HarnessLang.Python)]
    [InlineData(HarnessLang.NodeJs)]
    public async Task FileSourceCheckAndDownload(HarnessLang lang)
    {
        using var logger = _output.BuildLoggerFor<FileSourceTests>();
        await HarnessRunner.SkipUnlessAvailableAsync(lang, logger);
        var fixture = InstalledAppFixture.GetOrCreate(logger);
        await SourceTestHelpers.RunRowAsync(lang, "file", fixture.FeedDir, token: null, prerelease: false, fixture, logger);
    }
}

/// <summary>
/// HTTP source (<see cref="SimpleWebSource"/>) across all five client languages. Each row spins up its own
/// <see cref="StaticFileServer"/> on a random loopback port over the shared fixture feed, so it needs no
/// external target and runs in its own (implicit per-class) collection.
/// </summary>
public class HttpSourceTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Theory]
    [InlineData(HarnessLang.CSharp)]
    [InlineData(HarnessLang.Rust)]
    [InlineData(HarnessLang.Cpp)]
    [InlineData(HarnessLang.Python)]
    [InlineData(HarnessLang.NodeJs)]
    public async Task HttpSourceCheckAndDownload(HarnessLang lang)
    {
        using var logger = _output.BuildLoggerFor<HttpSourceTests>();
        await HarnessRunner.SkipUnlessAvailableAsync(lang, logger);
        var fixture = InstalledAppFixture.GetOrCreate(logger);
        using var server = new StaticFileServer(fixture.FeedDir, logger);
        await SourceTestHelpers.RunRowAsync(lang, "http", server.BaseUrl, token: null, prerelease: false, fixture, logger);
    }
}

/// <summary>
/// Gitea source (<see cref="GiteaSource"/>) across all five client languages, plus C#-only prerelease
/// filtering. Targets the <c>gitea-latest</c> server (API back-compat is covered by the destination suites),
/// so it shares the <c>gitea-latest</c> collection with <c>GiteaLatestDeploymentTests</c>.
/// </summary>
[Collection("gitea-latest")]
public class GiteaSourceTests(ITestOutputHelper output)
{
    /// <summary> The gitea server used for source tests. </summary>
    private static GiteaServer SourceGitea => DockerServices.GiteaServers[^1]; // gitea-latest

    private readonly ITestOutputHelper _output = output;

    [Theory]
    [InlineData(HarnessLang.CSharp)]
    [InlineData(HarnessLang.Rust)]
    [InlineData(HarnessLang.Cpp)]
    [InlineData(HarnessLang.Python)]
    [InlineData(HarnessLang.NodeJs)]
    public async Task GiteaSourceCheckAndDownload(HarnessLang lang)
    {
        await DockerServices.SkipUnlessGiteaUpAsync(SourceGitea);
        using var logger = _output.BuildLoggerFor<GiteaSourceTests>();
        await HarnessRunner.SkipUnlessAvailableAsync(lang, logger);
        var fixture = InstalledAppFixture.GetOrCreate(logger);

        await using var repo = await GiteaTestRepo.CreateAsync(SourceGitea, logger, "src");
        await UploadFeedToGiteaAsync(repo, fixture, logger, prerelease: false);
        await SourceTestHelpers.RunRowAsync(lang, "gitea", repo.HttpUrl, repo.Token, prerelease: false, fixture, logger);
    }

    /// <summary>
    /// C#-only: a gitea release marked prerelease must be invisible to GiteaSource(prerelease: false)
    /// and visible to GiteaSource(prerelease: true).
    /// </summary>
    [Fact]
    public async Task GiteaSourcePrereleaseFiltering()
    {
        await DockerServices.SkipUnlessGiteaUpAsync(SourceGitea);
        using var logger = _output.BuildLoggerFor<GiteaSourceTests>();
        var fixture = InstalledAppFixture.GetOrCreate(logger);

        await using var repo = await GiteaTestRepo.CreateAsync(SourceGitea, logger, "srcpre");
        await UploadFeedToGiteaAsync(repo, fixture, logger, prerelease: true);

        var locator = fixture.CreateCSharpLocator(out var rootDir, logger.ToVelopackLogger());
        try {
            var options = new UpdateOptions { ExplicitChannel = InstalledAppFixture.Channel, AllowVersionDowngrade = false };

            var stableOnly = new GiteaSource(repo.HttpUrl, repo.Token, prerelease: false);
            var hidden = await new UpdateManager(stableOnly, options, locator).CheckForUpdatesAsync();
            Assert.Null(hidden);

            var withPre = new GiteaSource(repo.HttpUrl, repo.Token, prerelease: true);
            var visible = await new UpdateManager(withPre, options, locator).CheckForUpdatesAsync();
            Assert.NotNull(visible);
            Assert.Equal(InstalledAppFixture.LatestVersion, visible.TargetFullRelease.Version?.ToString());
        } finally {
            SourceTestHelpers.TryDelete(rootDir);
        }
    }

    /// <summary> Uploads the fixture's packed feed to a gitea repo, in-process, as a published release. </summary>
    private static Task UploadFeedToGiteaAsync(GiteaTestRepo repo, InstalledAppFixture fixture, ILogger logger, bool prerelease)
    {
        return new GiteaUploadCommandRunner(logger).Run(new GiteaUploadOptions {
            RepoUrl = repo.HttpUrl,
            Token = repo.Token,
            ReleaseDir = new DirectoryInfo(fixture.FeedDir),
            Channel = InstalledAppFixture.Channel,
            Publish = true,
            Prerelease = prerelease,
        });
    }
}

/// <summary>
/// GitLab source (<see cref="GitlabSource"/>) across all five client languages, plus C#-only upcoming-release
/// filtering. Runs against the local GitLab container, so it owns the <c>gitlab</c> collection.
/// </summary>
[Collection("gitlab")]
public class GitLabSourceTests(ITestOutputHelper output)
{
    private readonly ITestOutputHelper _output = output;

    [Theory]
    [InlineData(HarnessLang.CSharp)]
    [InlineData(HarnessLang.Rust)]
    [InlineData(HarnessLang.Cpp)]
    [InlineData(HarnessLang.Python)]
    [InlineData(HarnessLang.NodeJs)]
    public async Task GitLabSourceCheckAndDownload(HarnessLang lang)
    {
        await DockerServices.SkipUnlessGitLabUpAsync();
        using var logger = _output.BuildLoggerFor<GitLabSourceTests>();
        await HarnessRunner.SkipUnlessAvailableAsync(lang, logger);
        var fixture = InstalledAppFixture.GetOrCreate(logger);

        await using var project = await GitLabTestProject.CreateAsync(logger, "src");
        await CreateGitLabReleaseFromFeedAsync(project, fixture, logger, upcoming: false);
        var apiUrl = $"{DockerServices.GitLabBaseUrl}/api/v4/projects/{project.Project.Id}";
        await SourceTestHelpers.RunRowAsync(lang, "gitlab", apiUrl, project.Token, prerelease: false, fixture, logger);
    }

    /// <summary>
    /// C#-only: a gitlab 'upcoming' release (future released_at) must be invisible to
    /// GitlabSource(upcomingRelease: false) and visible to GitlabSource(upcomingRelease: true).
    /// </summary>
    [Fact]
    public async Task GitlabSourcePrereleaseFiltering()
    {
        await DockerServices.SkipUnlessGitLabUpAsync();
        using var logger = _output.BuildLoggerFor<GitLabSourceTests>();
        var fixture = InstalledAppFixture.GetOrCreate(logger);

        await using var project = await GitLabTestProject.CreateAsync(logger, "srcpre");
        await CreateGitLabReleaseFromFeedAsync(project, fixture, logger, upcoming: true);
        var apiUrl = $"{DockerServices.GitLabBaseUrl}/api/v4/projects/{project.Project.Id}";

        var locator = fixture.CreateCSharpLocator(out var rootDir, logger.ToVelopackLogger());
        try {
            var options = new UpdateOptions { ExplicitChannel = InstalledAppFixture.Channel, AllowVersionDowngrade = false };

            var stableOnly = new GitlabSource(apiUrl, project.Token, upcomingRelease: false);
            var hidden = await new UpdateManager(stableOnly, options, locator).CheckForUpdatesAsync();
            Assert.Null(hidden);

            var withPre = new GitlabSource(apiUrl, project.Token, upcomingRelease: true);
            var visible = await new UpdateManager(withPre, options, locator).CheckForUpdatesAsync();
            Assert.NotNull(visible);
            Assert.Equal(InstalledAppFixture.LatestVersion, visible.TargetFullRelease.Version?.ToString());
        } finally {
            SourceTestHelpers.TryDelete(rootDir);
        }
    }

    /// <summary>
    /// Publishes a gitlab release with the fixture's nupkgs and the releases.stable.json feed file
    /// attached as asset links (backed by the generic package registry).
    /// </summary>
    private static async Task CreateGitLabReleaseFromFeedAsync(GitLabTestProject project, InstalledAppFixture fixture, ILogger logger, bool upcoming)
    {
        var feedFileName = $"releases.{InstalledAppFixture.Channel}.json";
        var assets = Directory.EnumerateFiles(fixture.FeedDir)
            .Where(f => f.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)
                || String.Equals(Path.GetFileName(f), feedFileName, StringComparison.OrdinalIgnoreCase))
            .Select(f => (Path.GetFileName(f), File.ReadAllBytes(f)))
            .ToList();
        Assert.NotEmpty(assets);

        var tag = "v" + InstalledAppFixture.LatestVersion;
        await GitLabAdmin.CreateReleaseWithAssetsAsync(project.Token, project.Project, tag, tag, assets, logger, upcomingRelease: upcoming);
    }
}

/// <summary>
/// Live GitHub source tests across all five client languages. All rows share ONE leased test repo
/// (see <see cref="GithubLiveContext"/>): the lease is acquired lazily by the first row that runs, the
/// fixture feed is published to it once, and xunit's class-fixture disposal releases the lease after
/// the last row finishes — even when rows fail. This class and <c>GitHubDeploymentTests</c> both consume
/// the shared 5-repo pool, so they share the <c>github</c> collection and never run concurrently.
/// </summary>
[Collection("github")]
public class GithubSourceLiveTests : IClassFixture<GithubLiveContext>
{
    private readonly ITestOutputHelper _output;
    private readonly GithubLiveContext _context;

    public GithubSourceLiveTests(ITestOutputHelper output, GithubLiveContext context)
    {
        _output = output;
        _context = context;
    }

    [Theory]
    [InlineData(HarnessLang.CSharp)]
    [InlineData(HarnessLang.Rust)]
    [InlineData(HarnessLang.Cpp)]
    [InlineData(HarnessLang.Python)]
    [InlineData(HarnessLang.NodeJs)]
    public async Task GithubSourceLive(HarnessLang lang)
    {
        Assert.SkipWhen(
            String.IsNullOrWhiteSpace(DeploymentTestEnv.GetGitHubToken()),
            $"{DeploymentTestEnv.GitHubTokenVar} is not set.");
        using var logger = _output.BuildLoggerFor<GithubSourceLiveTests>();
        await HarnessRunner.SkipUnlessAvailableAsync(lang, logger);
        var fixture = InstalledAppFixture.GetOrCreate(logger);

        var lease = await _context.GetOrCreateAsync(fixture, logger);

        // Live GitHub's list APIs are eventually consistent per-request: even after the shared context
        // has confirmed the feed is visible, a row's own fresh API calls can hit a stale replica
        // (observed as 404s and "asset not found in any release" from otherwise-correct clients).
        for (var attempt = 1; ; attempt++) {
            try {
                await SourceTestHelpers.RunRowAsync(lang, "github", lease.RepoUrl, lease.Token, prerelease: false, fixture, logger);
                break;
            } catch (Exception ex) when (attempt < 3) {
                logger.LogWarning(ex, "GitHub source row attempt {Attempt} failed (likely list-after-write lag), retrying in 15s", attempt);
                await Task.Delay(TimeSpan.FromSeconds(15));
            }
        }
    }
}

/// <summary>
/// Class-shared lazy context for <see cref="GithubSourceLiveTests"/>. On first use it acquires an
/// exclusive <see cref="GitHubRepoLease"/> over one of the live test repos (which resets it to a
/// pristine state) and publishes the <see cref="InstalledAppFixture"/> feed (1.0.0 + 2.0.0, channel
/// 'stable') to it in-process via <see cref="GitHubUploadCommandRunner"/>. Initialization happens at
/// most once per test-class run (later rows await the same task, including its failure); the lease is
/// released when xunit disposes the fixture after the class's last row.
/// </summary>
public sealed class GithubLiveContext : IAsyncLifetime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task<GitHubRepoLease>? _init;

    /// <summary> Returns the shared lease (with the feed already published), initializing it on first call. </summary>
    public async Task<GitHubRepoLease> GetOrCreateAsync(InstalledAppFixture fixture, ILogger log)
    {
        await _gate.WaitAsync();
        try {
            _init ??= AcquireAndUploadAsync(fixture, log);
        } finally {
            _gate.Release();
        }

        return await _init;
    }

    private static async Task<GitHubRepoLease> AcquireAndUploadAsync(InstalledAppFixture fixture, ILogger log)
    {
        // The lease outlives the row that created it (it is released on class-fixture disposal, when
        // xunit has no active test and the per-test output logger throws), so give it a safe wrapper.
        var lease = await GitHubRepoLock.AcquireAsync(new PostTestSafeLogger(log));
        try {
            log.LogInformation("Publishing {AppId} feed to leased repo {RepoUrl}", InstalledAppFixture.AppId, lease.RepoUrl);
            await new GitHubUploadCommandRunner(log).Run(new GitHubUploadOptions {
                RepoUrl = lease.RepoUrl,
                Token = lease.Token,
                ReleaseDir = new DirectoryInfo(fixture.FeedDir),
                Channel = InstalledAppFixture.Channel,
                Publish = true,
            });
            await WaitForFeedVisibleAsync(lease, log);
            return lease;
        } catch {
            // Don't hold the lock for the full stale window if the upload fails; every row will
            // observe this faulted init task and fail with the same underlying error.
            await lease.DisposeAsync();
            throw;
        }
    }

    /// <summary>
    /// GitHub's Releases API is eventually consistent (list-after-write lag): a release published a
    /// moment ago can be missing from listings for several seconds, which made early theory rows fail
    /// with "no update available". Poll the same feed the rows will query until the latest full asset
    /// is visible; throw on timeout so every row fails with a diagnosable cause instead.
    /// </summary>
    private static async Task WaitForFeedVisibleAsync(GitHubRepoLease lease, ILogger log)
    {
        var source = new GithubSource(lease.RepoUrl, lease.Token, prerelease: false);
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(90);
        while (true) {
            var feed = await source.GetReleaseFeed(log.ToVelopackLogger(), null, InstalledAppFixture.Channel);
            if (feed.Assets.Any(a => a.Type == VelopackAssetType.Full && a.Version?.ToString() == InstalledAppFixture.LatestVersion)) {
                return;
            }

            if (DateTime.UtcNow >= deadline) {
                var seen = String.Join(", ", feed.Assets.Select(a => $"{a.Type} {a.Version}"));
                throw new TimeoutException(
                    $"The {InstalledAppFixture.LatestVersion} full release did not become visible at {lease.RepoUrl} within 90s. Feed contains: [{seen}]");
            }

            log.LogInformation("Feed at {RepoUrl} does not show {Version} yet, waiting...", lease.RepoUrl, InstalledAppFixture.LatestVersion);
            await Task.Delay(2000);
        }
    }

    /// <inheritdoc />
    public ValueTask InitializeAsync() => default;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        // Release the lease if (and only if) it was successfully acquired; a faulted init task has
        // already disposed it, and no init at all (token missing / rows skipped) means nothing to do.
        if (_init is { IsCompletedSuccessfully: true })
            await (await _init).DisposeAsync();
    }

    /// <summary>
    /// Forwards to a per-test output logger but swallows logging failures. Neovolve's test-output
    /// loggers throw once the test that owns them has finished, and the lease held by this context
    /// logs during its release, which happens after the last row of the class.
    /// </summary>
    private sealed class PostTestSafeLogger(ILogger inner) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            try {
                inner.Log(logLevel, eventId, state, exception, formatter);
            } catch {
                // The owning test is no longer active; drop the message.
            }
        }
    }
}
