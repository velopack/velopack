using Amazon.S3;
using Amazon.S3.Model;
using Velopack.Deployment;
using Xunit;

namespace Velopack.Deployment.Tests;

/// <summary>
/// Object-store deployment lifecycle against the local S3Mock (adobe/s3mock) container. A fresh bucket is
/// created per test via the AWS SDK and deleted in the scope disposer. S3Mock serves unauthenticated HTTP
/// GETs, so the public feed URL is just <c>{endpoint}/{bucket}</c> (no bucket policy needed).
/// </summary>
[Collection("s3mock")]
public class S3DeploymentTests(ITestOutputHelper output) : ObjectStoreDeploymentSuite(output)
{
    private const string Endpoint = DockerServices.S3MockEndpoint; // http://localhost:9090
    private const string Region = "us-east-1";
    private const string KeyId = "s3mock";
    private const string Secret = "s3mock";

    private string _bucket = "";

    protected override Task SkipUnlessReadyAsync() => DockerServices.SkipUnlessS3MockUpAsync();

    protected override string? GetPublicFeedUrl() => $"{Endpoint}/{_bucket}";

    protected override async Task<IAsyncDisposable> CreateScopeAsync(ILogger log)
    {
        _bucket = "velopack-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var client = CreateRawClient();
        await client.PutBucketAsync(new PutBucketRequest { BucketName = _bucket });
        return new AsyncCleanup(async () => {
            try {
                await EmptyAndDeleteBucketAsync(client, _bucket);
            } catch { /* best-effort */ } finally {
                client.Dispose();
            }
        });
    }

    protected override Task UploadAsync(string releaseDir, string channel, int keepMaxReleases, ILogger log)
        => new S3UploadCommandRunner(log).Run(new S3UploadOptions {
            Endpoint = Endpoint,
            Region = Region,
            KeyId = KeyId,
            Secret = Secret,
            Bucket = _bucket,
            ReleaseDir = new DirectoryInfo(releaseDir),
            Channel = channel,
            KeepMaxReleases = keepMaxReleases,
        });

    protected override Task DownloadAsync(string releaseDir, string channel, ILogger log)
        => new S3DownloadCommandRunner(log).Run(new S3DownloadOptions {
            Endpoint = Endpoint,
            Region = Region,
            KeyId = KeyId,
            Secret = Secret,
            Bucket = _bucket,
            ReleaseDir = new DirectoryInfo(releaseDir),
            Channel = channel,
        });

    protected override IObjectStoreClient CreateClient(ILogger log)
        => S3ObjectStoreClient.Create(new S3DownloadOptions {
            Endpoint = Endpoint,
            Region = Region,
            KeyId = KeyId,
            Secret = Secret,
            Bucket = _bucket,
        }, log);

    private static AmazonS3Client CreateRawClient()
    {
        var config = new AmazonS3Config {
            ForcePathStyle = true,
            ServiceURL = Endpoint,
            AuthenticationRegion = Region,
        };
        return new AmazonS3Client(KeyId, Secret, config);
    }

    private static async Task EmptyAndDeleteBucketAsync(AmazonS3Client client, string bucket)
    {
        string? token = null;
        do {
            var list = await client.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, ContinuationToken = token });
            foreach (var obj in list.S3Objects ?? new List<S3Object>()) {
                await client.DeleteObjectAsync(new DeleteObjectRequest { BucketName = bucket, Key = obj.Key });
            }
            token = list.IsTruncated == true ? list.NextContinuationToken : null;
        } while (token != null);

        await client.DeleteBucketAsync(new DeleteBucketRequest { BucketName = bucket });
    }
}
