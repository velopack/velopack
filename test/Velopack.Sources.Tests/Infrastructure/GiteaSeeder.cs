using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Xunit.Sdk;
using Xunit.v3;

namespace Velopack.Sources.Tests.Infrastructure;

public static class GiteaSeeder
{
    private const string BaseUrl = "http://localhost:3000";
    private const string Username = "testadmin";
    private const string Password = "testpassword123!";
    private const string Email = "test@test.com";
    private const string RepoName = "testrepo";
    private const string TokenName = "test-token";

    private static readonly string[] TagNames = ["v1.0.1", "v1.0.2", "v1.0.3"];

    public static async Task<(string repoUrl, string token)> SeedAsync(IMessageSink sink)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        await CreateUser(client, sink);
        var token = await CreateApiToken(client, sink);
        await CreateRepository(client, token, sink);
        await DeleteStaleReleases(client, token, sink);

        foreach (var tag in TagNames) {
            var version = tag[1..]; // strip 'v' prefix
            await CreateRelease(client, token, tag, version, sink);
        }

        var repoUrl = $"{BaseUrl}/{Username}/{RepoName}";
        return (repoUrl, token);
    }

    private static async Task CreateUser(HttpClient client, IMessageSink sink)
    {
        try {
            var checkRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v1/user");
            checkRequest.Headers.Authorization = new AuthenticationHeaderValue(
                "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}")));
            var checkResponse = await client.SendAsync(checkRequest);
            if (checkResponse.IsSuccessStatusCode) {
                Log(sink, "Admin user already exists.");
                return;
            }
        } catch {
        }

        var formData = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["user_name"] = Username,
            ["email"] = Email,
            ["password"] = Password,
            ["retype"] = Password,
        });

        var response = await client.PostAsync($"{BaseUrl}/user/sign_up", formData);

        if (response.StatusCode == HttpStatusCode.OK
            || response.StatusCode == HttpStatusCode.Found
            || response.StatusCode == HttpStatusCode.SeeOther) {
            Log(sink, "Admin user created via sign-up.");
        } else {
            var body = await response.Content.ReadAsStringAsync();
            if (body.Contains("already been taken") || body.Contains("already exists")) {
                Log(sink, "Admin user already exists (detected from sign-up response).");
            } else {
                Log(sink, $"User sign-up returned {response.StatusCode}. Body (truncated): {body[..Math.Min(body.Length, 500)]}");
            }
        }
    }

    private static async Task<string> CreateApiToken(HttpClient client, IMessageSink sink)
    {
        var authHeader = new AuthenticationHeaderValue(
            "Basic", Convert.ToBase64String(Encoding.UTF8.GetBytes($"{Username}:{Password}")));

        var listRequest = new HttpRequestMessage(HttpMethod.Get, $"{BaseUrl}/api/v1/users/{Username}/tokens");
        listRequest.Headers.Authorization = authHeader;
        var listResponse = await client.SendAsync(listRequest);

        if (listResponse.IsSuccessStatusCode) {
            var listBody = await listResponse.Content.ReadAsStringAsync();
            using var listDoc = JsonDocument.Parse(listBody);
            foreach (var tokenObj in listDoc.RootElement.EnumerateArray()) {
                if (tokenObj.TryGetProperty("name", out var nameEl) && nameEl.GetString() == TokenName) {
                    var tokenId = tokenObj.GetProperty("id").GetInt64();
                    var deleteRequest = new HttpRequestMessage(HttpMethod.Delete,
                        $"{BaseUrl}/api/v1/users/{Username}/tokens/{tokenId}");
                    deleteRequest.Headers.Authorization = authHeader;
                    await client.SendAsync(deleteRequest);
                    Log(sink, $"Deleted existing token '{TokenName}' (id={tokenId}).");
                    break;
                }
            }
        }

        var createBody = JsonSerializer.Serialize(new {
            name = TokenName,
            scopes = new[] { "all" },
        });

        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v1/users/{Username}/tokens") {
            Content = new StringContent(createBody, Encoding.UTF8, "application/json"),
        };
        createRequest.Headers.Authorization = authHeader;

        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();

        var responseBody = await createResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseBody);

        var token = doc.RootElement.TryGetProperty("sha1", out var sha1El)
            ? sha1El.GetString()!
            : doc.RootElement.GetProperty("token").GetString()!;

        Log(sink, "API token created.");
        return token;
    }

    private static async Task CreateRepository(HttpClient client, string token, IMessageSink sink)
    {
        var checkRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v1/repos/{Username}/{RepoName}");
        checkRequest.Headers.Authorization = new AuthenticationHeaderValue("token", token);

        var checkResponse = await client.SendAsync(checkRequest);
        if (checkResponse.IsSuccessStatusCode) {
            Log(sink, $"Repository '{RepoName}' already exists.");
            return;
        }

        var body = JsonSerializer.Serialize(new {
            name = RepoName,
            auto_init = true,
        });

        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/api/v1/user/repos") {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("token", token);

        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();
        Log(sink, $"Repository '{RepoName}' created.");
    }

    private static async Task DeleteStaleReleases(HttpClient client, string token, IMessageSink sink)
    {
        var auth = new AuthenticationHeaderValue("token", token);

        var listRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v1/repos/{Username}/{RepoName}/releases?limit=50");
        listRequest.Headers.Authorization = auth;

        var listResponse = await client.SendAsync(listRequest);
        if (!listResponse.IsSuccessStatusCode) return;

        var body = await listResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        foreach (var release in doc.RootElement.EnumerateArray()) {
            var tag = release.GetProperty("tag_name").GetString();
            if (tag != null && !TagNames.Contains(tag)) {
                var releaseId = release.GetProperty("id").GetInt64();
                var deleteRequest = new HttpRequestMessage(HttpMethod.Delete,
                    $"{BaseUrl}/api/v1/repos/{Username}/{RepoName}/releases/{releaseId}");
                deleteRequest.Headers.Authorization = auth;
                await client.SendAsync(deleteRequest);
                Log(sink, $"Deleted stale release '{tag}' (id={releaseId}).");
            }
        }
    }

    private static async Task CreateRelease(
        HttpClient client, string token, string tagName, string version, IMessageSink sink)
    {
        var auth = new AuthenticationHeaderValue("token", token);

        // Check if release already exists
        var checkRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v1/repos/{Username}/{RepoName}/releases/tags/{tagName}");
        checkRequest.Headers.Authorization = auth;

        var checkResponse = await client.SendAsync(checkRequest);
        if (checkResponse.IsSuccessStatusCode) {
            Log(sink, $"Release '{tagName}' already exists.");
            return;
        }

        // Create release
        var releaseBody = JsonSerializer.Serialize(new {
            tag_name = tagName,
            name = tagName,
            body = $"Test release {tagName}",
        });

        var createRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/api/v1/repos/{Username}/{RepoName}/releases") {
            Content = new StringContent(releaseBody, Encoding.UTF8, "application/json"),
        };
        createRequest.Headers.Authorization = auth;

        var createResponse = await client.SendAsync(createRequest);
        createResponse.EnsureSuccessStatusCode();

        var createResponseBody = await createResponse.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(createResponseBody);
        var releaseId = doc.RootElement.GetProperty("id").GetInt64();
        Log(sink, $"Release '{tagName}' created (id={releaseId}).");

        // Upload per-version feed JSON as release asset
        var feedFileName = $"releases.{TestFeedData.Channel}.json";
        var feedContent = Encoding.UTF8.GetBytes(TestFeedData.FeedJsonForVersion(version));

        using var multipartContent = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(feedContent);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("application/json");
        multipartContent.Add(fileContent, "attachment", feedFileName);

        var uploadRequest = new HttpRequestMessage(HttpMethod.Post,
            $"{BaseUrl}/api/v1/repos/{Username}/{RepoName}/releases/{releaseId}/assets?name={feedFileName}") {
            Content = multipartContent,
        };
        uploadRequest.Headers.Authorization = auth;

        var uploadResponse = await client.SendAsync(uploadRequest);
        uploadResponse.EnsureSuccessStatusCode();
        Log(sink, $"Feed asset '{feedFileName}' uploaded to release '{tagName}'.");
    }

    private static void Log(IMessageSink sink, string message)
    {
        sink.OnMessage(new DiagnosticMessage($"[GiteaSeeder] {message}"));
    }
}
