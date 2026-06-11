using Microsoft.Extensions.Logging;
using Velopack.Core.Abstractions;

namespace Velopack.Flow.Commands;

public class LogoutCommandRunner(ILogger logger, IFancyConsole console) : ValidatedCommand<LogoutOptions, LogoutOptionsValidator>
{
    protected override async Task RunCoreAsync(LogoutOptions options)
    {
        var client = new VelopackFlowServiceClient(options, logger, console);
        await client.LogoutAsync(CancellationToken.None);
    }
}