using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PrNuGetProxy;

public sealed record PullRequestInfo(int Number);
public sealed record WorkflowRun(
    long Id,
    [property: JsonPropertyName("run_attempt")] int Attempt,
    [property: JsonPropertyName("head_sha")] string HeadSha,
    string Status,
    string? Conclusion,
    string Event,
    [property: JsonPropertyName("pull_requests")] PullRequestInfo[] PullRequests);
public sealed record Artifact(long Id, string Name, bool Expired, string? Digest);
public sealed record RunsResponse([property: JsonPropertyName("workflow_runs")] WorkflowRun[] Runs);
public sealed record ArtifactsResponse(Artifact[] Artifacts);

public interface IGitHubClient
{
    Task<IReadOnlyList<PullRequestInfo>> GetOpenPullRequestsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<WorkflowRun>> GetRunsAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<Artifact>> GetArtifactsAsync(long runId, CancellationToken cancellationToken);
    Task DownloadAsync(Artifact artifact, string destination, CancellationToken cancellationToken);
}

public sealed class GitHubClient(ProxyOptions options) : IGitHubClient
{
    public async Task<IReadOnlyList<PullRequestInfo>> GetOpenPullRequestsAsync(CancellationToken cancellationToken)
    {
        var pullRequests = new List<PullRequestInfo>();
        for (var page = 1; ; page++)
        {
            var response = await GetAsync<PullRequestInfo[]>(
                $"repos/{options.Repository}/pulls?state=open&per_page=100&page={page}", cancellationToken);
            pullRequests.AddRange(response);
            if (response.Length < 100) return pullRequests;
        }
    }

    public async Task<IReadOnlyList<WorkflowRun>> GetRunsAsync(CancellationToken cancellationToken)
    {
        var runs = new List<WorkflowRun>();
        for (var page = 1; runs.Count < options.RunLimit; page++)
        {
            var response = await GetAsync<RunsResponse>(
                $"repos/{options.Repository}/actions/workflows/{options.Workflow}/runs?event=pull_request&status=success&per_page=100&page={page}",
                cancellationToken);
            runs.AddRange(response.Runs.Take(options.RunLimit - runs.Count));
            if (response.Runs.Length < 100) break;
        }
        foreach (var id in options.RunIds)
        {
            var run = await GetAsync<WorkflowRun>($"repos/{options.Repository}/actions/runs/{id}", cancellationToken);
            if (runs.All(x => x.Id != id)) runs.Add(run);
        }
        return runs;
    }

    public async Task<IReadOnlyList<Artifact>> GetArtifactsAsync(long runId, CancellationToken cancellationToken)
    {
        var artifacts = new List<Artifact>();
        for (var page = 1; ; page++)
        {
            var response = await GetAsync<ArtifactsResponse>(
                $"repos/{options.Repository}/actions/runs/{runId}/artifacts?per_page=100&page={page}", cancellationToken);
            artifacts.AddRange(response.Artifacts);
            if (response.Artifacts.Length < 100) return artifacts;
        }
    }

    private static async Task<T> GetAsync<T>(string path, CancellationToken cancellationToken)
    {
        using var stream = new MemoryStream();
        await ExecuteAsync(["api", path], stream, cancellationToken);
        stream.Position = 0;
        return await JsonSerializer.DeserializeAsync<T>(stream, JsonSettings.Options, cancellationToken)
            ?? throw new InvalidDataException($"GitHub returned an empty response for {path}.");
    }

    public async Task DownloadAsync(Artifact artifact, string destination, CancellationToken cancellationToken)
    {
        await using var output = File.Create(destination);
        await ExecuteAsync(["api", $"repos/{options.Repository}/actions/artifacts/{artifact.Id}/zip"], output, cancellationToken);
    }

    private static async Task ExecuteAsync(string[] arguments, Stream output, CancellationToken cancellationToken)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(10));
        var start = new ProcessStartInfo("gh")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Unable to start GitHub CLI. Install gh and run gh auth login.");
        try
        {
            var errorTask = process.StandardError.ReadToEndAsync(timeout.Token);
            await process.StandardOutput.BaseStream.CopyToAsync(output, timeout.Token);
            await process.WaitForExitAsync(timeout.Token);
            var error = await errorTask;
            if (process.ExitCode != 0) throw new IOException($"GitHub CLI failed ({process.ExitCode}): {error.Trim()}");
        }
        finally
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
                await process.WaitForExitAsync(CancellationToken.None);
            }
        }
    }
}
