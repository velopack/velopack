using Azure.Storage;
using Azure.Storage.Blobs;
using Azure.Storage.Blobs.Models;
using Velopack.Deployment;
using Xunit;

namespace Velopack.Deployment.Tests;

/// <summary>
/// Object-store deployment lifecycle against the local Azurite blob emulator. A fresh container is created
/// per test with <see cref="PublicAccessType.Blob"/> so its blobs are readable over plain HTTP, letting the
/// public feed URL be verified via <c>SimpleWebSource</c>. The container is deleted in the scope disposer.
/// </summary>
public class AzureDeploymentTests(ITestOutputHelper output) : ObjectStoreDeploymentSuite(output)
{
    // Well-known Azurite dev account credentials.
    private const string Account = "devstoreaccount1";
    private const string AccountKey = "Eby8vdM02xNOcqFlqUwJPLlmEtlCDXJ1OUzFT50uSRZ6IFsuFq2UVErCz4I6tq/K1SZFPTOtr/KBHBeksoGMGw==";
    private const string Endpoint = "http://127.0.0.1:10000/devstoreaccount1";

    private string _container = "";

    protected override Task SkipUnlessReadyAsync() => DockerServices.SkipUnlessAzuriteUpAsync();

    protected override string? GetPublicFeedUrl() => $"{Endpoint}/{_container}";

    protected override async Task<IAsyncDisposable> CreateScopeAsync(ILogger log)
    {
        _container = "velopack-" + Guid.NewGuid().ToString("N").Substring(0, 12);
        var svc = new BlobServiceClient(new Uri(Endpoint), new StorageSharedKeyCredential(Account, AccountKey));
        var container = svc.GetBlobContainerClient(_container);
        await container.CreateAsync(PublicAccessType.Blob);
        return new AsyncCleanup(async () => {
            try {
                await container.DeleteIfExistsAsync();
            } catch { /* best-effort */ }
        });
    }

    protected override Task UploadAsync(string releaseDir, string channel, int keepMaxReleases, ILogger log)
        => new AzureUploadCommandRunner(log).Run(new AzureUploadOptions {
            Account = Account,
            Key = AccountKey,
            Endpoint = Endpoint,
            Container = _container,
            ReleaseDir = new DirectoryInfo(releaseDir),
            Channel = channel,
            KeepMaxReleases = keepMaxReleases,
        });

    protected override Task DownloadAsync(string releaseDir, string channel, ILogger log)
        => new AzureDownloadCommandRunner(log).Run(new AzureDownloadOptions {
            Account = Account,
            Key = AccountKey,
            Endpoint = Endpoint,
            Container = _container,
            ReleaseDir = new DirectoryInfo(releaseDir),
            Channel = channel,
        });

    protected override IObjectStoreClient CreateClient(ILogger log)
        => AzureObjectStoreClient.Create(new AzureDownloadOptions {
            Account = Account,
            Key = AccountKey,
            Endpoint = Endpoint,
            Container = _container,
        }, log);
}
