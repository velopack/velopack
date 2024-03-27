using System.Net;
using System.Text;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Velopack.Packaging;

namespace Velopack.Deployment;

public class S3DownloadOptions : RepositoryOptions
{
    public string KeyId { get; set; }

    public string Secret { get; set; }

    public string Session { get; set; }

    public string Region { get; set; }

    public string Endpoint { get; set; }

    public string Bucket { get; set; }
}

public class S3UploadOptions : S3DownloadOptions
{
    public int KeepMaxReleases { get; set; }
}

public class S3Repository : DownRepository<S3DownloadOptions>, IRepositoryCanUpload<S3UploadOptions>
{
    public S3Repository(ILogger logger) : base(logger)
    {
    }

    public async Task UploadMissingAssetsAsync(S3UploadOptions options)
    {
        var build = BuildAssets.Read(options.ReleaseDir.FullName, options.Channel);
        var client = GetS3Client(options);

        Log.Info($"Preparing to upload {build.Files.Count} local assets to S3 endpoint {options.Endpoint ?? ""}");

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
            await UploadFile(client, options.Bucket, Path.GetFileName(asset), new FileInfo(asset), true);
        }

        using var _1 = Utility.GetTempFileName(out var tmpReleases);
        File.WriteAllText(tmpReleases, ReleaseEntryHelper.GetAssetFeedJson(new VelopackAssetFeed { Assets = releaseEntries }));
        var releasesName = Utility.GetVeloReleaseIndexName(options.Channel);
        await UploadFile(client, options.Bucket, releasesName, new FileInfo(tmpReleases), true, noCache: true);

#pragma warning disable CS0612 // Type or member is obsolete
#pragma warning disable CS0618 // Type or member is obsolete
        var legacyKey = Utility.GetReleasesFileName(options.Channel);
        using var _2 = Utility.GetTempFileName(out var tmpReleases2);
        using (var fs = File.Create(tmpReleases2)) {
            ReleaseEntry.WriteReleaseFile(releaseEntries.Select(ReleaseEntry.FromVelopackAsset), fs);
        }
        await UploadFile(client, options.Bucket, legacyKey, new FileInfo(tmpReleases2), true, noCache: true);
#pragma warning restore CS0618 // Type or member is obsolete
#pragma warning restore CS0612 // Type or member is obsolete

        if (toDelete.Length > 0) {
            Log.Info($"Retention policy about to delete {toDelete.Length} releases...");
            foreach (var del in toDelete) {
                //var metadata = await client.GetObjectMetadataAsync(options.Bucket, del.FileName);
                await RetryAsync(() => client.DeleteObjectAsync(options.Bucket, del.FileName), "Deleting " + del.FileName);
            }
        }

        Log.Info("Done.");
    }

    protected override async Task<VelopackAssetFeed> GetReleasesAsync(S3DownloadOptions options)
    {
        var releasesName = Utility.GetVeloReleaseIndexName(options.Channel);
        var client = GetS3Client(options);

        var ms = new MemoryStream();

        try {
            await RetryAsync(async () => {
                using (var obj = await client.GetObjectAsync(options.Bucket, releasesName))
                using (var stream = obj.ResponseStream) {
                    await stream.CopyToAsync(ms);
                }
            }, $"Fetching {releasesName}...");
        } catch (AmazonS3Exception ex) when (ex.StatusCode == HttpStatusCode.NotFound) {
            return new VelopackAssetFeed();
        }

        return VelopackAssetFeed.FromJson(Encoding.UTF8.GetString(ms.ToArray()));
    }

    protected override async Task SaveEntryToFileAsync(S3DownloadOptions options, VelopackAsset entry, string filePath)
    {
        var client = GetS3Client(options);
        await RetryAsync(async () => {
            using (var obj = await client.GetObjectAsync(options.Bucket, entry.FileName)) {
                await obj.WriteResponseStreamToFileAsync(filePath, false, CancellationToken.None);
            }
        }, $"Downloading {entry.FileName}...");
    }

    private static AmazonS3Client GetS3Client(S3DownloadOptions options)
    {
        var config = new AmazonS3Config() { ServiceURL = options.Endpoint };
        if (options.Endpoint != null) {
            config.ServiceURL = options.Endpoint;
        } else if (options.Region != null) {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        } else {
            throw new InvalidOperationException("Missing endpoint");
        }

        if (options.Session != null) {
            return new AmazonS3Client(options.KeyId, options.Secret, options.Session, config);
        } else {
            return new AmazonS3Client(options.KeyId, options.Secret, config);
        }
    }

    private async Task UploadFile(AmazonS3Client client, string bucket, string key, FileInfo f, bool overwriteRemote, bool noCache = false)
    {
        string deleteOldVersionId = null;

        // try to detect an existing remote file of the same name
        try {
            var metadata = await client.GetObjectMetadataAsync(bucket, key);
            var md5 = GetFileMD5Checksum(f.FullName);
            var stored = metadata?.ETag?.Trim().Trim('"');

            if (stored != null) {
                if (stored.Equals(md5, StringComparison.InvariantCultureIgnoreCase)) {
                    Log.Info($"Upload file '{key}' skipped (already exists in remote)");
                    return;
                } else if (overwriteRemote) {
                    Log.Info($"File '{key}' exists in remote, replacing...");
                    deleteOldVersionId = metadata.VersionId;
                } else {
                    Log.Warn($"File '{key}' exists in remote and checksum does not match local file. Use 'overwrite' argument to replace remote file.");
                    return;
                }
            }
        } catch {
            // don't care if this check fails. worst case, we end up re-uploading a file that
            // already exists. storage providers should prefer the newer file of the same name.
        }

        var req = new PutObjectRequest {
            BucketName = bucket,
            FilePath = f.FullName,
            Key = key,
        };

        if (noCache) {
            req.Headers.CacheControl = "no-cache";
        }

        await RetryAsync(() => client.PutObjectAsync(req), "Uploading " + key + (noCache ? " (no-cache)" : ""));

        if (deleteOldVersionId != null) {
            try {
                await RetryAsync(() => client.DeleteObjectAsync(bucket, key, deleteOldVersionId),
                    "Removing old version of " + key);
            } catch { }
        }
    }

    private static string GetFileMD5Checksum(string filePath)
    {
        var sha = System.Security.Cryptography.MD5.Create();
        byte[] checksum;
        using (var fs = File.OpenRead(filePath))
            checksum = sha.ComputeHash(fs);
        return BitConverter.ToString(checksum).Replace("-", String.Empty);
    }
}