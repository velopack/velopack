using System.Text;
using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.Deployment;

public class AzureDownloadOptions : RepositoryOptions, IObjectDownloadOptions
{
    public string Account { get; set; }

    public string Key { get; set; }

    public string Endpoint { get; set; }

    public string Container { get; set; }

    public string SasToken { get; set; }

    public string Prefix { get; set; }
}

public class AzureUploadOptions : AzureDownloadOptions, IObjectUploadOptions
{
    public int KeepMaxReleases { get; set; }
}

public class AzureBlobClient(BlobContainerClient client, string prefix)
{
    public virtual Task DeleteBlobIfExistsAsync(string key, CancellationToken cancellationToken = default)
    {
        return client.DeleteBlobIfExistsAsync(prefix + key, cancellationToken: cancellationToken);
    }

    public virtual BlobClient GetBlobClient(string key)
    {
        return client.GetBlobClient(prefix + key);
    }
}

public class AzureRepository : ObjectRepository<AzureDownloadOptions, AzureUploadOptions, AzureBlobClient>
{
    public AzureRepository(ILogger logger) : base(logger)
    {
    }

    protected override AzureBlobClient CreateClient(AzureDownloadOptions options)
    {
        var serviceUrl = options.Endpoint ?? "https://" + options.Account + ".blob.core.windows.net";
        if (options.Endpoint == null) {
            Log.Info($"Endpoint not specified, default to: {serviceUrl}");
        }

        BlobServiceClient client;

        // Override default timeout with user-specified value
        BlobClientOptions clientOptions = new BlobClientOptions();
        clientOptions.Retry.NetworkTimeout = TimeSpan.FromMinutes(options.Timeout);

        if (!String.IsNullOrEmpty(options.SasToken)) {
            client = new BlobServiceClient(new Uri(serviceUrl), new AzureSasCredential(options.SasToken), clientOptions);
        } else {
            client = new BlobServiceClient(new Uri(serviceUrl), new StorageSharedKeyCredential(options.Account, options.Key), clientOptions);
        }

        var containerClient = client.GetBlobContainerClient(options.Container);

        var prefix = options.Prefix?.Trim();
        if (prefix == null) {
            prefix = "";
        }

        if (!string.IsNullOrEmpty(prefix) && !prefix.EndsWith("/")) {
            prefix += "/";
        }

        return new AzureBlobClient(containerClient, prefix);
    }

    protected override async Task DeleteObject(AzureBlobClient client, string key)
    {
        await RetryAsync(
            async () => {
                await client.DeleteBlobIfExistsAsync(key);
            },
            "Deleting " + key);
    }

    protected override async Task<byte[]> GetObjectBytes(AzureBlobClient client, string key)
    {
        return await RetryAsyncRet(
            async () => {
                try {
                    var obj = client.GetBlobClient(key);
                    var ms = new MemoryStream();
                    using var response = await obj.DownloadToAsync(ms, CancellationToken.None);
                    return ms.ToArray();
                } catch (Azure.RequestFailedException ex) when (ex.Status == 404) {
                    return null;
                }
            },
            $"Downloading {key}...");
    }

    protected override async Task<VelopackAssetFeed> GetReleasesAsync(AzureDownloadOptions options)
    {
        var releasesName = CoreUtil.GetVeloReleaseIndexName(options.Channel);
        var client = CreateClient(options);
        var bytes = await GetObjectBytes(client, releasesName);
        if (bytes == null || bytes.Length == 0) {
            return new VelopackAssetFeed();
        }
        return VelopackAssetFeed.FromJson(Encoding.UTF8.GetString(bytes));
    }

    protected override async Task SaveEntryToFileAsync(AzureDownloadOptions options, VelopackAsset entry, string filePath)
    {
        await RetryAsync(
            async () => {
                var client = CreateClient(options);
                var obj = client.GetBlobClient(entry.FileName);
                using var response = await obj.DownloadToAsync(filePath, CancellationToken.None);
            },
            $"Downloading {entry.FileName}...");
    }


    protected override async Task UploadObject(AzureBlobClient client, string key, FileInfo f, bool overwriteRemote, bool noCache)
    {
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

        var options = new BlobUploadOptions {
            HttpHeaders = new BlobHttpHeaders(),
            Conditions = overwriteRemote ? null : new BlobRequestConditions { IfNoneMatch = new ETag("*") }
        };

        if (noCache) {
            options.HttpHeaders.CacheControl = "no-cache";
        }

        await RetryAsync(() => blobClient.UploadAsync(f.FullName, options), "Uploading " + key);
    }
}