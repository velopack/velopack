namespace Velopack.Vpk.Commands.Flow;

#nullable enable
public class PublishCommand : VelopackServiceCommand
{
    public string ReleaseDirectory { get; set; } = "";

    public string? Channel { get; set; }

    public bool WaitForLive { get; set; }

    public PublishCommand()
        : base("publish", "Uploads a release to Velopack's hosted service")
    {
        AddOption<string>(v => ReleaseDirectory = v, "-o", "--outputDir")
            .SetDescription("The directory containing the Velopack release files.")
            .SetArgumentHelpName("DIR")
            .SetRequired();

        AddOption<string>(v => Channel = v, "-c", "--channel")
            .SetArgumentHelpName("NAME")
            .SetDescription("The channel used for the release.");

        AddOption<bool>(v => WaitForLive = v, "--waitForLive")
            .SetDescription("Wait for the release to finish processing and go live.");
    }
}