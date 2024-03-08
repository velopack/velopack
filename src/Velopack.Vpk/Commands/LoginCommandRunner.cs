using Velopack.Packaging.Abstractions;
using Velopack.Packaging.Flow;

namespace Velopack.Vpk.Commands;
#nullable enable

public class LoginCommandRunner(IVelopackFlowServiceClient Client) : ICommand<LoginOptions>
{

    public async Task Run(LoginOptions options)
    {
        await Client.LoginAsync(new() {
            VelopackBaseUrl = options.VelopackBaseUrl
        });
    }
}