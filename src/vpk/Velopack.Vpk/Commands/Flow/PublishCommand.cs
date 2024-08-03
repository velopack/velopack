namespace Velopack.Vpk.Commands.Flow;

#nullable enable
public class PublishCommand : VelopackServiceCommand
{
    public string ReleaseDirectory { get; set; } = "";

    public string? Channel { get; set; }


    public PublishCommand()
        : base("publish", "Uploads a release to Velopack's hosted service")
    {
        AddOption<string>(v => ReleaseDirectory = v, "--releaseDir")
            .SetDescription("The directory containing the Velopack release files.")
            .SetRequired();

        AddOption<string>(v => Channel = v, "-c", "--channel")
            .SetDescription("The channel for the release");
    }
}
