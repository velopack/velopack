namespace Velopack.Deployment.Tests;

/// <summary>
/// Runs the shared <see cref="GitReleaseDeploymentSuite"/> against one of the local docker Gitea servers.
/// A fresh randomly-named repository is created per test (and deleted afterwards), so every test starts pristine.
/// </summary>
public abstract class GiteaDeploymentSuite : GitReleaseDeploymentSuite
{
    private readonly GiteaServer _server;

    protected GiteaDeploymentSuite(ITestOutputHelper output, string serverName) : base(output)
    {
        _server = DockerServices.GiteaServers.Single(s => s.Name == serverName);
    }

    protected override Task SkipUnlessReadyAsync() => DockerServices.SkipUnlessGiteaUpAsync(_server);

    protected override async Task<IGitReleaseScope> CreateScopeAsync(ILogger log)
        => new GiteaGitReleaseScope(await GiteaTestRepo.CreateAsync(_server, log, "gitrel"));
}

[Collection("gitea-1.22")]
public class Gitea122DeploymentTests(ITestOutputHelper output) : GiteaDeploymentSuite(output, "gitea-1.22");

[Collection("gitea-1.24")]
public class Gitea124DeploymentTests(ITestOutputHelper output) : GiteaDeploymentSuite(output, "gitea-1.24");

[Collection("gitea-latest")]
public class GiteaLatestDeploymentTests(ITestOutputHelper output) : GiteaDeploymentSuite(output, "gitea-latest");
