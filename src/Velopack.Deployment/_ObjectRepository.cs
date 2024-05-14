using System.Text;
using Microsoft.Extensions.Logging;
using Velopack.Packaging;

namespace Velopack.Deployment;

public interface IObjectUploadOptions
{
    public int KeepMaxReleases { get; set; }
}

public interface IObjectDownloadOptions
{
}

public abstract class ObjectRepository<TDown, TUp, TClient> : DownRepository<TDown>, IRepositoryCanUpload<TUp>
    where TDown : RepositoryOptions, IObjectDownloadOptions
    where TUp : IObjectUploadOptions, TDown
{
    protected ObjectRepository(ILogger logger) : base(logger)
    {
    }

    protected abstract Task UploadObject(TClient client, string key, FileInfo f, bool overwriteRemote, bool noCache);
    protected abstract Task DeleteObject(TClient client, string key);
    protected abstract Task<byte[]> GetObjectBytes(TClient client, string key);
    protected abstract TClient CreateClient(TDown options);

    protected byte[] GetFileMD5Checksum(string filePath)
    {
        var sha = System.Security.Cryptography.MD5.Create();
        byte[] checksum;
        using (var fs = File.OpenRead(filePath))
            checksum = sha.ComputeHash(fs);
        return checksum;
    }

    protected override async Task<VelopackAssetFeed> GetReleasesAsync(TDown options)
    {
        var releasesName = Utility.GetVeloReleaseIndexName(options.Channel);
        var client = CreateClient(options);
        var bytes = await GetObjectBytes(client, releasesName);
        if (bytes == null || bytes.Length == 0) {
            return new VelopackAssetFeed();
        }
        return VelopackAssetFeed.FromJson(Encoding.UTF8.GetString(bytes));
    }

    public virtual async Task UploadMissingAssetsAsync(TUp options)
    {
        var build = BuildAssets.Read(options.ReleaseDir.FullName, options.Channel);
        var client = CreateClient(options);
        var releasesFilename = Path.GetFileName(options.ReleaseDir.FullName);

        Log.Info($"Preparing to upload {build.Files.Count} local asset(s).");

        var remoteReleases = await GetReleasesAsync(options);
        Log.Info($"There are {remoteReleases.Assets.Length} asset(s) in remote releases file '{releasesFilename}'.");

        var localEntries = build.GetReleaseEntries();
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

        foreach (var asset in build.Files) {
            await UploadObject(client, Path.GetFileName(asset), new FileInfo(asset), true, noCache: false);
        }

        using var _1 = Utility.GetTempFileName(out var tmpReleases);
        File.WriteAllText(tmpReleases, ReleaseEntryHelper.GetAssetFeedJson(new VelopackAssetFeed { Assets = releaseEntries }));
        var releasesName = Utility.GetVeloReleaseIndexName(options.Channel);
        await UploadObject(client, releasesName, new FileInfo(tmpReleases), true, noCache: true);

#pragma warning disable CS0612 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
        var legacyKey = Utility.GetReleasesFileName(options.Channel);
        using var _2 = Utility.GetTempFileName(out var tmpReleases2);
        using (var fs = File.Create(tmpReleases2)) {
            ReleaseEntry.WriteReleaseFile(releaseEntries.Select(ReleaseEntry.FromVelopackAsset), fs);
        }
        await UploadObject(client, legacyKey, new FileInfo(tmpReleases2), true, noCache: true);
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore CS0612 // Type or member is obsolete

        if (toDelete.Length > 0) {
            Log.Info($"Retention policy about to delete {toDelete.Length} release(s)...");
            foreach (var del in toDelete) {
                await DeleteObject(client, del.FileName);
            }
        }

        Log.Info("Done.");
    }
}
