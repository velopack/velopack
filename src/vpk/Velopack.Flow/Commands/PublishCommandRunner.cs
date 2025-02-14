using Microsoft.Extensions.Logging;
using Velopack.Core.Abstractions;

namespace Velopack.Flow.Commands;

public class PublishCommandRunner(ILogger logger, IFancyConsole console) : ICommand<PublishOptions>
{
    public async Task Run(PublishOptions options)
    {
        var loginOptions = new VelopackFlowLoginOptions() {
            AllowCacheCredentials = true,
            AllowDeviceCodeFlow = false,
            AllowInteractiveLogin = false,
        };

        var client = new VelopackFlowServiceClient(options, logger, console);
        CancellationToken token = CancellationToken.None;
        if (!await client.LoginAsync(loginOptions, false, token)) {
            return;
        }

        await client.UploadLatestReleaseAssetsAsync(
            options.Channel,
            options.ReleaseDirectory,
            options.TargetOs,
            options.WaitForLive,
            options.TieredRolloutPercentage,
            token);
    }
}