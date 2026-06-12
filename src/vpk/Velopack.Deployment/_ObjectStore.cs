using System.Text;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Packaging;
using Velopack.Util;

namespace Velopack.Deployment;

public interface IObjectUploadOptions
{
    public int KeepMaxReleases { get; set; }
}

public interface IObjectStoreClient
{
    Task UploadObject(string key, FileInfo file, bool overwriteRemote, bool noCache);

    Task DeleteObject(string key);

    Task<byte[]> GetObjectBytes(string key);

    Task DownloadToFile(string key, string filePath);
}

internal static class ObjectStoreUtil
{
    public static string NormalizePrefix(string prefix)
    {
        prefix = prefix?.Trim() ?? "";
        if (prefix.Length > 0 && !prefix.EndsWith("/")) {
            prefix += "/";
        }

        return prefix;
    }

    public static byte[] GetFileMD5Checksum(string filePath)
    {
        using var md5 = System.Security.Cryptography.MD5.Create();
        using var fs = File.OpenRead(filePath);
        return md5.ComputeHash(fs);
    }

    public static async Task<VelopackAssetFeed> GetReleaseFeedAsync(IObjectStoreClient client, string channel)
    {
        var releasesName = CoreUtil.GetVeloReleaseIndexName(channel);
        var bytes = await client.GetObjectBytes(releasesName);
        if (bytes == null || bytes.Length == 0) {
            return new VelopackAssetFeed();
        }
        return VelopackAssetFeed.FromJson(Encoding.UTF8.GetString(bytes));
    }
}

public abstract class ObjectDownloadCommandRunner<TOpt, TValidator>(ILogger logger) : DownloadCommandRunner<TOpt, TValidator>(logger)
    where TOpt : RepositoryOptions
    where TValidator : IValidator<TOpt>, new()
{
    protected override Task<VelopackAssetFeed> GetReleasesAsync(TOpt options)
    {
        var client = CreateClient(options);
        return ObjectStoreUtil.GetReleaseFeedAsync(client, options.Channel);
    }

    protected override Task SaveEntryToFileAsync(TOpt options, VelopackAsset entry, string filePath)
    {
        var client = CreateClient(options);
        return client.DownloadToFile(entry.FileName, filePath);
    }

    protected abstract IObjectStoreClient CreateClient(TOpt options);
}

public abstract class ObjectUploadCommandRunner<TOpt, TValidator>(ILogger logger) : ValidatedCommand<TOpt, TValidator>
    where TOpt : RepositoryOptions, IObjectUploadOptions
    where TValidator : IValidator<TOpt>, new()
{
    protected ILogger Log { get; } = logger;

    protected override async Task RunCoreAsync(TOpt options)
    {
        var build = BuildAssets.Read(options.ReleaseDir.FullName, options.Channel);
        var client = CreateClient(options);

        Log.Info($"Preparing to upload {build.Count} local asset(s).");

        var remoteReleases = await GetReleasesAsync(options);
        Log.Info($"There are {remoteReleases.Assets.Length} asset(s) in remote releases file.");

        var localEntries = await build.GetReleaseEntriesAsync().ConfigureAwait(false);
        var releaseEntries = ReleaseEntryHelper.MergeAssets(localEntries, remoteReleases.Assets).ToArray();

        Log.Info($"{releaseEntries.Length} merged local/remote release(s).");

        var toDelete = new VelopackAsset[0];

        if (options.KeepMaxReleases > 0) {
            var fullReleases = releaseEntries
                .OrderByDescending(x => x.Version)
                .Where(x => x.Type == VelopackAssetType.Full)
                .ToArray();
            if (fullReleases.Length > options.KeepMaxReleases) {
                var minVersion = fullReleases[options.KeepMaxReleases - 1].Version;
                toDelete = releaseEntries
                    .Where(x => x.Version < minVersion)
                    .ToArray();
                releaseEntries = releaseEntries.Except(toDelete).ToArray();
                Log.Info($"Retention policy (keepMaxReleases={options.KeepMaxReleases}) will delete {toDelete.Length} release(s).");
            } else {
                Log.Info($"Retention policy (keepMaxReleases={options.KeepMaxReleases}) will not be applied, because there will only be {fullReleases.Length} full release(s) when this upload has completed.");
            }
        }

        foreach (var asset in build.GetFilePaths()) {
            await client.UploadObject(Path.GetFileName(asset), new FileInfo(asset), true, noCache: false);
        }

        var newReleaseFeed = new VelopackAssetFeed { Assets = releaseEntries };

        using var _1 = TempUtil.GetTempFileName(out var tmpReleases);
        File.WriteAllText(tmpReleases, ReleaseEntryHelper.GetAssetFeedJson(newReleaseFeed));
        var releasesName = CoreUtil.GetVeloReleaseIndexName(options.Channel);
        await client.UploadObject(releasesName, new FileInfo(tmpReleases), true, noCache: true);

#pragma warning disable CS0612 // Type or member is obsolete
        var legacyKey = CoreUtil.GetReleasesFileName(options.Channel);
#pragma warning restore CS0612 // Type or member is obsolete
        using var _2 = TempUtil.GetTempFileName(out var tmpReleases2);
        File.WriteAllText(tmpReleases2, ReleaseEntryHelper.GetLegacyMigrationReleaseFeedString(newReleaseFeed));
        await client.UploadObject(legacyKey, new FileInfo(tmpReleases2), true, noCache: true);

        if (toDelete.Length > 0) {
            Log.Info($"Retention policy about to delete {toDelete.Length} release(s)...");
            foreach (var del in toDelete) {
                await client.DeleteObject(del.FileName);
            }
        }

        Log.Info("Done.");
    }

    protected virtual Task<VelopackAssetFeed> GetReleasesAsync(TOpt options)
    {
        var client = CreateClient(options);
        return ObjectStoreUtil.GetReleaseFeedAsync(client, options.Channel);
    }

    protected abstract IObjectStoreClient CreateClient(TOpt options);
}
