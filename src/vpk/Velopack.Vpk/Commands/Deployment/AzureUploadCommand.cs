namespace Velopack.Vpk.Commands.Deployment;

public class AzureUploadCommand : AzureBaseCommand
{
    public int KeepMaxReleases { get; private set; }

    public AzureUploadCommand()
        : base("az", "Upload releases to an Azure Blob Storage container.")
    {
        AddOption<int>((x) => KeepMaxReleases = x, ["--keepMaxReleases"])
            .SetDescription("The maximum number of releases to keep, older releases are deleted.")
            .SetArgumentHelpName("COUNT");
    }
}
