namespace Velopack.Vpk.Commands.Deployment;

public class AzureBaseCommand : OutputCommand
{
    public string Account { get; private set; }

    public string Key { get; private set; }

    public string Endpoint { get; private set; }

    public string Container { get; private set; }

    public string SasToken { get; private set; }

    public double Timeout { get; private set; }

    protected AzureBaseCommand(string name, string description)
        : base(name, description)
    {
        AddOption<string>((v) => Account = v, "--account")
            .SetDescription("Account name")
            .SetArgumentHelpName("ACCOUNT")
            .SetRequired();

        var key = AddOption<string>((v) => Key = v, "--key")
            .SetDescription("Account secret key")
            .SetArgumentHelpName("KEY");

        var sas = AddOption<string>((v) => SasToken = v, "--sas")
            .SetDescription("Shared access signature token (not the url)")
            .SetArgumentHelpName("TOKEN");

        AddOption<string>((v) => Container = v, "--container")
            .SetDescription("Azure container name")
            .SetArgumentHelpName("NAME")
            .SetRequired();

        AddOption<Uri>((v) => Endpoint = v.ToAbsoluteOrNull(), "--endpoint")
            .SetDescription("Service url (eg. https://<account-name>.blob.core.windows.net)")
            .SetArgumentHelpName("URL")
            .MustBeValidHttpUri();

        AddOption<double>((v) => Timeout = v, "--timeout")
            .SetDescription("Network timeout in minutes.")
            .SetArgumentHelpName("MINUTES")
            .SetDefault(30);

        this.AtLeastOneRequired(sas, key);
        this.AreMutuallyExclusive(sas, key);
    }
}
