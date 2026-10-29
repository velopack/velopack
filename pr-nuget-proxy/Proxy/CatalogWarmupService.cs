namespace PrNuGetProxy;

public sealed class CatalogWarmupService(PackageCatalog catalog, ILogger<CatalogWarmupService> logger) : IHostedService
{
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        var runs = await catalog.GetRunArtifactsAsync(cancellationToken);
        logger.LogInformation("Cached run and artifact metadata for {Count} completed PR builds.", runs.Length);
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
