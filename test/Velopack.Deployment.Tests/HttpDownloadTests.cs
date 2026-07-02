using Neovolve.Logging.Xunit;
using Velopack.Core;
using Velopack.Deployment;
using Velopack.Util;
using Xunit;

namespace Velopack.Deployment.Tests;

/// <summary>
/// Tests for <c>vpk download http</c> (<see cref="HttpDownloadCommandRunner"/>) against an in-process
/// <see cref="StaticFileServer"/>, which records every request so custom-header behavior can be asserted.
/// </summary>
[Collection("http")]
public class HttpDownloadTests
{
    // reuses the object-store suites' pack cache key (same id/channel/versions) so no extra packing occurs.
    private const string AppId = "ObjStoreApp";
    private const string Channel = "objstore";

    private readonly ITestOutputHelper _output;

    public HttpDownloadTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private ICacheLogger<HttpDownloadTests> CreateLogger() => _output.BuildLoggerFor<HttpDownloadTests>();

    private static Task RunDownloadAsync(ILogger log, string url, string releaseDir, string[]? headers = null, bool allowEmptyChannel = false)
        => new HttpDownloadCommandRunner(log).Run(new HttpDownloadOptions {
            Url = url,
            Channel = Channel,
            ReleaseDir = new DirectoryInfo(releaseDir),
            Headers = headers ?? [],
            AllowEmptyChannel = allowEmptyChannel,
        });

    [Fact]
    public async Task DownloadLatestFullPackage()
    {
        using var log = CreateLogger();
        var pack = ReleaseFixtures.GetCachedPack(AppId, Channel, log, "1.0.0");
        try {
            using var server = new StaticFileServer(pack.ReleaseDir, log);
            using var _1 = TempUtil.GetTempDirectory(out var downloadDir);

            await RunDownloadAsync(log, server.BaseUrl, downloadDir);

            var downloaded = Path.Combine(downloadDir, $"{AppId}-1.0.0-{Channel}-full.nupkg");
            Assert.True(File.Exists(downloaded), $"Expected downloaded package at {downloaded}");
            Assert.Contains(server.Requests, r => r.PathAndQuery.Contains($"releases.{Channel}.json"));
            Assert.Contains(server.Requests, r => r.PathAndQuery.Contains($"{AppId}-1.0.0-{Channel}-full.nupkg"));

            // re-running must short-circuit on the verified existing file (no re-download)
            var requestCount = server.Requests.Count;
            await RunDownloadAsync(log, server.BaseUrl, downloadDir);
            Assert.DoesNotContain(
                server.Requests.Skip(requestCount),
                r => r.PathAndQuery.Contains($"{AppId}-1.0.0-{Channel}-full.nupkg"));
        } finally {
            try { Directory.Delete(pack.ReleaseDir, true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task CustomHeadersAreSent()
    {
        using var log = CreateLogger();
        var pack = ReleaseFixtures.GetCachedPack(AppId, Channel, log, "1.0.0");
        try {
            using var server = new StaticFileServer(pack.ReleaseDir, log);
            using var _1 = TempUtil.GetTempDirectory(out var downloadDir);

            await RunDownloadAsync(
                log,
                server.BaseUrl,
                downloadDir,
                headers: ["X-Velopack-Test: header-value-123", "Authorization: Bearer secret-token"]);

            Assert.True(File.Exists(Path.Combine(downloadDir, $"{AppId}-1.0.0-{Channel}-full.nupkg")));

            // every request (feed + package) must carry the custom headers
            var relevant = server.Requests
                .Where(r => r.PathAndQuery.Contains($"releases.{Channel}.json") || r.PathAndQuery.Contains("full.nupkg"))
                .ToArray();
            Assert.NotEmpty(relevant);
            Assert.All(
                relevant,
                r => {
                    Assert.True(r.Headers.TryGetValue("X-Velopack-Test", out var custom), "X-Velopack-Test header missing");
                    Assert.Equal("header-value-123", custom);
                    Assert.True(r.Headers.TryGetValue("Authorization", out var auth), "Authorization header missing");
                    Assert.Equal("Bearer secret-token", auth);
                });
        } finally {
            try { Directory.Delete(pack.ReleaseDir, true); } catch { /* best-effort */ }
        }
    }

    [Fact]
    public async Task AllowEmptyChannelTolerates404()
    {
        using var log = CreateLogger();
        using var _1 = TempUtil.GetTempDirectory(out var emptyDir); // nothing in here -> feed request 404s
        using var server = new StaticFileServer(emptyDir, log);
        using var _2 = TempUtil.GetTempDirectory(out var downloadDir);

        // must not throw, must log a warning and produce no package file
        await RunDownloadAsync(log, server.BaseUrl, downloadDir, allowEmptyChannel: true);

        Assert.Contains(log.Entries, e => e.LogLevel == LogLevel.Warning && e.Message.Contains("allowEmptyChannel"));
        Assert.Empty(Directory.GetFiles(downloadDir, "*.nupkg"));
    }

    [Fact]
    public async Task MalformedHeaderThrowsUserInfoException()
    {
        using var log = CreateLogger();
        using var _1 = TempUtil.GetTempDirectory(out var downloadDir);

        var ex = await Assert.ThrowsAsync<UserInfoException>(
            () => RunDownloadAsync(log, "http://127.0.0.1:9/", downloadDir, headers: ["ThisHeaderHasNoColon"]));
        Assert.Contains("Name: Value", ex.Message);
    }
}
