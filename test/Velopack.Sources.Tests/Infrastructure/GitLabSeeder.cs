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
    private const string TagName = "v2.0.0";
    private const string TokenName = "velopack-test-token";

    public static async Task<(string apiUrl, string token)> SeedAsync(IMessageSink sink)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // Step 1: Create a PAT via gitlab-rails runner (works with all GitLab versions,
        // avoids the removed OAuth password grant)
        var pat = await CreatePatViaRailsRunner(sink);

        // Verify the token works
        var verifyRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v4/user");
        verifyRequest.Headers.Add("PRIVATE-TOKEN", pat);
        var verifyResponse = await client.SendAsync(verifyRequest);
        verifyResponse.EnsureSuccessStatusCode();
        Log(sink, "PAT verified successfully.");

        // Step 2: Create project (repo)
        var projectId = await CreateProject(client, pat, sink);

        // Step 3: Upload feed file via generic packages API
        await UploadFeedPackage(client, pat, projectId, sink);

        // Step 4: Create tag
        await CreateTag(client, pat, projectId, sink);

        // Step 5: Create release with link to the feed file
        await CreateReleaseWithLink(client, pat, projectId, sink);

        var apiUrl = $"{BaseUrl}/api/v4/projects/{projectId}";
        return (apiUrl, pat);
    }

    private static async Task<string> CreatePatViaRailsRunner(IMessageSink sink)
    {
        // Find the GitLab container name
        var containerName = await FindGitLabContainerName(sink);

        // Ruby script to revoke any existing PAT with this name and create a fresh one.
        // We must always create a new token because the plaintext value is only available
        // at creation time (existing tokens only have the hashed value).
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
        // Use docker ps to find the GitLab container
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
        // Check if project already exists
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

        // Create project
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

    private static async Task UploadFeedPackage(HttpClient client, string token, int projectId, IMessageSink sink)
    {
        var feedFileName = $"releases.{TestFeedData.Channel}.json";
        var feedContent = Encoding.UTF8.GetBytes(TestFeedData.FeedJson);

        var uploadRequest = new HttpRequestMessage(HttpMethod.Put,
            $"{BaseUrl}/api/v4/projects/{projectId}/packages/generic/releases/1.0.0/{feedFileName}") {
            Content = new ByteArrayContent(feedContent),
        };
        uploadRequest.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        uploadRequest.Headers.Add("PRIVATE-TOKEN", token);

        var response = await client.SendAsync(uploadRequest);

        // 201 = created, 200 = already exists (idempotent)
        if (response.StatusCode == HttpStatusCode.Created || response.IsSuccessStatusCode) {
            Log(sink, $"Feed package '{feedFileName}' uploaded.");
        } else {
            var body = await response.Content.ReadAsStringAsync();
            Log(sink, $"Feed package upload returned {response.StatusCode}: {body}");
            response.EnsureSuccessStatusCode();
        }
    }

    private static async Task CreateTag(HttpClient client, string token, int projectId, IMessageSink sink)
    {
        // Check if tag exists
        var checkRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v4/projects/{projectId}/repository/tags/{TagName}");
        checkRequest.Headers.Add("PRIVATE-TOKEN", token);

        var checkResponse = await client.SendAsync(checkRequest);
        if (checkResponse.IsSuccessStatusCode) {
            Log(sink, $"Tag '{TagName}' already exists.");
            return;
        }

        // Create tag
        var tagBody = JsonSerializer.Serialize(new {
            tag_name = TagName,
            @ref = "main",
        });

        var createRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/api/v4/projects/{projectId}/repository/tags") {
            Content = new StringContent(tagBody, Encoding.UTF8, "application/json"),
        };
        createRequest.Headers.Add("PRIVATE-TOKEN", token);

        var createResponse = await client.SendAsync(createRequest);

        // GitLab may return 400 if the tag already exists
        if (createResponse.IsSuccessStatusCode) {
            Log(sink, $"Tag '{TagName}' created.");
        } else {
            var body = await createResponse.Content.ReadAsStringAsync();
            if (body.Contains("already exists")) {
                Log(sink, $"Tag '{TagName}' already exists.");
            } else {
                Log(sink, $"Tag creation returned {createResponse.StatusCode}: {body}");
                createResponse.EnsureSuccessStatusCode();
            }
        }
    }

    private static async Task CreateReleaseWithLink(
        HttpClient client, string token, int projectId, IMessageSink sink)
    {
        // Check if release exists
        var checkRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v4/projects/{projectId}/releases/{TagName}");
        checkRequest.Headers.Add("PRIVATE-TOKEN", token);

        var checkResponse = await client.SendAsync(checkRequest);
        if (checkResponse.IsSuccessStatusCode) {
            Log(sink, $"Release '{TagName}' already exists.");

            // Ensure the asset link exists even if release was already created
            await EnsureReleaseLink(client, token, projectId, sink);
            return;
        }

        // Create release
        var releaseBody = JsonSerializer.Serialize(new {
            tag_name = TagName,
            name = TagName,
            description = "Test release",
        });

        var createRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/api/v4/projects/{projectId}/releases") {
            Content = new StringContent(releaseBody, Encoding.UTF8, "application/json"),
        };
        createRequest.Headers.Add("PRIVATE-TOKEN", token);

        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();
        Log(sink, $"Release '{TagName}' created.");

        // Create release link to the generic package
        await EnsureReleaseLink(client, token, projectId, sink);
    }

    private static async Task EnsureReleaseLink(
        HttpClient client, string token, int projectId, IMessageSink sink)
    {
        var feedFileName = $"releases.{TestFeedData.Channel}.json";
        var packageUrl = $"{BaseUrl}/api/v4/projects/{projectId}/packages/generic/releases/1.0.0/{feedFileName}";

        // Check if link already exists
        var listRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v4/projects/{projectId}/releases/{TagName}/assets/links");
        listRequest.Headers.Add("PRIVATE-TOKEN", token);

        var listResponse = await client.SendAsync(listRequest);
        if (listResponse.IsSuccessStatusCode) {
            var listBody = await listResponse.Content.ReadAsStringAsync();
            using var listDoc = JsonDocument.Parse(listBody);
            foreach (var link in listDoc.RootElement.EnumerateArray()) {
                if (link.TryGetProperty("name", out var nameEl) && nameEl.GetString() == feedFileName) {
                    Log(sink, $"Release link for '{feedFileName}' already exists.");
                    return;
                }
            }
        }

        // Create release link
        var linkBody = JsonSerializer.Serialize(new {
            name = feedFileName,
            url = packageUrl,
            link_type = "other",
        });

        var linkRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/api/v4/projects/{projectId}/releases/{TagName}/assets/links") {
            Content = new StringContent(linkBody, Encoding.UTF8, "application/json"),
        };
        linkRequest.Headers.Add("PRIVATE-TOKEN", token);

        var linkResponse = await client.SendAsync(linkRequest);
        linkResponse.EnsureSuccessStatusCode();
        Log(sink, $"Release link for '{feedFileName}' created.");
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
