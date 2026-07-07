using Microsoft.Extensions.Logging;
using Velopack.Core.Abstractions;

namespace Velopack.Flow.Commands;

public class ApiCommandRunner(ILogger logger, IFancyConsole console) : ValidatedCommand<ApiOptions, ApiOptionsValidator>
{
    protected override async Task RunCoreAsync(ApiOptions options)
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

        string response = await client.InvokeEndpointAsync(options.Endpoint, options.Method, options.Body, token);
        Console.WriteLine(response);
    }
}