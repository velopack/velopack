namespace Velopack.Vpk.Commands.Deployment;

public class AzureUploadCommand : AzureBaseCommand
{
    public int KeepMaxReleases { get; private set; }
    
    public string Folder { get; private set; }

    public AzureUploadCommand()
        : base("az", "Upload releases to an Azure Blob Storage container.")
    {
        AddOption<int>((x) => KeepMaxReleases = x, "--keepMaxReleases")
            .SetDescription("The maximum number of releases to keep in the container, anything older will be deleted.")
            .SetArgumentHelpName("COUNT");

        AddOption<string>((x) => Folder = x, "--folder")
            .SetDescription("The folder path within the container where files should be uploaded.")
            .SetArgumentHelpName("PATH");

        ReleaseDirectoryOption.SetRequired();
        ReleaseDirectoryOption.MustNotBeEmpty();
    }
}
