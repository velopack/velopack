namespace Velopack.Deployment.Tests;

/// <summary>
/// Runs the shared <see cref="GitReleaseDeploymentSuite"/> against the live GitHub test repo pool
/// (caesay/velopack-test-{1..5}). Each test serially acquires an exclusive repo lease (the assembly has test
/// parallelization disabled), which resets the repo to a pristine state on acquire and releases the lock on
/// dispose. Tests skip when the token env var is not set.
/// </summary>
public class GitHubDeploymentTests(ITestOutputHelper output) : GitReleaseDeploymentSuite(output)
{
    protected override Task SkipUnlessReadyAsync()
    {
        Assert.SkipWhen(
            DeploymentTestEnv.GetGitHubToken() == null,
            $"{DeploymentTestEnv.GitHubTokenVar} is not set.");
        return Task.CompletedTask;
    }

    protected override async Task<IGitReleaseScope> CreateScopeAsync(ILogger log)
        => new GitHubGitReleaseScope(await GitHubRepoLock.AcquireAsync(log));
}
