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
    
    public string Folder { get; set; }
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

        // Override default timeout with user-specified value
        BlobClientOptions clientOptions = new BlobClientOptions();
        clientOptions.Retry.NetworkTimeout = TimeSpan.FromMinutes(options.Timeout);

        if (!String.IsNullOrEmpty(options.SasToken)) {
            client = new BlobServiceClient(new Uri(serviceUrl), new AzureSasCredential(options.SasToken), clientOptions);
        } else {
            client = new BlobServiceClient(new Uri(serviceUrl), new StorageSharedKeyCredential(options.Account, options.Key), clientOptions);
        }

        return client.GetBlobContainerClient(options.Container);
    }

    protected override async Task DeleteObject(BlobContainerClient client, string key)
    {
        // Prepend folder path if specified (using _uploadFolder since this is called during upload)
        if (!string.IsNullOrEmpty(_uploadFolder)) {
            key = _uploadFolder.TrimEnd('/') + "/" + key;
        }
        
        await RetryAsync(
            async () => {
                await client.DeleteBlobIfExistsAsync(key);
            },
            "Deleting " + key);
    }

    protected override async Task<byte[]> GetObjectBytes(BlobContainerClient client, string key)
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
        
        // Prepend folder path if specified
        if (!string.IsNullOrEmpty(options.Folder)) {
            releasesName = options.Folder.TrimEnd('/') + "/" + releasesName;
        }
        
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
                var key = entry.FileName;
                
                // Prepend folder path if specified
                if (!string.IsNullOrEmpty(options.Folder)) {
                    key = options.Folder.TrimEnd('/') + "/" + key;
                }
                
                var obj = client.GetBlobClient(key);
                using var response = await obj.DownloadToAsync(filePath, CancellationToken.None);
            },
            $"Downloading {entry.FileName}...");
    }

    public override async Task UploadMissingAssetsAsync(AzureUploadOptions options)
    {
        // Store the folder in a private field for use in UploadObject
        // Note: Azure Blob Storage will handle path validation and reject invalid paths
        _uploadFolder = options.Folder;
        try {
            await base.UploadMissingAssetsAsync(options);
        } finally {
            _uploadFolder = null;
        }
    }

    private string _uploadFolder;

    protected override async Task UploadObject(BlobContainerClient client, string key, FileInfo f, bool overwriteRemote, bool noCache)
    {
        // Prepend folder path if specified
        if (!string.IsNullOrEmpty(_uploadFolder)) {
            key = _uploadFolder.TrimEnd('/') + "/" + key;
        }

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