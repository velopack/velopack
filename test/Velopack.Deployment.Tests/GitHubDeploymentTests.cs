namespace Velopack.Deployment.Tests;

/// <summary>
/// Runs the shared <see cref="GitReleaseDeploymentSuite"/> against the live GitHub test repo pool
/// (caesay/velopack-test-{1..5}). The whole collection shares one exclusive repo lease
/// (<see cref="GitHubSharedLeaseFixture"/>, released after the last test) and each test resets the repo to a pristine
/// state first; tests within the collection run serially. Tests skip when the token env var is not set.
/// </summary>
[Collection("github")]
public class GitHubDeploymentTests(ITestOutputHelper output, GitHubSharedLeaseFixture leaseFixture) : GitReleaseDeploymentSuite(output)
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
    {
        var lease = await leaseFixture.GetAsync(log);
        await lease.ResetAsync();
        return new GitHubGitReleaseScope(lease, ownsLease: false);
    }
}
