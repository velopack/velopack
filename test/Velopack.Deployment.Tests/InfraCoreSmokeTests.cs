using System.Net;
using Neovolve.Logging.Xunit;
using Octokit;
using Xunit;

namespace Velopack.Deployment.Tests;

/// <summary>
/// Permanent smoke tests over the core test infrastructure: the static file server, the packed
/// source-app feed fixture, and the GitHub repo lock.
/// </summary>
public class InfraCoreSmokeTests
{
    private readonly ITestOutputHelper _output;

    public InfraCoreSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task StaticFileServerServesAndRecords()
    {
        using var logger = _output.BuildLoggerFor<InfraCoreSmokeTests>();
        var dir = Path.Combine(Path.GetTempPath(), "velopack-staticfile-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try {
            var payload = new byte[] { 1, 2, 3, 4, 5, 250, 251, 252 };
            File.WriteAllBytes(Path.Combine(dir, "test.nupkg"), payload);
            File.WriteAllText(Path.Combine(dir, "releases.stable.json"), "{\"Assets\":[]}");

            using var server = new StaticFileServer(dir, logger);
            using var http = new HttpClient();
            http.DefaultRequestHeaders.Add("X-Velopack-Test", "hello-123");

            var bytes = await http.GetByteArrayAsync(server.BaseUrl + "test.nupkg");
            Assert.Equal(payload, bytes);

            var jsonResp = await http.GetAsync(server.BaseUrl + "releases.stable.json");
            Assert.Equal(HttpStatusCode.OK, jsonResp.StatusCode);
            Assert.Equal("application/json", jsonResp.Content.Headers.ContentType?.MediaType);

            var missing = await http.GetAsync(server.BaseUrl + "does-not-exist.nupkg");
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);

            var recorded = server.Requests.ToArray();
            var rec = recorded.First(r => r.PathAndQuery.Contains("test.nupkg"));
            Assert.True(rec.Headers.TryGetValue("X-Velopack-Test", out var hv));
            Assert.Equal("hello-123", hv);
        } finally {
            try { Directory.Delete(dir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public void InstalledAppFixturePacksFeed()
    {
        using var logger = _output.BuildLoggerFor<InfraCoreSmokeTests>();
        var fixture = InstalledAppFixture.GetOrCreate(logger);

        var indexPath = Path.Combine(fixture.FeedDir, "releases.stable.json");
        Assert.True(File.Exists(indexPath), "releases.stable.json was not produced");

        var feed = VelopackAssetFeed.FromJson(File.ReadAllText(indexPath));
        var fullAssets = feed.Assets.Where(a => a.Type == VelopackAssetType.Full).ToArray();
        Assert.Equal(2, fullAssets.Length);
        Assert.Contains(fullAssets, a => a.Version?.ToString() == InstalledAppFixture.InstalledVersion);
        Assert.Contains(fullAssets, a => a.Version?.ToString() == InstalledAppFixture.LatestVersion);

        Assert.False(String.IsNullOrWhiteSpace(fixture.Sha256OfLatestFullPackage));

        var spec = fixture.CreateInstalledLayout(out var rootDir);
        try {
            Assert.True(File.Exists(spec.ManifestPath), "sq.version was not extracted");
            Assert.True(File.Exists(spec.UpdateExePath), "dummy update exe was not created");
            Assert.True(Directory.Exists(spec.PackagesDir), "packages dir was not created");
            Assert.True(Directory.Exists(spec.CurrentBinaryDir), "current dir was not created");
            Assert.True(spec.IsPortable);

            var manifest = File.ReadAllText(spec.ManifestPath);
            Assert.Contains(InstalledAppFixture.AppId, manifest);

            var json = spec.ToJson();
            Assert.Contains("RootAppDir", json);
            Assert.Contains("ManifestPath", json);
            Assert.Contains("IsPortable", json);
        } finally {
            try { Directory.Delete(rootDir, true); } catch { /* ignore */ }
        }
    }

    [Fact]
    public async Task GitHubLockAcquireRelease()
    {
        Assert.SkipWhen(String.IsNullOrWhiteSpace(DeploymentTestEnv.GetGitHubToken()), $"{DeploymentTestEnv.GitHubTokenVar} is not set.");
        using var logger = _output.BuildLoggerFor<InfraCoreSmokeTests>();

        await using var lease = await GitHubRepoLock.AcquireAsync(logger, TimeSpan.FromMinutes(2));
        logger.LogInformation("Test acquired lock on repo {Repo}", lease.Name);

        // The Contents API is eventually consistent in both directions: a read straight after
        // CreateFile can 404 on a stale replica (just as a read after DeleteFile can still return
        // the deleted file below), so poll until the just-created lock file is visible.
        IReadOnlyList<Octokit.RepositoryContent> contents;
        var appearDeadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true) {
            try {
                contents = await lease.Client.Repository.Content.GetAllContents(lease.Owner, lease.Name, GitHubRepoLock.LockFileName);
                break;
            } catch (NotFoundException) when (DateTime.UtcNow < appearDeadline) {
                await Task.Delay(TimeSpan.FromSeconds(1));
            }
        }

        Assert.Single(contents);

        await lease.DisposeAsync();

        // GitHub's Contents API is eventually consistent: a read immediately after DeleteFile can still
        // return the just-deleted (cached) file, so poll until it reports the lock file is gone.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(30);
        while (true) {
            try {
                await lease.Client.Repository.Content.GetAllContents(lease.Owner, lease.Name, GitHubRepoLock.LockFileName);
            } catch (NotFoundException) {
                break;
            }

            Assert.False(DateTime.UtcNow >= deadline, "Lock file was still present 30s after dispose.");
            await Task.Delay(TimeSpan.FromSeconds(1));
        }
    }
}
