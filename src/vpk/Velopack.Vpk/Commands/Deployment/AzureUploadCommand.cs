namespace Velopack.Vpk.Commands.Deployment;

public class AzureUploadCommand : AzureBaseCommand
{
    public int KeepMaxReleases { get; private set; }
    public int KeepMaxDeltaReleases { get; private set; }

    public AzureUploadCommand()
        : base("az", "Upload releases to an Azure Blob Storage container.")
    {
        AddOption<int>((x) => KeepMaxReleases = x, "--keepMaxReleases")
            .SetDescription("The maximum number of releases to keep in the container, anything older will be deleted.")
            .SetArgumentHelpName("COUNT");

        var keepMaxDeltaReleases = AddOption<int>((x) => KeepMaxDeltaReleases = x, "--keepMaxDeltaReleases")
            .SetDescription("The maximum number of delta releases to keep in the target directory, anything older will be deleted.")
            .SetArgumentHelpName("COUNT");

        keepMaxDeltaReleases.DefaultValueFactory = (x) => KeepMaxReleases;

        ReleaseDirectoryOption.SetRequired();
        ReleaseDirectoryOption.MustNotBeEmpty();
    }
}
