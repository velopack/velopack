using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Velopack.Deployment;

public class HttpDownloadOptions
{
    public DirectoryInfo ReleaseDir { get; set; }
    public string Url { get; set; }
}

public class SimpleWebRepository
{
    private readonly ILogger _logger;

    public SimpleWebRepository(ILogger logger)
    {
        _logger = logger;
    }

    public async Task DownloadRecentPackages(HttpDownloadOptions options)
    {
        var uri = new Uri(options.Url);
        var releasesDir = options.ReleaseDir;
        var releasesUri = Utility.AppendPathToUri(uri, "RELEASES");
        var releasesIndex = await retryAsync(3, () => downloadReleasesIndex(releasesUri));

        File.WriteAllText(Path.Combine(releasesDir.FullName, "RELEASES"), releasesIndex);

        var releasesToDownload = ReleaseEntry.ParseReleaseFile(releasesIndex)
            .Where(x => !x.IsDelta)
            .OrderByDescending(x => x.Version)
            .Take(1)
            .Select(x => new {
                LocalPath = Path.Combine(releasesDir.FullName, x.OriginalFilename),
                RemoteUrl = new Uri(Utility.EnsureTrailingSlash(uri), x.BaseUrl + x.OriginalFilename + x.Query)
            });

        foreach (var releaseToDownload in releasesToDownload) {
            await retryAsync(3, () => downloadRelease(releaseToDownload.LocalPath, releaseToDownload.RemoteUrl));
        }
    }

    async Task<string> downloadReleasesIndex(Uri uri)
    {
        _logger.Info($"Trying to download RELEASES index from {uri}");

        var userAgent = new System.Net.Http.Headers.ProductInfoHeaderValue("Velopack", Assembly.GetExecutingAssembly().GetName().Version.ToString());
        using (HttpClient client = new HttpClient()) {
            client.DefaultRequestHeaders.UserAgent.Add(userAgent);
            return await client.GetStringAsync(uri);
        }
    }

    async Task downloadRelease(string localPath, Uri remoteUrl)
    {
        if (File.Exists(localPath)) {
            File.Delete(localPath);
        }

        _logger.Info($"Downloading release from {remoteUrl}");
        var wc = Utility.CreateDefaultDownloader();
        await wc.DownloadFile(remoteUrl.ToString(), localPath, null);
    }

    static async Task<T> retryAsync<T>(int count, Func<Task<T>> block)
    {
        int retryCount = count;

    retry:
        try {
            return await block();
        } catch (Exception) {
            retryCount--;
            if (retryCount >= 0) goto retry;

            throw;
        }
    }

    static async Task retryAsync(int count, Func<Task> block)
    {
        await retryAsync(count, async () => { await block(); return false; });
    }
}
