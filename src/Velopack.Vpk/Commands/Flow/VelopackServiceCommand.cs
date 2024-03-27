using Velopack.Packaging.Flow;

namespace Velopack.Vpk.Commands.Flow;

public abstract class VelopackServiceCommand : BaseCommand
{
    public string VelopackBaseUrl { get; private set; }

    protected VelopackServiceCommand(string name, string description)
        : base(name, description)
    {
        AddOption<string>(v => VelopackBaseUrl = v, "--baseUrl")
            .SetDescription("The base Uri for the Velopack API service.")
            .SetArgumentHelpName("URI")
            .SetDefault(VelopackServiceOptions.DefaultBaseUrl);
    }
}