using Velopack.Packaging.Abstractions;
using Velopack.Packaging.Flow;

#nullable enable
namespace Velopack.Vpk.Commands.Flow;

internal class LogoutCommandRunner(IVelopackFlowServiceClient Client) : ICommand<LogoutOptions>
{
    public async Task Run(LogoutOptions options)
    {
        await Client.LogoutAsync(options);
    }
}