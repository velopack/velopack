using Amazon;
using Amazon.S3;
using Amazon.S3.Model;
using Microsoft.Extensions.Logging;
using Velopack.Util;

namespace Velopack.Deployment;

public class S3DownloadOptions : RepositoryOptions, IObjectDownloadOptions
{
    public string KeyId { get; set; }

    public string Secret { get; set; }

    public string Session { get; set; }

    public string Region { get; set; }

    public string Endpoint { get; set; }

    public string Bucket { get; set; }

    public string Prefix { get; set; }
}

public class S3UploadOptions : S3DownloadOptions, IObjectUploadOptions
{
    public int KeepMaxReleases { get; set; }
}

public class S3BucketClient(AmazonS3Client client, string bucket, string prefix, bool disableSigning)
{
    public virtual Task<DeleteObjectResponse> DeleteObjectAsync(string key, CancellationToken cancellationToken = default)
    {
        var request = new DeleteObjectRequest();
        request.BucketName = bucket;
        request.Key = prefix + key;
        return client.DeleteObjectAsync(request, cancellationToken);
    }

    public virtual Task<DeleteObjectResponse> DeleteObjectAsync(string key, string versionId, CancellationToken cancellationToken = default)
    {
        var request = new DeleteObjectRequest();
        request.BucketName = bucket;
        request.Key = prefix + key;
        request.VersionId = versionId;
        return client.DeleteObjectAsync(request, cancellationToken);
    }

    public virtual Task<PutObjectResponse> PutObjectAsync(string key, string fullName, bool noCache, CancellationToken cancellationToken = default)
    {
        var request = new PutObjectRequest();
        request.BucketName = bucket;
        request.FilePath = fullName;
        request.Key = prefix + key;

        if (disableSigning) {
            // due to compatibility reasons CloudFlare R2, Oracle Object storage (maybe some other providers)
            // doesn't support Streaming SigV4 which is used in chunked uploading
            request.DisablePayloadSigning = true;
            request.DisableDefaultChecksumValidation = false;
        }

        if (noCache) {
            request.Headers.CacheControl = "no-cache";
        }

        return client.PutObjectAsync(request, cancellationToken);
    }

    public virtual Task<GetObjectResponse> GetObjectAsync(string key, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectRequest();
        request.BucketName = bucket;
        request.Key = prefix + key;
        return client.GetObjectAsync(request, cancellationToken);
    }

    public virtual Task<GetObjectMetadataResponse> GetObjectMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectMetadataRequest();
        request.BucketName = bucket;
        request.Key = prefix + key;
        return client.GetObjectMetadataAsync(request, cancellationToken);
    }
}

public class S3Repository : ObjectRepository<S3DownloadOptions, S3UploadOptions, S3BucketClient>
{
    public S3Repository(ILogger logger) : base(logger)
    {
    }

    protected override S3BucketClient CreateClient(S3DownloadOptions options)
    {
        bool disableSigning = false;
        var config = new AmazonS3Config() {
            ServiceURL = options.Endpoint,
            ForcePathStyle = true, // support for MINIO
            Timeout = TimeSpan.FromMinutes(options.Timeout)
        };

        if (options.Endpoint != null) {
            config.ServiceURL = options.Endpoint;
            // if the endpoint is using https, and is _not_ an AWS endpoint, we can disable signing 
            // not all providers support the AWS signing mechanism. the AWS SDK will refuse to upload
            // something which is not signed to an http endpoint which is why this is only done for https.
            var uri = new Uri(options.Endpoint);
            if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && !uri.Host.Equals("amazonaws.com", StringComparison.OrdinalIgnoreCase)) {
                disableSigning = true;
            }
        } else if (options.Region != null) {
            config.RegionEndpoint = RegionEndpoint.GetBySystemName(options.Region);
        } else {
            throw new InvalidOperationException("Missing endpoint");
        }

        AmazonS3Client client;
        if (options.Session != null) {
            client = new AmazonS3Client(options.KeyId, options.Secret, options.Session, config);
        } else if (options.KeyId != null || options.Secret != null) {
            client = new AmazonS3Client(options.KeyId, options.Secret, config);
        } else {
            client = new AmazonS3Client(config);
        }

        var prefix = options.Prefix?.Trim();
        if (prefix == null) {
            prefix = "";
        }

        if (!string.IsNullOrEmpty(prefix) && !prefix.EndsWith("/")) {
            prefix += "/";
        }

        return new S3BucketClient(client, options.Bucket, prefix, disableSigning);
    }

    protected override async Task DeleteObject(S3BucketClient client, string key)
    {
        await RetryAsync(() => client.DeleteObjectAsync(key), "Deleting " + key);
    }

    protected override async Task<byte[]> GetObjectBytes(S3BucketClient client, string key)
    {
        return await RetryAsyncRet(
            async () => {
                try {
                    var ms = new MemoryStream();
                    using (var obj = await client.GetObjectAsync(key))
                    using (var stream = obj.ResponseStream) {
                        await stream.CopyToAsync(ms);
                    }

                    return ms.ToArray();
                } catch (AmazonS3Exception ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound) {
                    return null;
                }
            },
            $"Downloading {key}...");
    }

    protected override async Task SaveEntryToFileAsync(S3DownloadOptions options, VelopackAsset entry, string filePath)
    {
        var client = CreateClient(options);
        await RetryAsync(
            async () => {
                using (var obj = await client.GetObjectAsync(entry.FileName)) {
                    await obj.WriteResponseStreamToFileAsync(filePath, false, CancellationToken.None);
                }
            },
            $"Downloading {entry.FileName}...");
    }

    protected override async Task UploadObject(S3BucketClient client, string key, FileInfo f, bool overwriteRemote, bool noCache)
    {
        string deleteOldVersionId = null;

        // try to detect an existing remote file of the same name
        try {
            var metadata = await client.GetObjectMetadataAsync(key);
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

        await RetryAsync(() => client.PutObjectAsync(key, f.FullName, noCache), "Uploading " + key + (noCache ? " (no-cache)" : ""));

        if (deleteOldVersionId != null) {
            try {
                await RetryAsync(
                    () => client.DeleteObjectAsync(key, deleteOldVersionId),
                    "Removing old version of " + key);
            } catch { }
        }
    }
}