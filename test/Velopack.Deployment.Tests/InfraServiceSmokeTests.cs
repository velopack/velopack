using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using Velopack.Core;
using Velopack.TestCommon;

namespace Velopack.Deployment.Tests;

/// <summary>
/// Permanent smoke tests that exercise the docker service-administration infra (DockerServices, GiteaAdmin,
/// GitLabAdmin) against the local stack. Every test skips (not fails) when its backing service is not running,
/// so the suite is safe to run without docker.
/// </summary>
public class InfraServiceSmokeTests
{
    private readonly ITestOutputHelper _output;

    public InfraServiceSmokeTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>The name of every Gitea server, so each is exercised (and skipped) independently.</summary>
    public static TheoryData<string> GiteaServerNames()
    {
        var data = new TheoryData<string>();
        foreach (var server in DockerServices.GiteaServers)
            data.Add(server.Name);
        return data;
    }

    [Theory]
    [MemberData(nameof(GiteaServerNames))]
    public async Task GiteaSeedsAndCreatesRepo(string serverName)
    {
        using var logger = _output.BuildLoggerFor<InfraServiceSmokeTests>();
        var server = DockerServices.GiteaServers.Single(s => s.Name == serverName);
        await DockerServices.SkipUnlessGiteaUpAsync(server);

        var ctx = await GiteaAdmin.EnsureSeededAsync(server, logger);
        var repoName = "smoke-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var repo = await GiteaAdmin.CreateRepoAsync(ctx, repoName, logger);
        try {
            var fetched = await GiteaAdmin.TryGetRepoAsync(ctx, repo.Owner, repo.Name);
            Assert.NotNull(fetched);
            Assert.Equal(repo.Owner, fetched!.Owner);
            Assert.Equal(repo.Name, fetched.Name);
            Assert.Equal(GiteaAdmin.AdminUsername, repo.Owner);
        } finally {
            await GiteaAdmin.DeleteRepoAsync(ctx, repo.Owner, repo.Name, logger);
        }
    }

    [Theory]
    [MemberData(nameof(GiteaServerNames))]
    public async Task GiteaTestRepoWrapperCreatesAndDeletes(string serverName)
    {
        using var logger = _output.BuildLoggerFor<InfraServiceSmokeTests>();
        var server = DockerServices.GiteaServers.Single(s => s.Name == serverName);
        await DockerServices.SkipUnlessGiteaUpAsync(server);

        GiteaContext ctx;
        string owner, name;
        await using (var repo = await GiteaTestRepo.CreateAsync(server, logger, "wrapper")) {
            ctx = repo.Context;
            owner = repo.Repo.Owner;
            name = repo.Repo.Name;
            Assert.False(String.IsNullOrWhiteSpace(repo.Token), "wrapper should expose a token");
            Assert.Contains(name, repo.HttpUrl);
            Assert.NotNull(await GiteaAdmin.TryGetRepoAsync(ctx, owner, name));
        }

        // Dispose must have deleted the repo (Gitea deletes synchronously).
        Assert.Null(await GiteaAdmin.TryGetRepoAsync(ctx, owner, name));
    }

    [Fact]
    public async Task GitLabSeedsAndCreatesRelease()
    {
        await DockerServices.SkipUnlessGitLabUpAsync();
        using var logger = _output.BuildLoggerFor<InfraServiceSmokeTests>();

        var token = await GitLabAdmin.EnsureSeededAsync(logger);
        var projectName = "smoke-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var project = await GitLabAdmin.CreateProjectAsync(token, projectName, logger);
        try {
            var assetName = "smoke-asset.txt";
            var bytes = Encoding.UTF8.GetBytes("hello-velopack-smoke-asset");
            await GitLabAdmin.CreateReleaseWithAssetsAsync(
                token, project, "v1.0.0", "v1.0.0",
                new[] { (assetName, bytes) }, logger);

            var release = await GitLabAdmin.GetReleaseAsync(token, project.Id, "v1.0.0");
            Assert.False(release.UpcomingRelease, "Release should be published (not upcoming).");

            var link = release.Links.Single(l => l.Name == assetName);
            Assert.False(String.IsNullOrWhiteSpace(link.DirectAssetUrl), "direct_asset_url should be set.");

            // Public project: the direct asset URL must resolve unauthenticated and return the raw bytes.
            using var req = new HttpRequestMessage(HttpMethod.Get, link.DirectAssetUrl);
            using var resp = await DockerServices.Http.SendAsync(req);
            Assert.True(resp.IsSuccessStatusCode, $"GET {link.DirectAssetUrl} returned {(int) resp.StatusCode}.");
            var downloaded = await resp.Content.ReadAsByteArrayAsync();
            Assert.Equal(bytes, downloaded);
        } finally {
            await GitLabAdmin.DeleteProjectAsync(token, project.Id, logger);
        }
    }

    [Fact]
    public async Task GitLabTestProjectWrapperCreatesAndDeletes()
    {
        await DockerServices.SkipUnlessGitLabUpAsync();
        using var logger = _output.BuildLoggerFor<InfraServiceSmokeTests>();

        string token;
        long projectId;
        await using (var project = await GitLabTestProject.CreateAsync(logger, "wrapper")) {
            token = project.Token;
            projectId = project.Project.Id;
            Assert.False(String.IsNullOrWhiteSpace(project.Token), "wrapper should expose a token");
            Assert.False(await IsGitLabProjectGoneAsync(token, projectId), "project should exist before dispose");
        }

        // Dispose must have deleted the project. GitLab deletion is asynchronous, so poll for it.
        Assert.True(
            await WaitForGitLabProjectGoneAsync(token, projectId, TimeSpan.FromSeconds(60)),
            "project should be gone (404 or marked for deletion) after dispose");
    }

    [Fact]
    public async Task AzuriteIsReachable()
    {
        await DockerServices.SkipUnlessAzuriteUpAsync();
        Assert.True(await DockerServices.IsAzuriteUpAsync());
    }

    [Fact]
    public async Task S3MockIsReachable()
    {
        await DockerServices.SkipUnlessS3MockUpAsync();
        Assert.True(await DockerServices.IsS3MockUpAsync());
    }

    private static async Task<bool> WaitForGitLabProjectGoneAsync(string token, long projectId, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (true) {
            if (await IsGitLabProjectGoneAsync(token, projectId))
                return true;
            if (DateTime.UtcNow >= deadline)
                return false;
            await Task.Delay(TimeSpan.FromSeconds(2));
        }
    }

    /// <summary>True once GitLab no longer serves the project: 404, or 200 with a non-null marked_for_deletion_at.</summary>
    private static async Task<bool> IsGitLabProjectGoneAsync(string token, long projectId)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{GitLabAdmin.ApiBase}/projects/{projectId}");
        req.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
        using var resp = await DockerServices.Http.SendAsync(req);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return true;
        if (!resp.IsSuccessStatusCode)
            return false;
        var body = await resp.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        return doc.RootElement.TryGetProperty("marked_for_deletion_at", out var m) && m.ValueKind != JsonValueKind.Null;
    }
}
