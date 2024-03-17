using System.Net;
using System.Text;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;
using Velopack.Packaging;

namespace Velopack.Deployment;

public class AzureDownloadOptions : RepositoryOptions
{
    public string Account { get; set; }

    public string Key { get; set; }

    public string Endpoint { get; set; }

    public string Container { get; set; }
}

public class AzureUploadOptions : AzureDownloadOptions
{
    public int KeepMaxReleases { get; set; }
}

public class AzureRepository : DownRepository<AzureDownloadOptions>, IRepositoryCanUpload<AzureUploadOptions>
{
    public AzureRepository(ILogger logger) : base(logger)
    {
    }

    public async Task UploadMissingAssetsAsync(AzureUploadOptions options)
    {
        var build = BuildAssets.Read(options.ReleaseDir.FullName, options.Channel);
        var client = GetBlobContainerClient(options);

        Log.Info($"Preparing to upload {build.Files.Count} local assets to Azure endpoint {options.Endpoint ?? ""}");

        var remoteReleases = await GetReleasesAsync(options);
        Log.Info($"There are {remoteReleases.Assets.Length} assets in remote RELEASES file.");

        var localEntries = build.GetReleaseEntries();
        var releaseEntries = ReleaseEntryHelper.MergeAssets(localEntries, remoteReleases.Assets).ToArray();

        Log.Info($"{releaseEntries.Length} merged local/remote releases.");

        VelopackAsset[] toDelete = new VelopackAsset[0];

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
                Log.Info($"Retention policy (keepMaxReleases={options.KeepMaxReleases}) will delete {toDelete.Length} releases.");
            } else {
                Log.Info($"Retention policy (keepMaxReleases={options.KeepMaxReleases}) will not be applied, because there will only be {fullReleases.Length} full releases when this upload has completed.");
            }
        }

        foreach (var asset in build.Files) {
            await UploadFile(client, Path.GetFileName(asset), new FileInfo(asset), true);
        }

        using var _1 = Utility.GetTempFileName(out var tmpReleases);
        File.WriteAllText(tmpReleases, ReleaseEntryHelper.GetAssetFeedJson(new VelopackAssetFeed { Assets = releaseEntries }));
        var releasesName = Utility.GetVeloReleaseIndexName(options.Channel);
        await UploadFile(client, releasesName, new FileInfo(tmpReleases), true);

#pragma warning disable CS0612 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
        var legacyKey = Utility.GetReleasesFileName(options.Channel);
        using var _2 = Utility.GetTempFileName(out var tmpReleases2);
        using (var fs = File.Create(tmpReleases2)) {
            ReleaseEntry.WriteReleaseFile(releaseEntries.Select(ReleaseEntry.FromVelopackAsset), fs);
        }
        await UploadFile(client, legacyKey, new FileInfo(tmpReleases2), true);
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore CS0612 // Type or member is obsolete

        if (toDelete.Length > 0) {
            Log.Info($"Retention policy about to delete {toDelete.Length} releases...");
            foreach (var del in toDelete) {
                await RetryAsync(() => client.DeleteBlobIfExistsAsync(del.FileName), "Deleting " + del.FileName);
            }
        }

        Log.Info("Done.");
    }

    protected override async Task<VelopackAssetFeed> GetReleasesAsync(AzureDownloadOptions options)
    {
        var releasesName = Utility.GetVeloReleaseIndexName(options.Channel);
        var client = GetBlobContainerClient(options);

        var ms = new MemoryStream();

        try {
            await RetryAsync(async () => {
                var obj = client.GetBlobClient(releasesName);
                using var response = await obj.DownloadToAsync(ms);
            }, $"Fetching {releasesName}...");
        } catch (RequestFailedException ex) when (ex.Status == 404) {
            return new VelopackAssetFeed();
        }

        return VelopackAssetFeed.FromJson(Encoding.UTF8.GetString(ms.ToArray()));
    }

    protected override async Task SaveEntryToFileAsync(AzureDownloadOptions options, VelopackAsset entry, string filePath)
    {
        var client = GetBlobContainerClient(options);
        await RetryAsync(async () => {
            var obj = client.GetBlobClient(entry.FileName); 
            using var response = await obj.DownloadToAsync(filePath, CancellationToken.None);
        }, $"Downloading {entry.FileName}...");
    }

    private static BlobServiceClient GetBlobServiceClient(AzureDownloadOptions options)
    {
        return new BlobServiceClient(new Uri(options.Endpoint), new StorageSharedKeyCredential(options.Account, options.Key));
    }

    private static BlobContainerClient GetBlobContainerClient(AzureDownloadOptions options)
    {
        var client = GetBlobServiceClient(options);
        var containerClient = client.GetBlobContainerClient(options.Container);
        return containerClient;
    }

    private async Task UploadFile(BlobContainerClient client, string key, FileInfo f, bool overwriteRemote)
    {
        // try to detect an existing remote file of the same name
        var blobClient = client.GetBlobClient(key);
        try {
            var properties = await blobClient.GetPropertiesAsync();
            var md5 = GetFileMD5Checksum(f.FullName);
            var stored = properties.Value.ContentHash;

            if (stored != null) {
                if (Enumerable.SequenceEqual(md5, stored)) {
                    Log.Info($"Upload file '{key}' skipped (already exists in remote)");
                    return;
                } else if (overwriteRemote) {
                    Log.Info($"File '{key}' exists in remote, replacing...");
                } else {
                    Log.Warn($"File '{key}' exists in remote and checksum does not match local file. Use 'overwrite' argument to replace remote file.");
                    return;
                }
            }
        } catch {
            // don't care if this check fails. worst case, we end up re-uploading a file that
            // already exists. storage providers should prefer the newer file of the same name.
        }

        await RetryAsync(() => blobClient.UploadAsync(f.FullName, overwriteRemote), "Uploading " + key);
    }

    private static byte[] GetFileMD5Checksum(string filePath)
    {
        var sha = System.Security.Cryptography.MD5.Create();
        byte[] checksum;
        using (var fs = File.OpenRead(filePath))
            checksum = sha.ComputeHash(fs);
        return checksum;
    }
}