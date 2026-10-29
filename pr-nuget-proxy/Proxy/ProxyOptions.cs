using System.Text.Json;
using System.Text.RegularExpressions;

namespace PrNuGetProxy;

public sealed class ProxyOptions
{
    public string Repository { get; set; } = "velopack/velopack";
    public string Workflow { get; set; } = "build.yml";
    public string PackagesArtifact { get; set; } = "packages";
    public string VersionArtifact { get; set; } = "build-version";
    public int[] PullRequests { get; set; } = [];
    public long[] RunIds { get; set; } = [];
    public string BaseUrl { get; set; } = "http://localhost:55631";
    public int RefreshSeconds { get; set; } = 120;
    public int RunLimit { get; set; } = 100;
    public string CacheDirectory { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "pr-nuget-proxy", "cache");

    public bool IsValid()
    {
        return Repository is not null && Regex.IsMatch(Repository, @"^[A-Za-z0-9_.-]+/[A-Za-z0-9_.-]+$")
            && Workflow is not null && Regex.IsMatch(Workflow, @"^[A-Za-z0-9_.-]+$")
            && Uri.TryCreate(BaseUrl, UriKind.Absolute, out var url) && url.IsLoopback
            && url.Scheme is "http" or "https" && url.AbsolutePath == "/" && url.Query.Length == 0 && url.Fragment.Length == 0
            && !BaseUrl.EndsWith('/') && RefreshSeconds >= 10 && RunLimit is >= 1 and <= 500
            && PullRequests is not null && PullRequests.All(x => x > 0)
            && RunIds is not null && RunIds.All(x => x > 0)
            && !string.IsNullOrWhiteSpace(CacheDirectory)
            && !string.IsNullOrWhiteSpace(PackagesArtifact) && !string.IsNullOrWhiteSpace(VersionArtifact);
    }
}

public static class JsonSettings
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web) { WriteIndented = true };

    public static async Task WriteAtomicAsync<T>(string path, T value, CancellationToken cancellationToken)
    {
        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(value, Options), cancellationToken);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }
}
