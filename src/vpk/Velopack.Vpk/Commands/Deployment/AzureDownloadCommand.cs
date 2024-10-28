namespace Velopack.Vpk.Commands.Deployment;

public class AzureDownloadCommand : AzureBaseCommand
{
    public bool UpdateReleasesFile { get; set; }

    public AzureDownloadCommand()
        : base("az", "Download latest release from an Azure Blob Storage container.")
    {

        AddOption<bool>((v) => UpdateReleasesFile = v, "--update-releases-file")
            .SetDescription("Create or update the local releases files.")
            .SetDefault(false);
    }
}
