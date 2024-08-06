using Velopack.Packaging.Flow;

namespace Velopack.Vpk.Commands.Flow;

public abstract class VelopackServiceCommand : BaseCommand
{
    public string VelopackBaseUrl { get; private set; }

    public string ApiKey { get; private set; }

    protected VelopackServiceCommand(string name, string description)
        : base(name, description)
    {
        AddOption<string>(v => VelopackBaseUrl = v, "--baseUrl")
            .SetDescription("The base Uri for the Velopack API service.")
            .SetArgumentHelpName("URI")
            .SetDefault(VelopackServiceOptions.DefaultBaseUrl);

        AddOption<string>(v => ApiKey = v, "--api-key")
            .SetDescription("The API key to use to authenticate with Velopack API service.")
            .SetArgumentHelpName("ApiKey");
    }
}