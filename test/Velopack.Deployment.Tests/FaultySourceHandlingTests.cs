using Neovolve.Logging.Xunit;
using Velopack.Core;
using Velopack.Deployment;
using Velopack.Logging;
using Velopack.Sources;

namespace Velopack.Tests;

public class FaultySourceHandlingTests(ITestOutputHelper testOutputHelper)
{
    private class FaultyRepository(ILogger logger) : SourceRepository<RepositoryOptions, FaultySource>(logger)
    {
        public override FaultySource CreateSource(RepositoryOptions options)
        {
            return new FaultySource();
        }
    }


    private class FaultySource : IUpdateSource
    {
        public Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel,
            Guid? stagingId = null,
            VelopackAsset? latestLocalRelease = null)
        {
            return Task.FromResult(
                VelopackAssetFeed.FromJson(
                    """
                    {
                        "Assets": null
                    }
                    """));
        }

        public Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile,
            Action<int> progress,
            CancellationToken cancelToken = default)
        {
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task FaultySourceHandling()
    {
        var logger = new TestOutputLogger(nameof(FaultySourceHandlingTests), testOutputHelper);
        var repo = new FaultyRepository(logger);
        var options = new RepositoryOptions {
            Channel = "stable",
            ReleaseDir = new DirectoryInfo("somewhere"),
            TargetOs = RuntimeOs.Windows,
        };
        var feed = await repo.CreateSource(options).GetReleaseFeed(
            logger.ToVelopackLogger(),
            null,
            "stable");
        Assert.Null(feed.Assets); //Assert bad package feed
        await repo.DownloadLatestFullPackageAsync(options); //Assert graceful exit
    }
}