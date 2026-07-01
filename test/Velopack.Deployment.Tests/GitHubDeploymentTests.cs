namespace Velopack.Deployment.Tests;

/// <summary>
/// Runs the shared <see cref="GitReleaseDeploymentSuite"/> against the live GitHub test repo pool
/// (caesay/velopack-test-{1..5}). Each test serially acquires an exclusive repo lease (the assembly has test
/// parallelization disabled), which resets the repo to a pristine state on acquire and releases the lock on
/// dispose. Tests skip when the token env var is not set.
/// </summary>
public class GitHubDeploymentTests(ITestOutputHelper output) : GitReleaseDeploymentSuite(output)
{
    // The 5-repo pool is shared by all CI legs; recreating a just-deleted tag name (every test would
    // otherwise tag '1.0.0') races GitHub's eventual consistency and fails with 'Validation Failed'.
    protected override bool UseUniqueTags => true;

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
