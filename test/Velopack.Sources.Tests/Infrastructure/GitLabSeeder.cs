using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit.Sdk;
using Xunit.v3;

namespace Velopack.Sources.Tests.Infrastructure;

public static class GitLabSeeder
{
    private const string BaseUrl = "http://localhost:8929";
    private const string RepoName = "testrepo";
    private const string TokenName = "velopack-test-token";

    private static readonly string[] TagNames = ["v1.0.1", "v1.0.2", "v1.0.3"];

    public static async Task<(string apiUrl, string token)> SeedAsync(IMessageSink sink)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        var pat = await CreatePatViaRailsRunner(sink);

        // Verify the token works
        var verifyRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v4/user");
        verifyRequest.Headers.Add("PRIVATE-TOKEN", pat);
        var verifyResponse = await client.SendAsync(verifyRequest);
        verifyResponse.EnsureSuccessStatusCode();
        Log(sink, "PAT verified successfully.");

        var projectId = await CreateProject(client, pat, sink);
        await DeleteStaleReleases(client, pat, projectId, sink);
        foreach (var tag in TagNames) {
            var version = tag[1..]; // strip 'v' prefix
            await UploadFeedPackage(client, pat, projectId, version, sink);
            await CreateTag(client, pat, projectId, tag, sink);
            await CreateRelease(client, pat, projectId, tag, version, sink);
        }

        var apiUrl = $"{BaseUrl}/api/v4/projects/{projectId}";
        return (apiUrl, pat);
    }

    private static async Task<string> CreatePatViaRailsRunner(IMessageSink sink)
    {
        var containerName = await FindGitLabContainerName(sink);

        var rubyScript =
            "u=User.find(1); "
            + "u.personal_access_tokens.active.where(name: 'velopack-test-token').each{|t| t.revoke!}; "
            + "t=u.personal_access_tokens.create!(name: 'velopack-test-token', scopes: ['api'], expires_at: 1.year.from_now); "
            + "puts t.token";

        var (exitCode, stdout, stderr) = await RunProcessAsync(
            "docker", $"exec {containerName} gitlab-rails runner \"{rubyScript}\"",
            workingDir: null, timeoutSeconds: 120);

        if (exitCode != 0) {
            throw new InvalidOperationException(
                $"Failed to create GitLab PAT via rails runner (exit code {exitCode}).\n"
                + $"STDOUT: {stdout}\nSTDERR: {stderr}");
        }

        var token = stdout.Trim();
        if (string.IsNullOrEmpty(token)) {
            throw new InvalidOperationException(
                $"GitLab rails runner returned empty token.\nSTDOUT: {stdout}\nSTDERR: {stderr}");
        }

        Log(sink, "PAT created via gitlab-rails runner.");
        return token;
    }

    private static async Task<string> FindGitLabContainerName(IMessageSink sink)
    {
        var (exitCode, stdout, stderr) = await RunProcessAsync(
            "docker", "ps --format {{.Names}} --filter ancestor=gitlab/gitlab-ce:latest",
            workingDir: null, timeoutSeconds: 10);

        if (exitCode != 0 || string.IsNullOrWhiteSpace(stdout)) {
            throw new InvalidOperationException(
                $"Could not find GitLab container (exit code {exitCode}).\n"
                + $"STDOUT: {stdout}\nSTDERR: {stderr}");
        }

        var containerName = stdout.Trim().Split('\n')[0].Trim();
        Log(sink, $"Found GitLab container: {containerName}");
        return containerName;
    }

    private static async Task<int> CreateProject(HttpClient client, string token, IMessageSink sink)
    {
        var searchRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v4/projects?search={RepoName}&owned=true");
        searchRequest.Headers.Add("PRIVATE-TOKEN", token);

        var searchResponse = await client.SendAsync(searchRequest);
        if (searchResponse.IsSuccessStatusCode) {
            var searchBody = await searchResponse.Content.ReadAsStringAsync();
            using var searchDoc = JsonDocument.Parse(searchBody);
            foreach (var project in searchDoc.RootElement.EnumerateArray()) {
                if (project.TryGetProperty("name", out var nameEl) && nameEl.GetString() == RepoName) {
                    var projectId = project.GetProperty("id").GetInt32();
                    Log(sink, $"Project '{RepoName}' already exists (id={projectId}).");
                    return projectId;
                }
            }
        }

        var createBody = JsonSerializer.Serialize(new {
            name = RepoName,
            initialize_with_readme = true,
        });

        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v4/projects") {
            Content = new StringContent(createBody, Encoding.UTF8, "application/json"),
        };
        createRequest.Headers.Add("PRIVATE-TOKEN", token);

        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();

        var responseBody = await createResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseBody);
        var id = doc.RootElement.GetProperty("id").GetInt32();
        Log(sink, $"Project '{RepoName}' created (id={id}).");
        return id;
    }

    private static async Task UploadFeedPackage(
        HttpClient client, string token, int projectId, string version, IMessageSink sink)
    {
        var feedFileName = $"releases.{TestFeedData.Channel}.json";
        var feedContent = Encoding.UTF8.GetBytes(TestFeedData.FeedJsonForVersion(version));

        var uploadRequest = new HttpRequestMessage(HttpMethod.Put,
            $"{BaseUrl}/api/v4/projects/{projectId}/packages/generic/releases/{version}/{feedFileName}") {
            Content = new ByteArrayContent(feedContent),
        };
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        uploadRequest.Headers.Add("PRIVATE-TOKEN", token);

        var response = await client.SendAsync(uploadRequest);

        if (response.StatusCode == HttpStatusCode.Created || response.IsSuccessStatusCode) {
            Log(sink, $"Feed package '{feedFileName}' uploaded.");
        } else {
            var body = await response.Content.ReadAsStringAsync();
            Log(sink, $"Feed package upload returned {response.StatusCode}: {body}");
            response.EnsureSuccessStatusCode();
        }
    }

    private static async Task DeleteStaleReleases(HttpClient client, string token, int projectId, IMessageSink sink)
    {
        var listRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v4/projects/{projectId}/releases?per_page=100");
        listRequest.Headers.Add("PRIVATE-TOKEN", token);

        var listResponse = await client.SendAsync(listRequest);
        if (!listResponse.IsSuccessStatusCode) return;

        var body = await listResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        foreach (var release in doc.RootElement.EnumerateArray()) {
            var tag = release.GetProperty("tag_name").GetString();
            if (tag != null && !TagNames.Contains(tag)) {
                var deleteRequest = new HttpRequestMessage(HttpMethod.Delete,
                    $"{BaseUrl}/api/v4/projects/{projectId}/releases/{tag}");
                deleteRequest.Headers.Add("PRIVATE-TOKEN", token);
                await client.SendAsync(deleteRequest);
                Log(sink, $"Deleted stale release '{tag}'.");
            }
        }
    }

    private static async Task CreateTag(
        HttpClient client, string token, int projectId, string tagName, IMessageSink sink)
    {
        var checkRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v4/projects/{projectId}/repository/tags/{tagName}");
        checkRequest.Headers.Add("PRIVATE-TOKEN", token);

        var checkResponse = await client.SendAsync(checkRequest);
        if (checkResponse.IsSuccessStatusCode) {
            Log(sink, $"Tag '{tagName}' already exists.");
            return;
        }

        var tagBody = JsonSerializer.Serialize(new {
            tag_name = tagName,
            @ref = "main",
        });

        var createRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/api/v4/projects/{projectId}/repository/tags") {
            Content = new StringContent(tagBody, Encoding.UTF8, "application/json"),
        };
        createRequest.Headers.Add("PRIVATE-TOKEN", token);

        var createResponse = await client.SendAsync(createRequest);

        if (createResponse.IsSuccessStatusCode) {
            Log(sink, $"Tag '{tagName}' created.");
        } else {
            var body = await createResponse.Content.ReadAsStringAsync();
            if (body.Contains("already exists")) {
                Log(sink, $"Tag '{tagName}' already exists.");
            } else {
                Log(sink, $"Tag creation returned {createResponse.StatusCode}: {body}");
                createResponse.EnsureSuccessStatusCode();
            }
        }
    }

    private static async Task CreateRelease(
        HttpClient client, string token, int projectId, string tagName, string version, IMessageSink sink)
    {
        var checkRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v4/projects/{projectId}/releases/{tagName}");
        checkRequest.Headers.Add("PRIVATE-TOKEN", token);

        var checkResponse = await client.SendAsync(checkRequest);
        if (checkResponse.IsSuccessStatusCode) {
            Log(sink, $"Release '{tagName}' already exists.");
            await EnsureReleaseLink(client, token, projectId, tagName, version, sink);
            return;
        }

        var releaseBody = JsonSerializer.Serialize(new {
            tag_name = tagName,
            name = tagName,
            description = $"Test release {tagName}",
        });

        var createRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/api/v4/projects/{projectId}/releases") {
            Content = new StringContent(releaseBody, Encoding.UTF8, "application/json"),
        };
        createRequest.Headers.Add("PRIVATE-TOKEN", token);

        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();
        Log(sink, $"Release '{tagName}' created.");

        await EnsureReleaseLink(client, token, projectId, tagName, version, sink);
    }

    private static async Task EnsureReleaseLink(
        HttpClient client, string token, int projectId, string tagName, string version, IMessageSink sink)
    {
        var feedFileName = $"releases.{TestFeedData.Channel}.json";
        var packageUrl = $"{BaseUrl}/api/v4/projects/{projectId}/packages/generic/releases/{version}/{feedFileName}";

        var listRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v4/projects/{projectId}/releases/{tagName}/assets/links");
        listRequest.Headers.Add("PRIVATE-TOKEN", token);

        var listResponse = await client.SendAsync(listRequest);
        if (listResponse.IsSuccessStatusCode) {
            var listBody = await listResponse.Content.ReadAsStringAsync();
            using var listDoc = JsonDocument.Parse(listBody);
            foreach (var link in listDoc.RootElement.EnumerateArray()) {
                if (link.TryGetProperty("name", out var nameEl) && nameEl.GetString() == feedFileName) {
                    Log(sink, $"Release link for '{feedFileName}' already exists on '{tagName}'.");
                    return;
                }
            }
        }

        var linkBody = JsonSerializer.Serialize(new {
            name = feedFileName,
            url = packageUrl,
            link_type = "other",
        });

        var linkRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/api/v4/projects/{projectId}/releases/{tagName}/assets/links") {
            Content = new StringContent(linkBody, Encoding.UTF8, "application/json"),
        };
        linkRequest.Headers.Add("PRIVATE-TOKEN", token);

        var linkResponse = await client.SendAsync(linkRequest);
        linkResponse.EnsureSuccessStatusCode();
        Log(sink, $"Release link for '{feedFileName}' created on '{tagName}'.");
    }

    private static async Task<(int exitCode, string stdout, string stderr)> RunProcessAsync(
        string fileName, string arguments, string? workingDir, int timeoutSeconds = 30)
    {
        var psi = new ProcessStartInfo {
            FileName = fileName,
            Arguments = arguments,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (workingDir != null) {
            psi.WorkingDirectory = workingDir;
        }

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException($"Failed to start process: {fileName}");

        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();

        var exited = await Task.Run(() => process.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds)));
        if (!exited) {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"Process '{fileName}' timed out after {timeoutSeconds} seconds.");
        }

        return (process.ExitCode, await stdoutTask, await stderrTask);
    }

    private static void Log(IMessageSink sink, string message)
    {
        sink.OnMessage(new DiagnosticMessage($"[GitLabSeeder] {message}"));
    }
}
