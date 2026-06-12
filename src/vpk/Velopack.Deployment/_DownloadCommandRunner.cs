using FluentValidation;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Sources;
using Velopack.Util;

namespace Velopack.Deployment;

public abstract class DownloadCommandRunner<TOpt, TValidator>(ILogger logger) : ValidatedCommand<TOpt, TValidator>
    where TOpt : RepositoryOptions
    where TValidator : IValidator<TOpt>, new()
{
    protected ILogger Log { get; } = logger;

    protected override async Task RunCoreAsync(TOpt options)
    {
        VelopackAssetFeed feed = await Retry.RetryAsyncRet(Log, () => GetReleasesAsync(options), $"Fetching releases for channel {options.Channel}...");
        var releases = feed.Assets;

        Log.Info($"Found {releases.Length} release(s) in remote file");

        var latest = releases.Where(r => r.Type == VelopackAssetType.Full).OrderByDescending(r => r.Version).FirstOrDefault();
        if (latest == null) {
            Log.Warn("No full / applicable release was found to download. Aborting.");
            return;
        }

        options.ReleaseDir.Create();
        var path = Path.Combine(options.ReleaseDir.FullName, latest.FileName);
        var incomplete = Path.Combine(options.ReleaseDir.FullName, latest.FileName + ".incomplete");

        if (File.Exists(path)) {
            Log.Warn($"File '{path}' already exists on disk. Verifying checksum...");

            var (existingSha1, existingSha256) = await IoUtil.CalculateFileSHA1AndSHA256Async(path).ConfigureAwait(false);
            bool hashMatch = (latest.SHA256 != null)
                ? latest.SHA256 == existingSha256
                : latest.SHA1 == existingSha1;

            if (hashMatch) {
                Log.Info("Checksum matches. Finished.");
                return;
            } else {
                Log.Info($"Checksum mismatch, re-downloading...");
            }
        }

        await Retry.RetryAsync(Log, () => SaveEntryToFileAsync(options, latest, incomplete), $"Downloading {latest.FileName}...");

        Log.Info("Verifying checksum...");
        var (dlSha1, dlSha256) = await IoUtil.CalculateFileSHA1AndSHA256Async(incomplete).ConfigureAwait(false);
        if (!string.IsNullOrEmpty(latest.SHA256)) {
            if (latest.SHA256 != dlSha256) {
                Log.Error($"Checksum mismatch, expected {latest.SHA256}, got {dlSha256}");
                return;
            }
        } else if (latest.SHA1 != dlSha1) {
            Log.Error($"Checksum mismatch, expected {latest.SHA1}, got {dlSha1}");
            return;
        }

        File.Move(incomplete, path, true);
        Log.Info("Finished.");
    }

    protected abstract Task<VelopackAssetFeed> GetReleasesAsync(TOpt options);

    protected abstract Task SaveEntryToFileAsync(TOpt options, VelopackAsset entry, string filePath);
}

public abstract class SourceDownloadCommandRunner<TOpt, TValidator>(ILogger logger) : DownloadCommandRunner<TOpt, TValidator>(logger)
    where TOpt : RepositoryOptions
    where TValidator : IValidator<TOpt>, new()
{
    protected override Task<VelopackAssetFeed> GetReleasesAsync(TOpt options)
    {
        var source = CreateSource(options);
        return source.GetReleaseFeed(Log.ToVelopackLogger(), null, options.Channel);
    }

    protected override Task SaveEntryToFileAsync(TOpt options, VelopackAsset entry, string filePath)
    {
        var source = CreateSource(options);
        return source.DownloadReleaseEntry(Log.ToVelopackLogger(), entry, filePath, (i) => { });
    }

    protected abstract IUpdateSource CreateSource(TOpt options);
}
