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
    private const string TagName = "v2.0.0";
    private const string TokenName = "test-token";

    public static async Task<(string repoUrl, string token)> SeedAsync(IMessageSink sink)
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };

        // Step 1: Create the admin user via sign-up form (first user becomes admin)
        await CreateUser(client, sink);

        // Step 2: Create API token
        var token = await CreateApiToken(client, sink);

        // Step 3: Create repository
        await CreateRepository(client, token, sink);

        // Step 4: Create release and upload feed asset
        await CreateReleaseWithAsset(client, token, sink);

        var repoUrl = $"{BaseUrl}/{Username}/{RepoName}";
        return (repoUrl, token);
    }

    private static async Task CreateUser(HttpClient client, IMessageSink sink)
    {
        // Check if user already exists by trying to authenticate
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
            // User doesn't exist yet, proceed with creation
        }

        // Register via the web sign-up form (first user gets admin privileges with INSTALL_LOCK=true)
        var formData = new FormUrlEncodedContent(new Dictionary<string, string> {
            ["user_name"] = Username,
            ["email"] = Email,
            ["password"] = Password,
            ["retype"] = Password,
        });

        var response = await client.PostAsync($"{BaseUrl}/user/sign_up", formData);

        // Gitea may return 302 (redirect) on successful sign-up, or 200
        if (response.StatusCode == HttpStatusCode.OK
            || response.StatusCode == HttpStatusCode.Found
            || response.StatusCode == HttpStatusCode.SeeOther) {
            Log(sink, "Admin user created via sign-up.");
        } else {
            var body = await response.Content.ReadAsStringAsync();
            // If the user already exists, the form may return a page with an error
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

        // Delete existing token with the same name (if any) to ensure idempotency
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

        // Create a new token
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

        // xUnit v3 Gitea returns "sha1" for the token value
        var token = doc.RootElement.TryGetProperty("sha1", out var sha1El)
            ? sha1El.GetString()!
            : doc.RootElement.GetProperty("token").GetString()!;

        Log(sink, "API token created.");
        return token;
    }

    private static async Task CreateRepository(HttpClient client, string token, IMessageSink sink)
    {
        // Check if repo exists
        var checkRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v1/repos/{Username}/{RepoName}");
        checkRequest.Headers.Authorization = new AuthenticationHeaderValue("token", token);

        var checkResponse = await client.SendAsync(checkRequest);
        if (checkResponse.IsSuccessStatusCode) {
            Log(sink, $"Repository '{RepoName}' already exists.");
            return;
        }

        // Create repo
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

    private static async Task CreateReleaseWithAsset(HttpClient client, string token, IMessageSink sink)
    {
        var auth = new AuthenticationHeaderValue("token", token);

        // Check if release already exists
        var checkRequest = new HttpRequestMessage(HttpMethod.Get,
            $"{BaseUrl}/api/v1/repos/{Username}/{RepoName}/releases/tags/{TagName}");
        checkRequest.Headers.Authorization = auth;

        var checkResponse = await client.SendAsync(checkRequest);
        if (checkResponse.IsSuccessStatusCode) {
            Log(sink, $"Release '{TagName}' already exists.");
            return;
        }

        // Create release
        var releaseBody = JsonSerializer.Serialize(new {
            tag_name = TagName,
            name = TagName,
            body = "Test release",
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
        Log(sink, $"Release '{TagName}' created (id={releaseId}).");

        // Upload feed JSON as release asset
        var feedFileName = $"releases.{TestFeedData.Channel}.json";
        var feedContent = Encoding.UTF8.GetBytes(TestFeedData.FeedJson);

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
        Log(sink, $"Feed asset '{feedFileName}' uploaded to release.");
    }

    private static void Log(IMessageSink sink, string message)
    {
        sink.OnMessage(new DiagnosticMessage($"[GiteaSeeder] {message}"));
    }
}
