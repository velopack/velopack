using System.Collections.Concurrent;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using Velopack.Core;

namespace Velopack.Deployment.Tests;

/// <summary>An authenticated Gitea admin session against a specific server.</summary>
public sealed record GiteaContext(GiteaServer Server, string Token, string Username);

/// <summary>A repository created on a Gitea server.</summary>
public sealed record GiteaRepo(string Owner, string Name, string HtmlUrl, string CloneUrl);

/// <summary>
/// Administers a Gitea server for tests: seeds an admin user + API token, and creates/deletes repos.
///
/// Deliberately uses plain <see cref="HttpClient"/> + System.Text.Json against the Gitea REST API rather
/// than the Gitea.Net client, so this administrative plumbing stays independent of the code under test.
/// Seeding is idempotent and safe under concurrent callers (a per-server <see cref="SemaphoreSlim"/> gate
/// plus a cached <see cref="GiteaContext"/>).
/// </summary>
public static class GiteaAdmin
{
    public const string AdminUsername = "velopack";
    public const string AdminPassword = "VelopackTest123!";
    public const string AdminEmail = "velopack@example.com";

    // A single deterministic token name reused across runs: we delete-then-recreate on each seed so a
    // data volume holds at most one test token instead of accumulating one per process.
    public const string TokenName = "velopack-tests";

    private static readonly string[] TokenScopes = {
        "write:repository", "write:user", "write:organization", "write:admin",
    };

    private static readonly ConcurrentDictionary<string, SemaphoreSlim> _locks = new();
    private static readonly ConcurrentDictionary<string, GiteaContext> _contexts = new();

    /// <summary>
    /// Ensures the admin user + a fresh API token exist on <paramref name="server"/>, returning a cached
    /// <see cref="GiteaContext"/>. Idempotent: repeated calls for the same server return the same context.
    /// </summary>
    public static async Task<GiteaContext> EnsureSeededAsync(GiteaServer server, ILogger logger)
    {
        if (_contexts.TryGetValue(server.Name, out var cached))
            return cached;

        var gate = _locks.GetOrAdd(server.Name, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync().ConfigureAwait(false);
        try {
            if (_contexts.TryGetValue(server.Name, out cached))
                return cached;

            // Probe first: only shell into the container (which fails where the docker CLI can't exec, e.g.
            // a remote daemon) when the admin user doesn't already authenticate over the REST API.
            if (!await ProbeAdminUserAsync(server).ConfigureAwait(false)) {
                await CreateAdminUserAsync(server, logger).ConfigureAwait(false);
            } else {
                logger.Info($"Gitea admin user already present on {server.Name}; skipping docker exec.");
            }

            var token = await CreateTokenAsync(server, logger).ConfigureAwait(false);
            var ctx = new GiteaContext(server, token, AdminUsername);
            _contexts[server.Name] = ctx;
            logger.Info($"Seeded Gitea admin '{AdminUsername}' on {server.Name} ({server.BaseUrl}).");
            return ctx;
        } finally {
            gate.Release();
        }
    }

    private static async Task CreateAdminUserAsync(GiteaServer server, ILogger logger)
    {
        var result = await DockerServices.RunProcessAsync(
            "docker",
            new[] {
                "exec", "-u", "git", server.ContainerName,
                "gitea", "admin", "user", "create",
                "--admin",
                "--username", AdminUsername,
                "--password", AdminPassword,
                "--email", AdminEmail,
                "--must-change-password=false",
            },
            TimeSpan.FromSeconds(60)).ConfigureAwait(false);

        if (result.Success)
            return;

        // Idempotency: a pre-existing user is a success for our purposes.
        if (result.Combined.Contains("already exists", StringComparison.OrdinalIgnoreCase)) {
            logger.Info($"Gitea admin user already exists on {server.Name}.");
            return;
        }

        throw new Exception($"Failed to create Gitea admin user on {server.Name}: {result.Combined}");
    }

    /// <summary>Probes whether the admin user authenticates over the REST API (basic auth GET /api/v1/user).</summary>
    private static async Task<bool> ProbeAdminUserAsync(GiteaServer server)
    {
        try {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{server.BaseUrl}/api/v1/user");
            AddBasicAuth(req);
            using var resp = await DockerServices.Http.SendAsync(req).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        } catch {
            return false;
        }
    }

    private static async Task<string> CreateTokenAsync(GiteaServer server, ILogger logger)
    {
        var url = $"{server.BaseUrl}/api/v1/users/{AdminUsername}/tokens";

        // Delete any stale token of this name first so re-seeds recreate one token instead of accumulating.
        await DeleteExistingTokenAsync(server, TokenName).ConfigureAwait(false);

        // Newer Gitea requires an explicit scope list; some older API versions reject the field entirely.
        var withScopes = JsonSerializer.Serialize(new { name = TokenName, scopes = TokenScopes });
        var (status, body) = await PostTokenAsync(server, url, withScopes).ConfigureAwait(false);
        if (IsSuccess(status))
            return ExtractSha1(server, body);

        // Only retry without scopes when the failure is specifically about the scopes field.
        var scopeRejected = (status == HttpStatusCode.BadRequest || status == HttpStatusCode.UnprocessableEntity)
            && body.Contains("scope", StringComparison.OrdinalIgnoreCase);
        if (scopeRejected) {
            logger.Info($"Gitea token request with scopes rejected on {server.Name}, retrying without scopes. Response: {body}");
            var withoutScopes = JsonSerializer.Serialize(new { name = TokenName });
            var (status2, body2) = await PostTokenAsync(server, url, withoutScopes).ConfigureAwait(false);
            if (IsSuccess(status2))
                return ExtractSha1(server, body2);
            throw new Exception($"Failed to create Gitea token on {server.Name}. With scopes: {body}. Without scopes: {body2}");
        }

        throw new Exception($"Failed to create Gitea token on {server.Name}: {(int) status} {body}");
    }

    /// <summary>Deletes a named token if present; ignores 404/422 (no such token).</summary>
    private static async Task DeleteExistingTokenAsync(GiteaServer server, string tokenName)
    {
        try {
            var url = $"{server.BaseUrl}/api/v1/users/{AdminUsername}/tokens/{Uri.EscapeDataString(tokenName)}";
            using var req = new HttpRequestMessage(HttpMethod.Delete, url);
            AddBasicAuth(req);
            using var resp = await DockerServices.Http.SendAsync(req).ConfigureAwait(false);
            // 204 => deleted; 404/422 => nothing to delete. Any other status is left for CreateToken to surface.
        } catch {
            // Best-effort: a failed pre-delete shouldn't block creation.
        }
    }

    private static string ExtractSha1(GiteaServer server, string body)
    {
        using var doc = JsonDocument.Parse(body);
        var sha1 = doc.RootElement.GetProperty("sha1").GetString();
        if (String.IsNullOrWhiteSpace(sha1))
            throw new Exception($"Gitea token response on {server.Name} did not contain a 'sha1' token: {body}");
        return sha1!;
    }

    private static bool IsSuccess(HttpStatusCode status) => (int) status >= 200 && (int) status < 300;

    private static async Task<(HttpStatusCode status, string body)> PostTokenAsync(GiteaServer server, string url, string json)
    {
        using var req = new HttpRequestMessage(HttpMethod.Post, url);
        AddBasicAuth(req);
        req.Content = new StringContent(json, Encoding.UTF8, "application/json");
        using var resp = await DockerServices.Http.SendAsync(req).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        return (resp.StatusCode, body);
    }

    private static void AddBasicAuth(HttpRequestMessage req)
    {
        var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{AdminUsername}:{AdminPassword}"));
        req.Headers.Authorization = new AuthenticationHeaderValue("Basic", basic);
    }

    /// <summary>Creates an auto-initialised public repository owned by the admin user (default branch 'main').</summary>
    public static async Task<GiteaRepo> CreateRepoAsync(GiteaContext ctx, string repoName, ILogger logger)
    {
        var url = $"{ctx.Server.BaseUrl}/api/v1/user/repos";
        var json = JsonSerializer.Serialize(new {
            name = repoName,
            auto_init = true,
            default_branch = "main",
            @private = false,
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, url) {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        AddTokenAuth(req, ctx);
        using var resp = await DockerServices.Http.SendAsync(req).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Failed to create Gitea repo '{repoName}' on {ctx.Server.Name}: {(int) resp.StatusCode} {body}");

        var repo = ParseRepo(body);
        logger.Info($"Created Gitea repo {repo.Owner}/{repo.Name} on {ctx.Server.Name}.");
        return repo;
    }

    /// <summary>Fetches a repo, returning null if it does not exist.</summary>
    public static async Task<GiteaRepo?> TryGetRepoAsync(GiteaContext ctx, string owner, string name)
    {
        var url = $"{ctx.Server.BaseUrl}/api/v1/repos/{owner}/{name}";
        using var req = new HttpRequestMessage(HttpMethod.Get, url);
        AddTokenAuth(req, ctx);
        using var resp = await DockerServices.Http.SendAsync(req).ConfigureAwait(false);
        if (resp.StatusCode == HttpStatusCode.NotFound)
            return null;
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Failed to get Gitea repo {owner}/{name} on {ctx.Server.Name}: {(int) resp.StatusCode} {body}");
        return ParseRepo(body);
    }

    /// <summary>Best-effort delete; swallows any failure so test teardown never masks the real assertion.</summary>
    public static async Task DeleteRepoAsync(GiteaContext ctx, string owner, string name, ILogger logger)
    {
        try {
            var url = $"{ctx.Server.BaseUrl}/api/v1/repos/{owner}/{name}";
            using var req = new HttpRequestMessage(HttpMethod.Delete, url);
            AddTokenAuth(req, ctx);
            using var resp = await DockerServices.Http.SendAsync(req).ConfigureAwait(false);
            logger.Info($"Deleted Gitea repo {owner}/{name} on {ctx.Server.Name} (status {(int) resp.StatusCode}).");
        } catch (Exception ex) {
            logger.Warn(ex, $"Best-effort delete of Gitea repo {owner}/{name} on {ctx.Server.Name} failed.");
        }
    }

    private static void AddTokenAuth(HttpRequestMessage req, GiteaContext ctx)
        => req.Headers.TryAddWithoutValidation("Authorization", $"token {ctx.Token}");

    private static GiteaRepo ParseRepo(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var owner = root.GetProperty("owner").GetProperty("login").GetString()!;
        var name = root.GetProperty("name").GetString()!;
        var htmlUrl = root.GetProperty("html_url").GetString()!;
        var cloneUrl = root.TryGetProperty("clone_url", out var c) ? (c.GetString() ?? htmlUrl) : htmlUrl;
        return new GiteaRepo(owner, name, htmlUrl, cloneUrl);
    }
}
