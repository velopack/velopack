using System.Text.Json;
using Octokit;

namespace Velopack.Deployment.Tests;

/// <summary>
/// Acquires an exclusive lease over one of the shared live GitHub test repositories
/// (<c>caesay/velopack-test-{1..5}</c>) by atomically creating a lock file via the Contents API.
/// Creating a file without a sha fails with HTTP 422 if it already exists, which makes creation an
/// atomic mutex. The lease resets the repo to a pristine state on acquire and deletes the lock file
/// on dispose.
/// </summary>
public static class GitHubRepoLock
{
    /// <summary> The lock marker file created at the root of a repo to signal exclusive ownership. </summary>
    public const string LockFileName = ".velopack-test-lock";

    private const string Owner = "caesay";
    private const int RepoCount = 5;
    private static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(20);
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan SecondaryRateLimitBackoff = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Acquires a lease over the first available test repo, shuffling repo order to spread load and
    /// polling every ~30s until one is free or <paramref name="timeout"/> (default 10 min) elapses.
    /// Rate limiting is not treated as "busy": it waits for the limit to reset, or fails fast if the
    /// reset is past the deadline, instead of burning more of the (shared) token's quota by polling.
    /// </summary>
    public static async Task<GitHubRepoLease> AcquireAsync(ILogger log, TimeSpan? timeout = null)
    {
        var token = DeploymentTestEnv.GetGitHubToken()
            ?? throw new InvalidOperationException($"{DeploymentTestEnv.GitHubTokenVar} is not set.");
        var deadline = DateTime.UtcNow + (timeout ?? TimeSpan.FromMinutes(10));
        var attempt = 0;

        while (true) {
            attempt++;
            try {
                var lease = await TryAcquireAnyAsync(token, attempt, log);
                if (lease != null)
                    return lease;
            } catch (RateLimitExceededException ex) {
                var resetUtc = ex.Reset.UtcDateTime + TimeSpan.FromSeconds(5);
                if (resetUtc >= deadline)
                    throw new TimeoutException($"GitHub API rate limit exhausted until {resetUtc:o}, past the lock timeout.", ex);
                log.LogWarning("GitHub API rate limit exhausted, waiting until {ResetUtc:o} before retry", resetUtc);
                await Task.Delay(resetUtc - DateTime.UtcNow);
                continue;
            } catch (SecondaryRateLimitExceededException ex) {
                if (DateTime.UtcNow + SecondaryRateLimitBackoff >= deadline)
                    throw new TimeoutException("GitHub secondary rate limit hit, no time left before the lock timeout.", ex);
                log.LogWarning("GitHub secondary rate limit hit, waiting {Seconds}s before retry", SecondaryRateLimitBackoff.TotalSeconds);
                await Task.Delay(SecondaryRateLimitBackoff);
                continue;
            }

            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException($"Could not acquire any of the {RepoCount} GitHub test repos within the timeout.");

            var delay = PollInterval + TimeSpan.FromSeconds(Random.Shared.Next(0, 10));
            log.LogInformation("All {Count} GitHub test repos busy, waiting {Seconds}s before retry", RepoCount, delay.TotalSeconds);
            await Task.Delay(delay);
        }
    }

    private static async Task<GitHubRepoLease?> TryAcquireAnyAsync(string token, int attempt, ILogger log)
    {
        var order = Enumerable.Range(1, RepoCount).OrderBy(_ => Random.Shared.Next()).ToArray();
        foreach (var n in order) {
            var name = $"velopack-test-{n}";
            var repoUrl = $"https://github.com/{Owner}/{name}";
            var client = CreateClient(token);
            log.LogInformation("GitHub lock attempt {Attempt}: trying repo {Repo}", attempt, name);
            var lockSha = await TryLockRepoAsync(client, name, log);
            if (lockSha != null) {
                var lease = new GitHubRepoLease(repoUrl, Owner, name, token, client, log, lockSha);
                try {
                    await lease.ResetAsync();
                } catch (Exception ex) {
                    // Don't leak the freshly-created lock for the full stale window if reset fails.
                    log.LogWarning(ex, "Reset failed after acquiring lock on {Repo}, releasing", name);
                    await lease.DisposeAsync();
                    throw;
                }
                log.LogInformation("Acquired GitHub lock on repo {Repo}", name);
                return lease;
            }
        }

        return null;
    }

    internal static GitHubClient CreateClient(string token)
    {
        return new GitHubClient(new ProductHeaderValue("Velopack-DeploymentTests")) {
            Credentials = new Credentials(token),
        };
    }

    /// <summary>
    /// Attempts to create the lock file on the repo, returning the created content sha on success (used to
    /// safely delete exactly our own lock on release), or null if the repo is already locked. Reads the lock
    /// file first so a busy repo costs one API call. Rate-limit and credential errors propagate to the caller.
    /// </summary>
    private static async Task<string?> TryLockRepoAsync(GitHubClient client, string name, ILogger log)
    {
        try {
            var existing = (await client.Repository.Content.GetAllContents(Owner, name, LockFileName)).FirstOrDefault();
            if (existing != null) {
                var acquired = TryParseAcquiredUtc(existing.Content);
                if (acquired == null || DateTime.UtcNow - acquired.Value <= StaleAfter)
                    return null;

                log.LogInformation("Lock on {Repo} is stale (acquired {AcquiredUtc:o}), reclaiming", name, acquired.Value);
                await client.Repository.Content.DeleteFile(Owner, name, LockFileName,
                    new DeleteFileRequest("reclaim stale velopack test lock", existing.Sha));
            }
        } catch (NotFoundException) {
            // No lock file (or it was just deleted): the repo looks free, try to take it.
        } catch (Exception ex) when (!IsFatal(ex)) {
            log.LogWarning(ex, "Error inspecting existing lock on {Repo}", name);
            return null;
        }

        try {
            // Creating without a sha fails with 422 if the file exists, so only one acquirer can win, even if
            // the read above came from a stale replica.
            var created = await client.Repository.Content.CreateFile(Owner, name, LockFileName,
                new CreateFileRequest("acquire velopack test lock", BuildLockContent()));
            return created.Content.Sha;
        } catch (ApiValidationException) {
            // 422 => another acquirer holds it.
            return null;
        } catch (Exception ex) when (!IsFatal(ex)) {
            log.LogWarning(ex, "Unexpected error creating lock on {Repo}", name);
            return null;
        }
    }

    /// <summary>
    /// Errors that retrying cannot fix by waiting for a repo to free up: rate limits (handled by the caller) and
    /// bad credentials (fail immediately rather than spin until the timeout).
    /// </summary>
    private static bool IsFatal(Exception ex)
        => ex is RateLimitExceededException or SecondaryRateLimitExceededException or AuthorizationException;

    private static string BuildLockContent()
    {
        var owner = $"{Environment.MachineName}/{Environment.ProcessId}/{Guid.NewGuid()}";
        return JsonSerializer.Serialize(new {
            owner,
            acquiredUtc = DateTime.UtcNow.ToString("o"),
        });
    }

    private static DateTime? TryParseAcquiredUtc(string? json)
    {
        if (String.IsNullOrWhiteSpace(json))
            return null;
        try {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("acquiredUtc", out var el) && el.ValueKind == JsonValueKind.String) {
                if (DateTime.TryParse(el.GetString(), null, System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
                    return dt.ToUniversalTime();
            }
        } catch (JsonException) {
            // Corrupt lock content — treat as unknown age (never auto-reclaimed here).
        }

        return null;
    }
}

/// <summary>
/// An exclusive lease over one GitHub test repository. Always dispose (pass or fail) to release the
/// lock. Disposal only removes the lock file; repo artifacts are left in place for post-mortem and the
/// next acquirer resets them.
/// </summary>
public sealed class GitHubRepoLease : IAsyncDisposable, IDisposable
{
    /// <summary> The <c>https://github.com/{owner}/{name}</c> URL of the leased repo. </summary>
    public string RepoUrl { get; }

    /// <summary> The repo owner (organisation or user). </summary>
    public string Owner { get; }

    /// <summary> The repo name. </summary>
    public string Name { get; }

    /// <summary> The GitHub token used to authenticate. </summary>
    public string Token { get; }

    /// <summary> The authenticated Octokit client bound to this lease. </summary>
    public GitHubClient Client { get; }

    private readonly ILogger _log;
    private readonly string _lockSha;
    private bool _disposed;

    internal GitHubRepoLease(string repoUrl, string owner, string name, string token, GitHubClient client, ILogger log, string lockSha)
    {
        RepoUrl = repoUrl;
        Owner = owner;
        Name = name;
        Token = token;
        Client = client;
        _log = log;
        _lockSha = lockSha;
    }

    /// <summary>
    /// Resets the repo to a pristine state: deletes every release, then every tag ref. Never touches
    /// branches (including the default branch).
    /// </summary>
    internal async Task ResetAsync()
    {
        // The Releases API is eventually consistent: a single list-then-delete pass can miss releases
        // created moments ago by the previous lease holder. Loop until a listing comes back empty.
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(60);
        var deletedReleases = 0;
        var deletedTags = 0;
        while (true) {
            var releases = await Client.Repository.Release.GetAll(Owner, Name);
            foreach (var r in releases) {
                try {
                    await Client.Repository.Release.Delete(Owner, Name, r.Id);
                } catch (NotFoundException) {
                    // already gone — the release listing was stale
                }

                deletedReleases++;
            }

            IReadOnlyList<Reference> tags;
            try {
                tags = await Client.Git.Reference.GetAllForSubNamespace(Owner, Name, "tags");
            } catch (NotFoundException) {
                tags = Array.Empty<Reference>();
            }

            foreach (var tag in tags) {
                // tag.Ref looks like "refs/tags/1.0.0"; the Delete API wants "tags/1.0.0".
                var reference = tag.Ref.StartsWith("refs/", StringComparison.Ordinal) ? tag.Ref.Substring("refs/".Length) : tag.Ref;
                try {
                    await Client.Git.Reference.Delete(Owner, Name, reference);
                } catch (NotFoundException) {
                    // already gone — the ref listing was stale
                }

                deletedTags++;
            }

            if ((releases.Count == 0 && tags.Count == 0) || DateTime.UtcNow >= deadline)
                break;

            await Task.Delay(2000);
        }

        _log.LogInformation("Reset repo {Repo}: deleted {Releases} releases and {Tags} tags", Name, deletedReleases, deletedTags);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_disposed)
            return;
        _disposed = true;

        try {
            // Delete exactly the lock file we created (by its sha). If it was replaced — e.g. our lease
            // outlived the stale window and another process reclaimed it — GitHub returns 409 and we must
            // NOT delete, since the file now belongs to that other owner.
            await Client.Repository.Content.DeleteFile(Owner, Name, GitHubRepoLock.LockFileName,
                new DeleteFileRequest("release velopack test lock", _lockSha));
            _log.LogInformation("Released GitHub lock on repo {Repo}", Name);
        } catch (ApiException ex) when (ex.StatusCode == System.Net.HttpStatusCode.Conflict) {
            _log.LogWarning("Not releasing GitHub lock on repo {Repo}: it was reclaimed by another owner (sha changed)", Name);
        } catch (Exception ex) {
            _log.LogWarning(ex, "Failed to release GitHub lock on repo {Repo}", Name);
        }
    }

    /// <inheritdoc />
    public void Dispose() => DisposeAsync().AsTask().GetAwaiter().GetResult();
}

/// <summary>
/// Collection fixture holding one <see cref="GitHubRepoLease"/> for every test in the "github" collection, so a
/// test run takes one lock instead of one per test (each lock acquisition and reset costs several API calls on
/// the token shared by all CI legs). The lease is acquired lazily by the first test that needs it (so skipped
/// runs never lock), tests in the collection run serially and reset the repo themselves, and the lock is
/// released when xunit disposes the fixture.
/// </summary>
public sealed class GitHubSharedLeaseFixture : IAsyncLifetime
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private Task<GitHubRepoLease>? _init;

    /// <summary>
    /// Returns the shared lease, acquiring it on first call. A failed acquisition is cached so later tests fail
    /// fast instead of polling the lock pool again.
    /// </summary>
    public async Task<GitHubRepoLease> GetAsync(ILogger log)
    {
        await _gate.WaitAsync();
        try {
            // The lease outlives the test that acquired it, so its logger must tolerate a finished test.
            _init ??= GitHubRepoLock.AcquireAsync(new PostTestSafeLogger(log));
        } finally {
            _gate.Release();
        }

        return await _init;
    }

    /// <inheritdoc />
    public ValueTask InitializeAsync() => default;

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (_init is { IsCompletedSuccessfully: true })
            await (await _init).DisposeAsync();
    }
}

[CollectionDefinition("github")]
public class GitHubCollection : ICollectionFixture<GitHubSharedLeaseFixture>;

/// <summary>
/// Forwards to a per-test output logger but swallows logging failures. Neovolve's test-output
/// loggers throw once the test that owns them has finished, and a lease shared across tests logs
/// after its creating test is over (including during release, after the last test).
/// </summary>
internal sealed class PostTestSafeLogger(ILogger inner) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        try {
            inner.Log(logLevel, eventId, state, exception, formatter);
        } catch {
            // The owning test is no longer active; drop the message.
        }
    }
}
