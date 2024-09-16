using System.Threading;
using Serilog.Core;
using Velopack.Packaging.Abstractions;
using Velopack.Packaging.Flow;

namespace Velopack.Vpk.Commands.Flow;
public class ApiCommandRunner(IVelopackFlowServiceClient Client) : ICommand<ApiOptions>
{
    public async Task Run(ApiOptions options)
    {
        CancellationToken token = CancellationToken.None;
        if (!await Client.LoginAsync(new VelopackLoginOptions() {
            AllowCacheCredentials = true,
            AllowDeviceCodeFlow = false,
            AllowInteractiveLogin = false,
            ApiKey = options.ApiKey,
            VelopackBaseUrl = options.VelopackBaseUrl
        }, true, token)) {
            return;
        }

        string response = await Client.InvokeEndpointAsync(options, options.Endpoint, options.Method, options.Body, token);
        Console.WriteLine(response);
    }
}

