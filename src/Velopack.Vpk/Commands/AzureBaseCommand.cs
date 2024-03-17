
namespace Velopack.Vpk.Commands;

public class AzureBaseCommand : OutputCommand
{
    public string Account { get; private set; }

    public string Key { get; private set; }

    public string Endpoint { get; private set; }

    public string Container { get; private set; }

    protected AzureBaseCommand(string name, string description)
        : base(name, description)
    {
        AddOption<string>((v) => Account = v, "--account")
            .SetDescription("Account name")
            .SetArgumentHelpName("ACCOUNT")
            .SetRequired();

        AddOption<string>((v) => Key = v, "--key")
            .SetDescription("Account secret key")
            .SetArgumentHelpName("KEY")
            .SetRequired();

        AddOption<string>((v) => Container = v, "--container")
            .SetDescription("Azure container name")
         .SetArgumentHelpName("CONTAINER")
         .SetRequired();

        AddOption<Uri>((v) => Endpoint = v.ToAbsoluteOrNull(), "--endpoint")
            .SetDescription("Service url (eg. https://<storage-account-name>.blob.core.windows.net)")
            .SetArgumentHelpName("URL")
            .MustBeValidHttpUri()
            .SetRequired();

    }
}

public class AzureDownloadCommand : AzureBaseCommand
{
    public AzureDownloadCommand()
        : base("az", "Download latest release from an AZ container.")
    {
    }
}

public class AzureUploadCommand : AzureBaseCommand
{
    public int KeepMaxReleases { get; private set; }

    public AzureUploadCommand()
        : base("az", "Upload releases to an Azure container.")
    {
        AddOption<int>((x) => KeepMaxReleases = x, "--keepMaxReleases")
            .SetDescription("The maximum number of releases to keep in the bucket, anything older will be deleted.")
            .SetArgumentHelpName("COUNT");

        ReleaseDirectoryOption.SetRequired();
        ReleaseDirectoryOption.MustNotBeEmpty();
    }
}
