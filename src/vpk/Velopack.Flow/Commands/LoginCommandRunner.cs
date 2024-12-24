using Microsoft.Extensions.Logging;
using Velopack.Core.Abstractions;

namespace Velopack.Flow.Commands;

public class LoginCommandRunner(ILogger logger, IFancyConsole console) : ICommand<LoginOptions>
{
    public async Task Run(LoginOptions options)
    {
        var client = new VelopackFlowServiceClient(options, logger, console);
        await client.LoginAsync(null, false, CancellationToken.None);
    }
}