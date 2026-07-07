using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.Http;
using System.Text;
using System.Threading;

namespace Velopack.Deployment.Tests;

/// <summary>Result of running an external process: exit code plus captured stdout/stderr.</summary>
public sealed record ProcessResult(int ExitCode, string StdOut, string StdErr)
{
    public bool Success => ExitCode == 0;
    public string Combined => (StdOut + "\n" + StdErr).Trim();
}

/// <summary>
/// Describes one of the Gitea servers in the local docker stack. Multiple Gitea versions
/// are run side by side so the tests catch API back-compat breaks between releases.
/// </summary>
public sealed record GiteaServer(string Name, string BaseUrl, string ContainerName);

/// <summary>
/// Central registry of the local docker service endpoints used by the deployment tests, plus
/// cheap cached "is it up?" probes. Every probe is memoised for the lifetime of the process
/// (via <see cref="Lazy{T}"/>) so an absent service is only probed once and tests skip fast.
///
/// The endpoints/ports here mirror <c>docker/docker-compose.yml</c> exactly — keep them in sync.
/// </summary>
public static class DockerServices
{
    /// <summary>Repo-relative path to the compose file, surfaced in skip messages.</summary>
    public const string ComposeFile = "test/Velopack.Deployment.Tests/docker/docker-compose.yml";

    public const string GitLabBaseUrl = "http://localhost:8929";
    public const string GitLabContainer = "velopack-test-gitlab";

    // Azurite has no health endpoint; a bare GET returns HTTP 400, which is still proof it is up.
    public const string AzuriteBlobEndpoint = "http://127.0.0.1:10000";
    // S3 emulator (adobe/s3mock). A GET to the root returns the ListAllMyBuckets XML with HTTP 200.
    public const string S3MockEndpoint = "http://localhost:9090";

    /// <summary>The three Gitea servers defined in the compose file.</summary>
    public static IReadOnlyList<GiteaServer> GiteaServers { get; } = new[] {
        new GiteaServer("gitea-1.22", "http://localhost:3122", "velopack-test-gitea-122"),
        new GiteaServer("gitea-1.24", "http://localhost:3124", "velopack-test-gitea-124"),
        new GiteaServer("gitea-latest", "http://localhost:3199", "velopack-test-gitea-latest"),
    };

    /// <summary>Shared HttpClient. Per-request auth is applied via HttpRequestMessage, never default headers.</summary>
    internal static readonly HttpClient Http = new HttpClient();

    private static readonly ConcurrentDictionary<string, Lazy<Task<bool>>> _probes = new();

    private static Task<bool> ProbeCachedAsync(string key, Func<Task<bool>> factory)
        => _probes.GetOrAdd(key, _ => new Lazy<Task<bool>>(factory)).Value;

    public static Task<bool> IsGiteaUpAsync(GiteaServer server)
        => ProbeCachedAsync($"gitea:{server.Name}", () => ProbeHttpAsync($"{server.BaseUrl}/api/healthz", TimeSpan.FromSeconds(3)));

    // Note: GitLab's /-/health is IP-whitelisted to loopback (the in-container docker healthcheck can use it,
    // but host requests arrive from the docker gateway and get a 404). Probe the web login page instead,
    // which returns 200 once GitLab is up and 502 while it is still booting.
    public static Task<bool> IsGitLabUpAsync()
        => ProbeCachedAsync("gitlab", () => ProbeHttpAsync($"{GitLabBaseUrl}/users/sign_in", TimeSpan.FromSeconds(5)));

    public static Task<bool> IsAzuriteUpAsync()
        => ProbeCachedAsync("azurite", () => ProbeHttpAsync($"{AzuriteBlobEndpoint}/", TimeSpan.FromSeconds(3), anyResponse: true));

    public static Task<bool> IsS3MockUpAsync()
        => ProbeCachedAsync("s3mock", () => ProbeHttpAsync($"{S3MockEndpoint}/", TimeSpan.FromSeconds(3)));

    public static async Task SkipUnlessGiteaUpAsync(GiteaServer server)
    {
        Assert.SkipWhen(
            !await IsGiteaUpAsync(server).ConfigureAwait(false),
            $"Gitea '{server.Name}' is not reachable at {server.BaseUrl}. Start the stack with: docker compose -f {ComposeFile} up -d");
    }

    public static async Task SkipUnlessGitLabUpAsync()
    {
        Assert.SkipWhen(
            !await IsGitLabUpAsync().ConfigureAwait(false),
            $"GitLab is not reachable at {GitLabBaseUrl}. Start the stack with: docker compose -f {ComposeFile} up -d "
            + "(GitLab can take several minutes to become healthy on first boot).");
    }

    public static async Task SkipUnlessAzuriteUpAsync()
    {
        Assert.SkipWhen(
            !await IsAzuriteUpAsync().ConfigureAwait(false),
            $"Azurite is not reachable at {AzuriteBlobEndpoint}. Start the stack with: docker compose -f {ComposeFile} up -d");
    }

    public static async Task SkipUnlessS3MockUpAsync()
    {
        Assert.SkipWhen(
            !await IsS3MockUpAsync().ConfigureAwait(false),
            $"S3Mock is not reachable at {S3MockEndpoint}. Start the stack with: docker compose -f {ComposeFile} up -d");
    }

    private static async Task<bool> ProbeHttpAsync(string url, TimeSpan timeout, bool anyResponse = false)
    {
        try {
            using var cts = new CancellationTokenSource(timeout);
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await Http.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            return anyResponse || resp.IsSuccessStatusCode;
        } catch {
            return false;
        }
    }

    /// <summary>
    /// Runs an external process (e.g. <c>docker exec ...</c>), capturing stdout and stderr. Used by the
    /// Gitea/GitLab admin helpers to drive the containers' CLIs. Never throws for a non-zero exit — callers
    /// inspect <see cref="ProcessResult"/> and decide.
    /// </summary>
    internal static async Task<ProcessResult> RunProcessAsync(string fileName, IReadOnlyList<string> args, TimeSpan timeout)
    {
        var psi = new ProcessStartInfo(fileName) {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in args)
            psi.ArgumentList.Add(a);

        using var proc = new Process { StartInfo = psi };
        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        proc.OutputDataReceived += (_, e) => { if (e.Data != null) stdout.AppendLine(e.Data); };
        proc.ErrorDataReceived += (_, e) => { if (e.Data != null) stderr.AppendLine(e.Data); };

        proc.Start();
        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        using var cts = new CancellationTokenSource(timeout);
        try {
            await proc.WaitForExitAsync(cts.Token).ConfigureAwait(false);
        } catch (OperationCanceledException) {
            try { proc.Kill(true); } catch { }
            throw new TimeoutException($"Process '{fileName} {String.Join(' ', args)}' timed out after {timeout.TotalSeconds:0}s.");
        }

        return new ProcessResult(proc.ExitCode, stdout.ToString().Trim(), stderr.ToString().Trim());
    }
}
