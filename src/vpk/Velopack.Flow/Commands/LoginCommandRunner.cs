using Microsoft.Extensions.Logging;
using Velopack.Core.Abstractions;

namespace Velopack.Flow.Commands;

public class LoginCommandRunner(ILogger logger, IFancyConsole console) : ValidatedCommand<LoginOptions>
{
    protected override async Task RunCoreAsync(LoginOptions options)
    {
        var client = new VelopackFlowServiceClient(options, logger, console);
        await client.LoginAsync(null, false, CancellationToken.None);
    }
}