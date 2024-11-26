using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Microsoft.Extensions.Logging;
using Velopack.Util;

namespace Velopack.Deployment;

public class AzureDownloadOptions : RepositoryOptions, IObjectDownloadOptions
{
    public string Account { get; set; }

    public string Key { get; set; }

    public string Endpoint { get; set; }

    public string Container { get; set; }

    public string SasToken { get; set; }
}

public class AzureUploadOptions : AzureDownloadOptions, IObjectUploadOptions
{
    public int KeepMaxReleases { get; set; }
}

public class AzureRepository : ObjectRepository<AzureDownloadOptions, AzureUploadOptions, BlobContainerClient>
{
    public AzureRepository(ILogger logger) : base(logger)
    {
    }

    protected override BlobContainerClient CreateClient(AzureDownloadOptions options)
    {
        var serviceUrl = options.Endpoint ?? "https://" + options.Account + ".blob.core.windows.net";
        if (options.Endpoint == null) {
            Log.Info($"Endpoint not specified, default to: {serviceUrl}");
        }

        BlobServiceClient client;
        BlobClientOptions clientOptions = new BlobClientOptions();
        clientOptions.Retry.NetworkTimeout = TimeSpan.FromMinutes(30);

        if (!String.IsNullOrEmpty(options.SasToken)) {
            client = new BlobServiceClient(new Uri(serviceUrl), new AzureSasCredential(options.SasToken), clientOptions);
        } else {
            client = new BlobServiceClient(new Uri(serviceUrl), new StorageSharedKeyCredential(options.Account, options.Key), clientOptions);
        }
        return client.GetBlobContainerClient(options.Container);
    }

    protected override async Task DeleteObject(BlobContainerClient client, string key)
    {
        await RetryAsync(async () => {
            await client.DeleteBlobIfExistsAsync(key);
        }, "Deleting " + key);
    }

    protected override async Task<byte[]> GetObjectBytes(BlobContainerClient client, string key)
    {
        return await RetryAsyncRet(async () => {
            try {
                var obj = client.GetBlobClient(key);
                var ms = new MemoryStream();
                using var response = await obj.DownloadToAsync(ms, CancellationToken.None);
                return ms.ToArray();
            } catch (Azure.RequestFailedException ex) when (ex.Status == 404) {
                return null;
            }
        }, $"Downloading {key}...");
    }

    protected override async Task SaveEntryToFileAsync(AzureDownloadOptions options, VelopackAsset entry, string filePath)
    {
        await RetryAsync(async () => {
            var client = CreateClient(options);
            var obj = client.GetBlobClient(entry.FileName);
            using var response = await obj.DownloadToAsync(filePath, CancellationToken.None);
        }, $"Downloading {entry.FileName}...");
    }

    protected override async Task UploadObject(BlobContainerClient client, string key, FileInfo f, bool overwriteRemote, bool noCache)
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