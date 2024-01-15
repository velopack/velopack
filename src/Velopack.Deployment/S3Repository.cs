using System.Net;
using System.Text;
using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Velopack.Packaging;
using Velopack.Sources;

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
        Log.Info($"There are {remoteReleases.Length} assets in remote RELEASES file.");

        var localEntries = build.GetReleaseEntries();

        // merge local release entries with remote ones
        // will preserve the local entries because they appear first
        var releaseEntries = localEntries
            .Concat(remoteReleases)
            .DistinctBy(r => r.OriginalFilename)
            .OrderBy(k => k.Version)
            .ThenBy(k => !k.IsDelta)
            .ToArray();

        Log.Info($"{releaseEntries.Length} merged releases.");

        foreach (var asset in build.Files) {
            await UploadFile(client, options.Bucket, Path.GetFileName(asset), new FileInfo(asset), true);
        }

        using var _1 = Utility.GetTempFileName(out var tmpReleases);
        ReleaseEntry.WriteReleaseFile(releaseEntries, tmpReleases);
        var releasesName = Utility.GetReleasesFileName(options.Channel);
        await UploadFile(client, options.Bucket, releasesName, new FileInfo(tmpReleases), true);
        Log.Info("Done.");
    }

    protected override async Task<ReleaseEntry[]> GetReleasesAsync(S3DownloadOptions options)
    {
        var releasesName = Utility.GetReleasesFileName(options.Channel);
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
            return new ReleaseEntry[0];
        }

        return ReleaseEntry.ParseReleaseFile(Encoding.UTF8.GetString(ms.ToArray())).ToArray();
    }

    protected override async Task SaveEntryToFileAsync(S3DownloadOptions options, ReleaseEntry entry, string filePath)
    {
        var client = GetS3Client(options);
        await RetryAsync(async () => {
            using (var obj = await client.GetObjectAsync(options.Bucket, entry.OriginalFilename)) {
                await obj.WriteResponseStreamToFileAsync(filePath, false, CancellationToken.None);
            }
        }, $"Downloading {entry.OriginalFilename}...");
    }

    private static AmazonS3Client GetS3Client(S3DownloadOptions options)
    {
        if (options.Region != null) {
            var r = RegionEndpoint.GetBySystemName(options.Region);
            if (string.IsNullOrWhiteSpace(options.KeyId)) {
                return new AmazonS3Client(r);
            }
            return new AmazonS3Client(options.KeyId, options.Secret, options.Session, r);
        } else if (options.Endpoint != null) {
            var config = new AmazonS3Config() { ServiceURL = options.Endpoint };
            return new AmazonS3Client(options.KeyId, options.Secret, options.Session, config);
        } else {
            throw new InvalidOperationException("Missing endpoint");
        }
    }

    private async Task UploadFile(AmazonS3Client client, string bucket, string key, FileInfo f, bool overwriteRemote)
    {
        string deleteOldVersionId = null;

        // try to detect an existing remote file of the same name
        try {
            var metadata = await client.GetObjectMetadataAsync(bucket, key);
            var md5 = GetFileMD5Checksum(f.FullName);
            var stored = metadata?.ETag?.Trim().Trim('"');

            if (stored != null) {
                if (stored.Equals(md5, StringComparison.InvariantCultureIgnoreCase)) {
                    Log.Info($"Upload file '{f.Name}' skipped (already exists in remote)");
                    return;
                } else if (overwriteRemote) {
                    Log.Info($"File '{f.Name}' exists in remote, replacing...");
                    deleteOldVersionId = metadata.VersionId;
                } else {
                    Log.Warn($"File '{f.Name}' exists in remote and checksum does not match local file. Use 'overwrite' argument to replace remote file.");
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

        await RetryAsync(() => client.PutObjectAsync(req), "Uploading " + f.Name);

        if (deleteOldVersionId != null) {
            try {
                await RetryAsync(() => client.DeleteObjectAsync(bucket, key, deleteOldVersionId),
                    "Removing old version of " + f.Name);
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