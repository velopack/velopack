using System.Globalization;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using Velopack.Core;

namespace Velopack.Deployment.Tests;

/// <summary>A GitLab project created for a test.</summary>
public sealed record GitLabProject(long Id, string PathWithNamespace, string WebUrl);

/// <summary>An asset link on a GitLab release.</summary>
public sealed record GitLabAssetLink(string Name, string Url, string? DirectAssetUrl);

/// <summary>A GitLab release plus its asset links, as read back from the API.</summary>
public sealed record GitLabRelease(string TagName, string? Name, bool UpcomingRelease, IReadOnlyList<GitLabAssetLink> Links);

/// <summary>
/// Administers the local GitLab container for update-source tests: seeds a deterministic personal access
/// token (PAT), creates public projects, and publishes releases with downloadable assets.
///
/// The PAT is seeded once via <c>gitlab-rails runner</c> (a slow, 30-60s operation) with a fixed token value
/// so its plaintext is known to the tests. Seeding is idempotent (skips if the named token already exists)
/// and guarded by a process-wide <see cref="SemaphoreSlim"/>.
///
/// Field mapping — the created releases are shaped to satisfy BOTH update-source implementations
/// (<c>src/lib-csharp/Sources/GitlabSource.cs</c> and <c>src/lib-rust/src/sources/gitlab.rs</c>):
///   • release.<c>upcoming_release</c>  — both filter this out unless prerelease/upcoming is requested, so
///     stable releases are published with <c>released_at</c> in the past (GitLab derives upcoming_release
///     from a future released_at).
///   • release.<c>released_at</c>       — both sort releases by this descending to pick the latest.
///   • assets.links[].<c>name</c>       — both match the wanted asset filename case-insensitively.
///   • assets.links[].<c>url</c>        — used when an access token IS present (the tests always pass one).
///   • assets.links[].<c>direct_asset_url</c> — used when NO access token is present.
/// Both link URLs point at the GitLab generic package registry download endpoint, which serves the raw bytes
/// with or without a token on a public project. (Plain project "markdown" uploads are NOT served unless the
/// upload is referenced from an issue/MR/release description, so the package registry is used instead.)
/// </summary>
public static class GitLabAdmin
{
    public const string BaseUrl = DockerServices.GitLabBaseUrl;
    public const string ContainerName = DockerServices.GitLabContainer;
    public const string ApiBase = BaseUrl + "/api/v4";

    public const string TokenName = "velopack-tests";
    // Deterministic value set via PersonalAccessToken#set_token so tests know the plaintext across runs.
    public const string SeededToken = "glpat-velopacktests1234567890";
    private const string PackageName = "velopack";

    private static readonly SemaphoreSlim _seedLock = new(1, 1);
    private static volatile string? _seededToken;

    /// <summary>
    /// Ensures the <see cref="SeededToken"/> PAT exists and works, returning it. Idempotent and cached for
    /// the process. First checks whether the token already authenticates (fast), and only shells into the
    /// container to run <c>gitlab-rails runner</c> if it does not.
    /// </summary>
    public static async Task<string> EnsureSeededAsync(ILogger logger)
    {
        if (_seededToken != null)
            return _seededToken;

        await _seedLock.WaitAsync().ConfigureAwait(false);
        try {
            if (_seededToken != null)
                return _seededToken;

            if (await ProbePatAsync(SeededToken).ConfigureAwait(false)) {
                logger.Info("GitLab PAT already valid; skipping seed.");
                _seededToken = SeededToken;
                return _seededToken;
            }

            await SeedViaRailsAsync(logger).ConfigureAwait(false);

            if (!await ProbePatAsync(SeededToken).ConfigureAwait(false))
                throw new Exception("GitLab PAT seeding ran but the token still does not authenticate.");

            _seededToken = SeededToken;
            return _seededToken;
        } finally {
            _seedLock.Release();
        }
    }

    /// <summary>Validates a PAT via GET /api/v4/user.</summary>
    public static async Task<bool> ProbePatAsync(string token)
    {
        try {
            using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/user");
            req.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
            using var resp = await DockerServices.Http.SendAsync(req).ConfigureAwait(false);
            return resp.IsSuccessStatusCode;
        } catch {
            return false;
        }
    }

    private static async Task SeedViaRailsAsync(ILogger logger)
    {
        // Single-line, idempotent Ruby: create the named PAT with a fixed token value only if it is absent.
        var ruby =
            "u = User.find_by_username('root'); " +
            "t = u.personal_access_tokens.active.find_by(name: '" + TokenName + "'); " +
            "if t.nil?; " +
            "t = u.personal_access_tokens.create!(scopes: [:api, :read_api, :read_repository, :write_repository], " +
            "name: '" + TokenName + "', expires_at: 365.days.from_now); " +
            "t.set_token('" + SeededToken + "'); t.save!; " +
            "end; puts t.token.to_s";

        logger.Info("Seeding GitLab PAT via gitlab-rails runner (this can take 30-60s)...");
        var result = await DockerServices.RunProcessAsync(
            "docker",
            new[] { "exec", ContainerName, "gitlab-rails", "runner", ruby },
            TimeSpan.FromSeconds(180)).ConfigureAwait(false);

        if (!result.Success)
            throw new Exception($"gitlab-rails runner failed to seed PAT: {result.Combined}");
    }

    /// <summary>Creates a public project initialised with a README (so it has a 'main' branch for tagging).</summary>
    public static async Task<GitLabProject> CreateProjectAsync(string token, string name, ILogger logger)
    {
        var json = JsonSerializer.Serialize(new {
            name,
            visibility = "public",
            initialize_with_readme = true,
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/projects") {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
        using var resp = await DockerServices.Http.SendAsync(req).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Failed to create GitLab project '{name}': {(int) resp.StatusCode} {body}");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        var project = new GitLabProject(
            root.GetProperty("id").GetInt64(),
            root.GetProperty("path_with_namespace").GetString()!,
            root.GetProperty("web_url").GetString()!);
        logger.Info($"Created GitLab project {project.PathWithNamespace} (id {project.Id}).");
        return project;
    }

    /// <summary>
    /// Publishes a release on <paramref name="project"/> with the given assets attached. Each asset is first
    /// uploaded to the generic package registry, then linked from the release (both <c>url</c> and
    /// <c>direct_asset_url</c> point at the registry download endpoint). See the class remarks for the field
    /// mapping that keeps this compatible with both update-source implementations.
    /// </summary>
    public static async Task<GitLabRelease> CreateReleaseWithAssetsAsync(
        string token, GitLabProject project, string tagName, string releaseName,
        IReadOnlyList<(string FileName, byte[] Content)> assets, ILogger logger, bool upcomingRelease = false)
    {
        var version = SanitizePackageVersion(tagName);
        var links = new List<object>(assets.Count);
        foreach (var (fileName, content) in assets) {
            var downloadUrl = await UploadGenericPackageAsync(token, project.Id, version, fileName, content).ConfigureAwait(false);
            links.Add(new {
                name = fileName,
                url = downloadUrl,
                direct_asset_url = downloadUrl,
                link_type = "package",
            });
        }

        // GitLab computes upcoming_release from released_at: a future date => upcoming/pre-release.
        var releasedAt = (upcomingRelease ? DateTime.UtcNow.AddYears(1) : DateTime.UtcNow.AddMinutes(-1))
            .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var json = JsonSerializer.Serialize(new {
            tag_name = tagName,
            name = releaseName,
            @ref = "main",
            released_at = releasedAt,
            assets = new { links },
        });

        using var req = new HttpRequestMessage(HttpMethod.Post, $"{ApiBase}/projects/{project.Id}/releases") {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        req.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
        using var resp = await DockerServices.Http.SendAsync(req).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Failed to create GitLab release '{tagName}' on project {project.Id}: {(int) resp.StatusCode} {body}");

        var release = ParseRelease(body);
        logger.Info($"Created GitLab release '{release.TagName}' (upcoming={release.UpcomingRelease}) with {release.Links.Count} asset(s).");
        return release;
    }

    /// <summary>Reads a release (and its asset links) back from the API.</summary>
    public static async Task<GitLabRelease> GetReleaseAsync(string token, long projectId, string tagName)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"{ApiBase}/projects/{projectId}/releases/{Uri.EscapeDataString(tagName)}");
        req.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
        using var resp = await DockerServices.Http.SendAsync(req).ConfigureAwait(false);
        var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            throw new Exception($"Failed to get GitLab release '{tagName}' on project {projectId}: {(int) resp.StatusCode} {body}");
        return ParseRelease(body);
    }

    /// <summary>Best-effort project delete; swallows failures so teardown never masks a real assertion.</summary>
    public static async Task DeleteProjectAsync(string token, long projectId, ILogger logger)
    {
        try {
            using var req = new HttpRequestMessage(HttpMethod.Delete, $"{ApiBase}/projects/{projectId}");
            req.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
            using var resp = await DockerServices.Http.SendAsync(req).ConfigureAwait(false);
            logger.Info($"Deleted GitLab project {projectId} (status {(int) resp.StatusCode}).");
        } catch (Exception ex) {
            logger.Warn(ex, $"Best-effort delete of GitLab project {projectId} failed.");
        }
    }

    private static async Task<string> UploadGenericPackageAsync(string token, long projectId, string version, string fileName, byte[] content)
    {
        var url = $"{ApiBase}/projects/{projectId}/packages/generic/{PackageName}/{version}/{Uri.EscapeDataString(fileName)}";
        using var req = new HttpRequestMessage(HttpMethod.Put, url) {
            Content = new ByteArrayContent(content),
        };
        req.Content.Headers.ContentType = new MediaTypeHeaderValue("application/octet-stream");
        req.Headers.TryAddWithoutValidation("PRIVATE-TOKEN", token);
        using var resp = await DockerServices.Http.SendAsync(req).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode) {
            var body = await resp.Content.ReadAsStringAsync().ConfigureAwait(false);
            throw new Exception($"Failed to upload generic package '{fileName}' to project {projectId}: {(int) resp.StatusCode} {body}");
        }
        return url;
    }

    private static GitLabRelease ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        var tag = root.GetProperty("tag_name").GetString()!;
        var name = root.TryGetProperty("name", out var n) ? n.GetString() : null;
        var upcoming = root.TryGetProperty("upcoming_release", out var u) && u.GetBoolean();

        var links = new List<GitLabAssetLink>();
        if (root.TryGetProperty("assets", out var assets) && assets.TryGetProperty("links", out var linkArr)) {
            foreach (var l in linkArr.EnumerateArray()) {
                links.Add(new GitLabAssetLink(
                    l.GetProperty("name").GetString()!,
                    l.GetProperty("url").GetString()!,
                    l.TryGetProperty("direct_asset_url", out var d) ? d.GetString() : null));
            }
        }
        return new GitLabRelease(tag, name, upcoming, links);
    }

    // Generic package versions must be a non-empty run of [A-Za-z0-9.+-]; sanitise anything else.
    private static string SanitizePackageVersion(string tag)
    {
        var sb = new StringBuilder(tag.Length);
        foreach (var ch in tag) {
            sb.Append(Char.IsLetterOrDigit(ch) || ch is '.' or '+' or '-' ? ch : '-');
        }
        var s = sb.ToString().Trim('.');
        return String.IsNullOrEmpty(s) ? "0.0.0" : s;
    }
}
