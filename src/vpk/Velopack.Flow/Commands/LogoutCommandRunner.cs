using Microsoft.Extensions.Logging;
using Velopack.Core.Abstractions;

namespace Velopack.Flow.Commands;

public class LogoutCommandRunner(ILogger logger, IFancyConsole console) : ICommand<LogoutOptions>
{
    public async Task Run(LogoutOptions options)
    {
        var client = new VelopackFlowServiceClient(options, logger, console);
        await client.LogoutAsync(CancellationToken.None);
    }
}