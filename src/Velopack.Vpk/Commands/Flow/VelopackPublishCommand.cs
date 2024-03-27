namespace Velopack.Vpk.Commands.Flow;
#nullable enable

public class VelopackPublishCommand : VelopackBaseCommand
{
    public string? Version { get; set; }

    public VelopackPublishCommand()
        : base("publish", "Uploads a release to Velopack's hosted service")
    {

    }
}
