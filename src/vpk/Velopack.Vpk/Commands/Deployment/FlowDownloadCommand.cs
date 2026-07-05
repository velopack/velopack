namespace Velopack.Vpk.Commands.Deployment;
#nullable enable
public class FlowDownloadCommand : OutputCommand
{
    public string? VelopackBaseUrl { get; private set; }

    public string? PackageId { get; private set; }

    public double Timeout { get; private set; }

    public FlowDownloadCommand()
        : base("flow", "Download latest release from Velopack Flow.")
    {
        AddOption<string>(v => VelopackBaseUrl = v, "--baseUrl")
            .SetDescription("The base Uri for the Velopack API service.")
            .SetArgumentHelpName("URI");

        AddOption<string>(v => PackageId = v, "--packageId", "-p")
            .SetDescription("The package ID of the application to download.")
            .SetArgumentHelpName("ID")
            .SetRequired();

        AddOption<double>((v) => Timeout = v, "--timeout")
            .SetDescription("Network timeout in minutes.")
            .SetArgumentHelpName("MINUTES")
            .SetDefault(30);
    }
}
