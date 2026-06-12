using Azure;
using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Validation;

namespace Velopack.Deployment;

public class AzureDownloadOptions : RepositoryOptions
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

public class AzureDownloadOptionsValidator<T> : RepositoryOptionsValidator<T> where T : AzureDownloadOptions
{
    public AzureDownloadOptionsValidator()
    {
        RuleFor(x => x.Account).NotEmpty();
        RuleFor(x => x.Container).NotEmpty();
        RuleFor(x => x.Endpoint).MustBeValidHttpUri();
        RuleFor(x => x.SasToken)
            .Must((opt, sas) => !string.IsNullOrEmpty(sas) || !string.IsNullOrEmpty(opt.Key))
            .WithMessage("At least one of 'sas' and 'key' options are required.")
            .Must((opt, sas) => string.IsNullOrEmpty(sas) || string.IsNullOrEmpty(opt.Key))
            .WithMessage("Cannot use 'sas' and 'key' options together, please choose one.");
    }
}

public sealed class AzureDownloadOptionsValidator : AzureDownloadOptionsValidator<AzureDownloadOptions>;

public sealed class AzureUploadOptionsValidator : AzureDownloadOptionsValidator<AzureUploadOptions>
{
    public AzureUploadOptionsValidator()
    {
        AddReleaseDirRules();
    }
}

public class AzureObjectStoreClient(BlobContainerClient client, string prefix, ILogger logger) : IObjectStoreClient
{
    public static AzureObjectStoreClient Create(AzureDownloadOptions options, ILogger logger)
    {
        var serviceUrl = options.Endpoint ?? "https://" + options.Account + ".blob.core.windows.net";
        if (options.Endpoint == null) {
            logger.Info($"Endpoint not specified, default to: {serviceUrl}");
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

        return new AzureObjectStoreClient(containerClient, ObjectStoreUtil.NormalizePrefix(options.Prefix), logger);
    }

    public async Task UploadObject(string key, FileInfo f, bool overwriteRemote, bool noCache)
    {
        var blobClient = GetBlobClient(key);
        try {
            var properties = await blobClient.GetPropertiesAsync();
            var md5 = ObjectStoreUtil.GetFileMD5Checksum(f.FullName);
            var stored = properties.Value.ContentHash;

            if (stored != null) {
                if (Enumerable.SequenceEqual(md5, stored)) {
                    logger.Info($"Upload file '{key}' skipped (already exists in remote)");
                    return;
                } else if (overwriteRemote) {
                    logger.Info($"File '{key}' exists in remote, replacing...");
                } else {
                    logger.Warn($"File '{key}' exists in remote and checksum does not match local file. Use 'overwrite' argument to replace remote file.");
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

        await Retry.RetryAsync(logger, () => blobClient.UploadAsync(f.FullName, options), "Uploading " + key);
    }

    public async Task DeleteObject(string key)
    {
        await Retry.RetryAsync(
            logger,
            async () => {
                await client.DeleteBlobIfExistsAsync(prefix + key);
            },
            "Deleting " + key);
    }

    public async Task<byte[]> GetObjectBytes(string key)
    {
        return await Retry.RetryAsyncRet(
            logger,
            async () => {
                try {
                    var obj = GetBlobClient(key);
                    var ms = new MemoryStream();
                    using var response = await obj.DownloadToAsync(ms, CancellationToken.None);
                    return ms.ToArray();
                } catch (Azure.RequestFailedException ex) when (ex.Status == 404) {
                    return null;
                }
            },
            $"Downloading {key}...");
    }

    public async Task DownloadToFile(string key, string filePath)
    {
        await Retry.RetryAsync(
            logger,
            async () => {
                var obj = GetBlobClient(key);
                using var response = await obj.DownloadToAsync(filePath, CancellationToken.None);
            },
            $"Downloading {key}...");
    }

    private BlobClient GetBlobClient(string key)
    {
        return client.GetBlobClient(prefix + key);
    }
}

public class AzureDownloadCommandRunner(ILogger logger)
    : ObjectDownloadCommandRunner<AzureDownloadOptions, AzureDownloadOptionsValidator>(logger)
{
    protected override IObjectStoreClient CreateClient(AzureDownloadOptions options)
    {
        return AzureObjectStoreClient.Create(options, Log);
    }
}

public class AzureUploadCommandRunner(ILogger logger)
    : ObjectUploadCommandRunner<AzureUploadOptions, AzureUploadOptionsValidator>(logger)
{
    protected override IObjectStoreClient CreateClient(AzureUploadOptions options)
    {
        return AzureObjectStoreClient.Create(options, Log);
    }
}
