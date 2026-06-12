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

public class AzureObjectStoreClient(BlobContainerClient client, string prefix, ILogger logger)
    : ObjectStoreClient(logger)
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

    protected override async Task<RemoteObjectInfo> GetRemoteObjectInfoAsync(string key)
    {
        var properties = await GetBlobClient(key).GetPropertiesAsync();
        var stored = properties.Value.ContentHash;
        if (stored == null) {
            return null;
        }

        return new RemoteObjectInfo { Md5Hex = Convert.ToHexString(stored).ToLowerInvariant() };
    }

    protected override async Task UploadObjectCoreAsync(string key, FileInfo file, bool overwriteRemote, bool noCache)
    {
        var options = new BlobUploadOptions {
            HttpHeaders = new BlobHttpHeaders(),
            Conditions = overwriteRemote ? null : new BlobRequestConditions { IfNoneMatch = new ETag("*") }
        };

        if (noCache) {
            options.HttpHeaders.CacheControl = "no-cache";
        }

        await GetBlobClient(key).UploadAsync(file.FullName, options);
    }

    protected override async Task<byte[]> GetObjectBytesCoreAsync(string key)
    {
        var obj = GetBlobClient(key);
        var ms = new MemoryStream();
        using var response = await obj.DownloadToAsync(ms, CancellationToken.None);
        return ms.ToArray();
    }

    protected override async Task DownloadToFileCoreAsync(string key, string filePath)
    {
        var obj = GetBlobClient(key);
        using var response = await obj.DownloadToAsync(filePath, CancellationToken.None);
    }

    protected override async Task DeleteObjectCoreAsync(string key)
    {
        await client.DeleteBlobIfExistsAsync(prefix + key);
    }

    protected override bool IsNotFoundException(Exception ex)
    {
        return ex is RequestFailedException { Status: 404 };
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
