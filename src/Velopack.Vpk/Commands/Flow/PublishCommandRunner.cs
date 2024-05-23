using System.Threading;
using Velopack.Packaging.Abstractions;
using Velopack.Packaging.Flow;

namespace Velopack.Vpk.Commands.Flow;

public class PublishCommandRunner(IVelopackFlowServiceClient Client) : ICommand<PublishOptions>
{
    public async Task Run(PublishOptions options)
    {
        CancellationToken token = CancellationToken.None;
        if (!await Client.LoginAsync(new VelopackLoginOptions() {
            AllowCacheCredentials = true,
            AllowDeviceCodeFlow = false,
            AllowInteractiveLogin = false,
            ApiKey = options.ApiKey,
            VelopackBaseUrl = options.VelopackBaseUrl
        }, token)) {
            return;
        }

        await Client.UploadLatestReleaseAssetsAsync(options.Channel, options.ReleaseDirectory, options.VelopackBaseUrl, token);
    }
}
