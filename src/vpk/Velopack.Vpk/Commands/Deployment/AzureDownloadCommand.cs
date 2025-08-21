namespace Velopack.Vpk.Commands.Deployment;

public class AzureDownloadCommand : AzureBaseCommand
{
    public string Folder { get; private set; }

    public AzureDownloadCommand()
        : base("az", "Download latest release from an Azure Blob Storage container.")
    {
        AddOption<string>((x) => Folder = x, "--folder")
            .SetDescription("The folder path within the container where files are stored.")
            .SetArgumentHelpName("PATH");
    }
}
