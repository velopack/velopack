using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;

namespace Velopack.Deployment;

public class S3DownloadOptions : RepositoryOptions, IObjectDownloadOptions
{
    public string KeyId { get; set; }

    public string Secret { get; set; }

    public string Session { get; set; }

    public string Region { get; set; }

    public string Endpoint { get; set; }

    public string ContainerName { get; set; }
}

public class S3UploadOptions : S3DownloadOptions, IObjectUploadOptions
{
    public int KeepMaxReleases { get; set; }
}

public class S3Repository : ObjectRepository<S3DownloadOptions, S3UploadOptions, AmazonS3Client>
{
    public S3Repository(ILogger logger) : base(logger)
    {
    }

    protected override AmazonS3Client CreateClient(S3DownloadOptions options)
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

    protected override async Task DeleteObject(AmazonS3Client client, string container, string key)
    {
        await RetryAsync(() => client.DeleteObjectAsync(container, key), "Deleting " + key);
    }

    protected override async Task<byte[]> GetObjectBytes(AmazonS3Client client, string container, string key)
    {
        return await RetryAsyncRet(async () => {
            var ms = new MemoryStream();
            using (var obj = await client.GetObjectAsync(container, key))
            using (var stream = obj.ResponseStream) {
                await stream.CopyToAsync(ms);
            }
            return ms.ToArray();
        }, $"Downloading {key}...");
    }

    protected override async Task SaveEntryToFileAsync(S3DownloadOptions options, VelopackAsset entry, string filePath)
    {
        var client = CreateClient(options);
        await RetryAsync(async () => {
            using (var obj = await client.GetObjectAsync(options.ContainerName, entry.FileName)) {
                await obj.WriteResponseStreamToFileAsync(filePath, false, CancellationToken.None);
            }
        }, $"Downloading {entry.FileName}...");
    }

    protected override async Task UploadObject(AmazonS3Client client, string container, string key, FileInfo f, bool overwriteRemote, bool noCache)
    {
        string deleteOldVersionId = null;

        // try to detect an existing remote file of the same name
        try {
            var metadata = await client.GetObjectMetadataAsync(container, key);
            var md5bytes = GetFileMD5Checksum(f.FullName);
            var md5 = BitConverter.ToString(md5bytes).Replace("-", String.Empty);
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
            BucketName = container,
            FilePath = f.FullName,
            Key = key,
        };

        if (noCache) {
            req.Headers.CacheControl = "no-cache";
        }

        await RetryAsync(() => client.PutObjectAsync(req), "Uploading " + key + (noCache ? " (no-cache)" : ""));

        if (deleteOldVersionId != null) {
            try {
                await RetryAsync(() => client.DeleteObjectAsync(container, key, deleteOldVersionId),
                    "Removing old version of " + key);
            } catch { }
        }
    }
}