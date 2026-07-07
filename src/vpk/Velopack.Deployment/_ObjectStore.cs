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

public sealed class RemoteObjectInfo
{
    /// <summary> MD5 checksum of the remote object as a lowercase hex string. </summary>
    public string Md5Hex { get; init; }

    /// <summary> Provider-specific version identifier of the remote object (eg. S3 versioning), may be null. </summary>
    public string VersionId { get; init; }
}

public abstract class ObjectStoreClient(ILogger logger) : IObjectStoreClient
{
    protected ILogger Log { get; } = logger;

    public async Task UploadObject(string key, FileInfo file, bool overwriteRemote, bool noCache)
    {
        RemoteObjectInfo remote = null;

        // try to detect an existing remote file of the same name
        try {
            remote = await GetRemoteObjectInfoAsync(key).ConfigureAwait(false);
        } catch (Exception ex) when (IsNotFoundException(ex)) {
            // remote object does not exist, so we can proceed with the upload
        } catch (Exception ex) {
            // worst case, we end up re-uploading a file that already exists. storage
            // providers should prefer the newer file of the same name.
            Log.Debug(ex, $"Failed to check for an existing remote object '{key}'.");
        }

        bool replacing = false;
        if (remote != null) {
            var localMd5 = Convert.ToHexString(ObjectStoreUtil.GetFileMD5Checksum(file.FullName)).ToLowerInvariant();
            if (string.Equals(remote.Md5Hex, localMd5, StringComparison.OrdinalIgnoreCase)) {
                Log.Info($"Upload file '{key}' skipped (already exists in remote)");
                return;
            } else if (overwriteRemote) {
                Log.Info($"File '{key}' exists in remote, replacing...");
                replacing = true;
            } else {
                Log.Warn($"File '{key}' exists in remote and checksum does not match local file. Use 'overwrite' argument to replace remote file.");
                return;
            }
        }

        await Retry.RetryAsync(
            Log,
            () => UploadObjectCoreAsync(key, file, overwriteRemote, noCache),
            "Uploading " + key + (noCache ? " (no-cache)" : ""));

        if (replacing) {
            try {
                await AfterObjectReplacedAsync(key, remote).ConfigureAwait(false);
            } catch (Exception ex) {
                Log.Warn(ex, $"Failed to clean up the replaced remote object '{key}'.");
            }
        }
    }

    public Task DeleteObject(string key)
    {
        return Retry.RetryAsync(Log, () => DeleteObjectCoreAsync(key), "Deleting " + key);
    }

    public async Task<byte[]> GetObjectBytes(string key)
    {
        return await Retry.RetryAsyncRet(
            Log,
            async () => {
                try {
                    return await GetObjectBytesCoreAsync(key).ConfigureAwait(false);
                } catch (Exception ex) when (IsNotFoundException(ex)) {
                    return null;
                }
            },
            $"Downloading {key}...");
    }

    public Task DownloadToFile(string key, string filePath)
    {
        return Retry.RetryAsync(Log, () => DownloadToFileCoreAsync(key, filePath), $"Downloading {key}...");
    }

    /// <summary> Returns checksum info for the remote object, or null if it does not exist or has no checksum. May also throw a not-found exception. </summary>
    protected abstract Task<RemoteObjectInfo> GetRemoteObjectInfoAsync(string key);

    protected abstract Task UploadObjectCoreAsync(string key, FileInfo file, bool overwriteRemote, bool noCache);

    protected abstract Task<byte[]> GetObjectBytesCoreAsync(string key);

    protected abstract Task DownloadToFileCoreAsync(string key, string filePath);

    protected abstract Task DeleteObjectCoreAsync(string key);

    /// <summary> Returns true if the exception indicates that the requested object does not exist. </summary>
    protected abstract bool IsNotFoundException(Exception ex);

    /// <summary> Called after an existing remote object was overwritten, eg. to clean up an old object version. </summary>
    protected virtual Task AfterObjectReplacedAsync(string key, RemoteObjectInfo replaced) => Task.CompletedTask;
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

        var remoteReleases = await GetReleasesAsync(options, client);
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

    protected virtual Task<VelopackAssetFeed> GetReleasesAsync(TOpt options, IObjectStoreClient client)
    {
        return ObjectStoreUtil.GetReleaseFeedAsync(client, options.Channel);
    }

    protected abstract IObjectStoreClient CreateClient(TOpt options);
}
