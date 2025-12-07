#nullable enable
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Sources;

namespace Velopack.Deployment;

public class FlowDownloadOptions : RepositoryOptions
{
    public string? VelopackBaseUrl { get; set; }

    public string? PackageId { get; set; }
}

public class FlowRepository(ILogger logger) : DownRepository<FlowDownloadOptions>(logger)
{
    public static VelopackFlowSource CreateSource(FlowDownloadOptions options)
    {
        var baseUrl = options.VelopackBaseUrl ?? "https://api.velopack.io/";
        return new VelopackFlowSource(baseUrl);
    }

    protected override Task<VelopackAssetFeed> GetReleasesAsync(FlowDownloadOptions options)
    {
        var source = CreateSource(options);
        return source.GetReleaseFeed(Log.ToVelopackLogger(), options.PackageId, options.Channel);
    }

    protected override Task SaveEntryToFileAsync(FlowDownloadOptions options, VelopackAsset entry, string filePath)
    {
        var source = CreateSource(options);
        return source.DownloadReleaseEntry(Log.ToVelopackLogger(), entry, filePath, (i) => { });
    }
}
