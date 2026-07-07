using Velopack.Core;

namespace Velopack.Deployment.Tests;

/// <summary>
/// A disposable Gitea test repository: seeds the server, creates a fresh randomly-named repo, and deletes
/// it (best-effort) on dispose. Mirrors <see cref="GitHubRepoLease"/> so source/deployment suites need no
/// try/finally boilerplate — just <c>await using var repo = await GiteaTestRepo.CreateAsync(server, log);</c>.
/// </summary>
public sealed class GiteaTestRepo : IAsyncDisposable
{
    /// <summary> The seeded admin session used to create and later delete the repo. </summary>
    public GiteaContext Context { get; }

    /// <summary> The created repository. </summary>
    public GiteaRepo Repo { get; }

    /// <summary> The admin API token (from <see cref="Context"/>). </summary>
    public string Token => Context.Token;

    /// <summary> The repo URL suitable for <c>GiteaUploadOptions.RepoUrl</c> / <c>GiteaSource</c>. </summary>
    public string HttpUrl => Repo.HtmlUrl;

    private readonly ILogger _logger;
    private bool _disposed;

    private GiteaTestRepo(GiteaContext context, GiteaRepo repo, ILogger logger)
    {
        Context = context;
        Repo = repo;
        _logger = logger;
    }

    /// <summary>Seeds <paramref name="server"/> and creates a repo named <c>{prefix ?? "test"}-{random8}</c>.</summary>
    public static async Task<GiteaTestRepo> CreateAsync(GiteaServer server, ILogger logger, string? namePrefix = null)
    {
        var ctx = await GiteaAdmin.EnsureSeededAsync(server, logger).ConfigureAwait(false);
        var name = $"{namePrefix ?? "test"}-{Guid.NewGuid().ToString("N").Substring(0, 8)}";
        var repo = await GiteaAdmin.CreateRepoAsync(ctx, name, logger).ConfigureAwait(false);
        return new GiteaTestRepo(ctx, repo, logger);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;
        await GiteaAdmin.DeleteRepoAsync(Context, Repo.Owner, Repo.Name, _logger).ConfigureAwait(false);
    }
}
