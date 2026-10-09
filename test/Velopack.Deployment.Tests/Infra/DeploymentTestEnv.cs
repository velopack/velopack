namespace Velopack.Deployment.Tests;

public static class DeploymentTestEnv
{
    public const string GitHubTokenVar = "VELOPACK_DEPLOYMENT_TEST_TOKEN";

    /// <summary>
    /// Reads the GitHub deployment test token. On Windows also checks the User-level
    /// environment, because test runners often inherit a stale process environment.
    /// </summary>
    public static string? GetGitHubToken()
    {
        var value = Environment.GetEnvironmentVariable(GitHubTokenVar);
        if (!String.IsNullOrWhiteSpace(value))
            return value;

        if (OperatingSystem.IsWindows()) {
            value = Environment.GetEnvironmentVariable(GitHubTokenVar, EnvironmentVariableTarget.User);
            if (!String.IsNullOrWhiteSpace(value))
                return value;
        }

        return null;
    }

    /// <summary>
    /// Live GitHub tests are temporarily disabled: their lock-pool polling shares the CI token's API rate limit
    /// and starves concurrent runs. Flip this back once they use their own token / cheaper locking.
    /// </summary>
    public const bool GitHubTestsDisabled = true;

    /// <summary> Skips the current test unless live GitHub tests are enabled and a token is configured. </summary>
    public static void SkipUnlessGitHubAvailable()
    {
        Assert.SkipWhen(GitHubTestsDisabled, "Live GitHub deployment tests are temporarily disabled.");
        Assert.SkipWhen(GetGitHubToken() == null, $"{GitHubTokenVar} is not set.");
    }
}
