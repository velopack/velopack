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
}
