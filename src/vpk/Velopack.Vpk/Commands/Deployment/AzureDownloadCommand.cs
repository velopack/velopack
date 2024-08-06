namespace Velopack.Vpk.Commands.Deployment;

public class AzureDownloadCommand : AzureBaseCommand
{
    public AzureDownloadCommand()
        : base("az", "Download latest release from an Azure Blob Storage container.")
    {
    }
}
