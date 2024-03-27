using Azure.Storage;
using Azure.Storage.Blobs;
using Microsoft.Extensions.Logging;

namespace Velopack.Deployment;

public class AzureDownloadOptions : RepositoryOptions, IObjectDownloadOptions
{
    public string Account { get; set; }

    public string Key { get; set; }

    public string Endpoint { get; set; }

    public string ContainerName { get; set; }

    public string SasToken { get; set; }
}

public class AzureUploadOptions : AzureDownloadOptions, IObjectUploadOptions
{
    public int KeepMaxReleases { get; set; }
}

public class AzureRepository : ObjectRepository<AzureDownloadOptions, AzureUploadOptions, BlobServiceClient>
{
    public AzureRepository(ILogger logger) : base(logger)
    {
    }

    protected override BlobServiceClient CreateClient(AzureDownloadOptions options)
    {
        var serviceUrl = options.Endpoint ?? "https://" + options.Account + ".blob.core.windows.net";
        if (options.Endpoint == null) {
            Log.Info($"Endpoint not specified, default to: {serviceUrl}");
        }

        if (!String.IsNullOrEmpty(options.SasToken)) {
            return new BlobServiceClient(new Uri(serviceUrl), new Azure.AzureSasCredential(options.SasToken));
        }

        return new BlobServiceClient(new Uri(serviceUrl), new StorageSharedKeyCredential(options.Account, options.Key));
    }

    protected override async Task DeleteObject(BlobServiceClient client, string container, string key)
    {
        await RetryAsync(async () => {
            var containerClient = client.GetBlobContainerClient(container);
            await containerClient.DeleteBlobIfExistsAsync(key);
        }, "Deleting " + key);
    }

    protected override async Task<byte[]> GetObjectBytes(BlobServiceClient client, string container, string key)
    {
        return await RetryAsyncRet(async () => {
            try {
                var containerClient = client.GetBlobContainerClient(container);
                var obj = containerClient.GetBlobClient(key);
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
            var containerClient = client.GetBlobContainerClient(options.ContainerName);
            var obj = containerClient.GetBlobClient(entry.FileName);
            using var response = await obj.DownloadToAsync(filePath, CancellationToken.None);
        }, $"Downloading {entry.FileName}...");
    }

    protected override async Task UploadObject(BlobServiceClient client, string container, string key, FileInfo f, bool overwriteRemote, bool noCache)
    {
        var containerClient = client.GetBlobContainerClient(container);
        var blobClient = containerClient.GetBlobClient(key);
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
}