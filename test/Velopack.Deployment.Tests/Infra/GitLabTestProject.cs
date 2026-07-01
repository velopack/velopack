using Velopack.Core;

namespace Velopack.Deployment.Tests;

/// <summary>
/// A disposable GitLab test project: ensures the PAT is seeded, creates a fresh public project, and deletes
/// it (best-effort) on dispose. Mirrors <see cref="GitHubRepoLease"/> so source/deployment suites need no
/// try/finally boilerplate — just <c>await using var project = await GitLabTestProject.CreateAsync(log);</c>.
/// </summary>
public sealed class GitLabTestProject : IAsyncDisposable
{
    /// <summary> The seeded personal access token used to create and later delete the project. </summary>
    public string Token { get; }

    /// <summary> The created project. </summary>
    public GitLabProject Project { get; }

    private readonly ILogger _logger;
    private bool _disposed;

    private GitLabTestProject(string token, GitLabProject project, ILogger logger)
    {
        Token = token;
        Project = project;
        _logger = logger;
    }

    /// <summary>Ensures seeding and creates a public project named <c>{prefix ?? "test"}-{random8}</c>.</summary>
    public static async Task<GitLabTestProject> CreateAsync(ILogger logger, string? namePrefix = null)
    {
        var token = await GitLabAdmin.EnsureSeededAsync(logger).ConfigureAwait(false);
        var name = $"{namePrefix ?? "test"}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var project = await GitLabAdmin.CreateProjectAsync(token, name, logger).ConfigureAwait(false);
        return new GitLabTestProject(token, project, logger);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await GitLabAdmin.DeleteProjectAsync(Token, Project.Id, _logger).ConfigureAwait(false);
    }
}
