using Amazon;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Validation;

namespace Velopack.Deployment;

public class S3DownloadOptions : RepositoryOptions
{
    public string KeyId { get; set; }

    public string Secret { get; set; }

    public string Session { get; set; }

    public string Region { get; set; }

    public string Endpoint { get; set; }

    public string Bucket { get; set; }

    public string Prefix { get; set; }

    public bool DisablePathStyle { get; set; }

    public bool DisableChecksumValidation { get; set; }
}

public class S3UploadOptions : S3DownloadOptions, IObjectUploadOptions
{
    public int KeepMaxReleases { get; set; }
}

public class S3DownloadOptionsValidator<T> : RepositoryOptionsValidator<T> where T : S3DownloadOptions
{
    public S3DownloadOptionsValidator()
    {
        RuleFor(x => x.Bucket).NotEmpty();
        RuleFor(x => x.Region)
            .Must((opt, region) => !string.IsNullOrEmpty(region) || !string.IsNullOrEmpty(opt.Endpoint))
            .WithMessage("At least one of 'region' and 'endpoint' options are required.")
            .Must((opt, region) => string.IsNullOrEmpty(region) || string.IsNullOrEmpty(opt.Endpoint))
            .WithMessage("Cannot use 'region' and 'endpoint' options together, please choose one.")
            .Must(region => {
                if (string.IsNullOrEmpty(region)) return true;
                var r = RegionEndpoint.GetBySystemName(region);
                return r is not null && r.DisplayName != "Unknown";
            })
            .WithMessage("{PropertyName} '{PropertyValue}' lookup failed, is this a valid AWS region?");
        RuleFor(x => x.Endpoint).MustBeValidHttpUri();
        RuleFor(x => x.KeyId)
            .Must((opt, keyId) => string.IsNullOrEmpty(keyId) == string.IsNullOrEmpty(opt.Secret))
            .WithMessage("'keyId' and 'secret' options must be provided together, or not at all.");
        RuleFor(x => x.Session)
            .Must((opt, session) => string.IsNullOrEmpty(session) || !string.IsNullOrEmpty(opt.KeyId))
            .WithMessage("'session' option also requires 'keyId' and 'secret' to be provided.");
    }
}

public sealed class S3DownloadOptionsValidator : S3DownloadOptionsValidator<S3DownloadOptions>;

public sealed class S3UploadOptionsValidator : S3DownloadOptionsValidator<S3UploadOptions>
{
    public S3UploadOptionsValidator()
    {
        AddReleaseDirRules();
    }
}

public class S3ObjectStoreClient(AmazonS3Client client, string bucket, string prefix, bool disableSigning, bool disableChecksumValidation, ILogger logger)
    : ObjectStoreClient(logger)
{
    public static S3ObjectStoreClient Create(S3DownloadOptions options, ILogger logger)
    {
        bool disableSigning = false;
        var config = new AmazonS3Config() {
            ForcePathStyle = !options.DisablePathStyle, // default is ForcePathStyle = true, as it's more widely supported
            Timeout = TimeSpan.FromMinutes(options.Timeout),
        };

        if (options.DisableChecksumValidation) {
            config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
            config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
        }

        if (options.Endpoint != null) {
            config.ServiceURL = options.Endpoint;
            // if the endpoint is using https, and is _not_ an AWS endpoint, we can disable signing
            // not all providers support the AWS signing mechanism. the AWS SDK will refuse to upload
            // something which is not signed to an http endpoint which is why this is only done for https.
            var uri = new Uri(options.Endpoint);
            if (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) && !uri.Host.Equals("amazonaws.com", StringComparison.OrdinalIgnoreCase)) {
                disableSigning = true;
                config.ResponseChecksumValidation = ResponseChecksumValidation.WHEN_REQUIRED;
                config.RequestChecksumCalculation = RequestChecksumCalculation.WHEN_REQUIRED;
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

        return new S3ObjectStoreClient(client, options.Bucket, ObjectStoreUtil.NormalizePrefix(options.Prefix), disableSigning, options.DisableChecksumValidation, logger);
    }

    protected override async Task<RemoteObjectInfo> GetRemoteObjectInfoAsync(string key)
    {
        var metadata = await GetObjectMetadataAsync(key);
        var stored = metadata?.ETag?.Trim().Trim('"').ToLowerInvariant();
        if (stored == null) {
            return null;
        }

        // note: multipart upload ETags contain a '-' and are not an MD5 of the whole object,
        // so they will simply fail the checksum compare and take the replace path.
        return new RemoteObjectInfo { Md5Hex = stored, VersionId = metadata.VersionId };
    }

    protected override Task UploadObjectCoreAsync(string key, FileInfo file, bool overwriteRemote, bool noCache)
    {
        return PutObjectAsync(key, file.FullName, noCache);
    }

    protected override Task AfterObjectReplacedAsync(string key, RemoteObjectInfo replaced)
    {
        if (replaced.VersionId == null) {
            return Task.CompletedTask;
        }

        return Retry.RetryAsync(Log, () => DeleteObjectAsync(key, replaced.VersionId), "Removing old version of " + key);
    }

    protected override async Task<byte[]> GetObjectBytesCoreAsync(string key)
    {
        var ms = new MemoryStream();
        using (var obj = await GetObjectAsync(key))
        using (var stream = obj.ResponseStream) {
            await stream.CopyToAsync(ms);
        }

        return ms.ToArray();
    }

    protected override async Task DownloadToFileCoreAsync(string key, string filePath)
    {
        using (var obj = await GetObjectAsync(key)) {
            await obj.WriteResponseStreamToFileAsync(filePath, false, CancellationToken.None);
        }
    }

    protected override Task DeleteObjectCoreAsync(string key)
    {
        return DeleteObjectAsync(key);
    }

    protected override bool IsNotFoundException(Exception ex)
    {
        return ex is AmazonS3Exception { StatusCode: System.Net.HttpStatusCode.NotFound };
    }

    private Task<DeleteObjectResponse> DeleteObjectAsync(string key, string versionId = null, CancellationToken cancellationToken = default)
    {
        var request = new DeleteObjectRequest();
        request.BucketName = bucket;
        request.Key = prefix + key;
        request.VersionId = versionId;
        return client.DeleteObjectAsync(request, cancellationToken);
    }

    private Task<PutObjectResponse> PutObjectAsync(string key, string fullName, bool noCache, CancellationToken cancellationToken = default)
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

        if (disableChecksumValidation) {
            // some S3-compatible providers (eg. RustFS) do not support the default
            // checksums calculated by the AWS SDK, so the user can opt out entirely.
            request.DisableDefaultChecksumValidation = true;
        }

        if (noCache) {
            request.Headers.CacheControl = "no-cache";
        }

        return client.PutObjectAsync(request, cancellationToken);
    }

    private Task<GetObjectResponse> GetObjectAsync(string key, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectRequest();
        request.BucketName = bucket;
        request.Key = prefix + key;
        return client.GetObjectAsync(request, cancellationToken);
    }

    private Task<GetObjectMetadataResponse> GetObjectMetadataAsync(string key, CancellationToken cancellationToken = default)
    {
        var request = new GetObjectMetadataRequest();
        request.BucketName = bucket;
        request.Key = prefix + key;
        return client.GetObjectMetadataAsync(request, cancellationToken);
    }
}

public class S3DownloadCommandRunner(ILogger logger)
    : ObjectDownloadCommandRunner<S3DownloadOptions, S3DownloadOptionsValidator>(logger)
{
    protected override IObjectStoreClient CreateClient(S3DownloadOptions options)
    {
        return S3ObjectStoreClient.Create(options, Log);
    }
}

public class S3UploadCommandRunner(ILogger logger)
    : ObjectUploadCommandRunner<S3UploadOptions, S3UploadOptionsValidator>(logger)
{
    protected override IObjectStoreClient CreateClient(S3UploadOptions options)
    {
        return S3ObjectStoreClient.Create(options, Log);
    }
}
