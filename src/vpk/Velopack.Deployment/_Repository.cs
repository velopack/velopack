using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Packaging.Abstractions;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack.Deployment;

public class RepositoryOptions : IOutputOptions
{
    private string _channel;

    public RuntimeOs TargetOs { get; set; }

    public string Channel {
        get => _channel ?? DefaultName.GetDefaultChannel(TargetOs);
        set => _channel = value;
    }

    public DirectoryInfo ReleaseDir { get; set; }

    public double Timeout { get; set; } = 30d;
}

public interface IRepositoryCanUpload<TUp> where TUp : RepositoryOptions
{
    Task UploadMissingAssetsAsync(TUp options);
}

public interface IRepositoryCanDownload<TDown> where TDown : RepositoryOptions
{
    Task DownloadLatestFullPackageAsync(TDown options);
}

public abstract class SourceRepository<TDown, TSource>(ILogger logger) : DownRepository<TDown>(logger)
    where TDown : RepositoryOptions
    where TSource : IUpdateSource
{
    protected override Task<VelopackAssetFeed> GetReleasesAsync(TDown options)
    {
        var source = CreateSource(options);
        return source.GetReleaseFeed(Log.ToVelopackLogger(), null, options.Channel);
    }

    protected override Task SaveEntryToFileAsync(TDown options, VelopackAsset entry, string filePath)
    {
        var source = CreateSource(options);
        return source.DownloadReleaseEntry(Log.ToVelopackLogger(), entry, filePath, (i) => { });
    }

    public abstract TSource CreateSource(TDown options);
}

public abstract class DownRepository<TDown> : IRepositoryCanDownload<TDown>
    where TDown : RepositoryOptions
{
    protected ILogger Log { get; }

    public DownRepository(ILogger logger)
    {
        Log = logger;
    }

    public virtual async Task DownloadLatestFullPackageAsync(TDown options)
    {
        VelopackAssetFeed feed = await RetryAsyncRet(() => GetReleasesAsync(options), $"Fetching releases for channel {options.Channel}...");
        var releases = feed.Assets;

        Log.Info($"Found {releases.Length} release(s) in remote file");

        var latest = releases.Where(r => r.Type == VelopackAssetType.Full).OrderByDescending(r => r.Version).FirstOrDefault();
        if (latest == null) {
            Log.Warn("No full / applicable release was found to download. Aborting.");
            return;
        }

        var path = Path.Combine(options.ReleaseDir.FullName, latest.FileName);
        var incomplete = Path.Combine(options.ReleaseDir.FullName, latest.FileName + ".incomplete");

        if (File.Exists(path)) {
            Log.Warn($"File '{path}' already exists on disk. Verifying checksum...");

            bool hashMatch = (latest.SHA256 != null)
                ? latest.SHA256 == IoUtil.CalculateFileSHA256(path)
                : latest.SHA1 == IoUtil.CalculateFileSHA1(path);

            if (hashMatch) {
                Log.Info("Checksum matches. Finished.");
                return;
            } else {
                Log.Info($"Checksum mismatch, re-downloading...");
            }
        }

        await RetryAsync(() => SaveEntryToFileAsync(options, latest, incomplete), $"Downloading {latest.FileName}...");

        Log.Info("Verifying checksum...");
        string newHash;
        if (!string.IsNullOrEmpty(latest.SHA256)) {
            if (latest.SHA256 != (newHash = IoUtil.CalculateFileSHA256(incomplete))) {
                Log.Error($"Checksum mismatch, expected {latest.SHA256}, got {newHash}");
                return;
            }
        } else if (latest.SHA1 != (newHash = IoUtil.CalculateFileSHA1(incomplete))) {
            Log.Error($"Checksum mismatch, expected {latest.SHA1}, got {newHash}");
            return;
        }

        File.Move(incomplete, path, true);
        Log.Info("Finished.");
    }

    protected abstract Task<VelopackAssetFeed> GetReleasesAsync(TDown options);

    protected abstract Task SaveEntryToFileAsync(TDown options, VelopackAsset entry, string filePath);

    protected async Task<T> RetryAsyncRet<T>(Func<Task<T>> block, string message, int maxRetries = 1)
    {
        int ctry = 0;
        while (true) {
            try {
                Log.Info((ctry > 0 ? $"(retry {ctry}) " : "") + message);
                return await block().ConfigureAwait(false);
            } catch (Exception ex) {
                if (ctry++ > maxRetries) {
                    Log.Error(ex.Message + ", will not try again.");
                    throw;
                }

                Log.Error($"{ex.Message}, retrying in 1 second.");
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }

    protected async Task RetryAsync(Func<Task> block, string message, int maxRetries = 1)
    {
        int ctry = 0;
        while (true) {
            try {
                Log.Info((ctry > 0 ? $"(retry {ctry}) " : "") + message);
                await block().ConfigureAwait(false);
                return;
            } catch (Exception ex) {
                if (ctry++ > maxRetries) {
                    Log.Error(ex.Message + ", will not try again.");
                    throw;
                }

                Log.Error($"{ex.Message}, retrying in 1 second.");
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }
}