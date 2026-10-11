namespace Velopack.Vpk.Commands.Deployment;

public class AzureDownloadCommand : AzureBaseCommand
{
    public bool InfoJson { get; private set; }

    public AzureDownloadCommand()
        : base("az", "Download latest release from an Azure Blob Storage container.")
    {
        AddOption<bool>((v) => InfoJson = v, ["--info-json"])
            .SetDescription("Print the latest release as JSON instead of downloading.");
    }
}
