namespace Velopack.Vpk.Commands.Deployment;

public class S3BaseCommand : OutputCommand
{
    public string KeyId { get; private set; }

    public string Secret { get; private set; }

    public string Session { get; private set; }

    public string Region { get; private set; }

    public string Endpoint { get; private set; }

    public string Bucket { get; private set; }

    public string Prefix { get; private set; }

    public bool DisablePathStyle { get; private set; }

    public double Timeout { get; private set; }

    protected S3BaseCommand(string name, string description)
        : base(name, description)
    {
        AddOption<string>((v) => KeyId = v, ["--keyId"])
            .SetDescription("Authentication identifier or access key.")
            .SetArgumentHelpName("KEYID");

        AddOption<string>((v) => Secret = v, ["--secret"])
            .SetDescription("Authentication secret key.")
            .SetArgumentHelpName("KEY");

        AddOption<string>((v) => Region = v, ["--region"])
            .SetDescription("AWS service region (eg. us-west-1).")
            .SetArgumentHelpName("REGION");

        AddOption<string>((v) => Session = v, ["--session"])
            .SetDescription("Authentication session token.")
            .SetArgumentHelpName("TOKEN");

        AddOption<string>((v) => Endpoint = v, ["--endpoint"])
            .SetDescription("Custom S3-compatible service url (backblaze, digital ocean, etc).")
            .SetArgumentHelpName("URL");

        AddOption<string>((v) => Bucket = v, ["--bucket"])
            .SetDescription("Name of the S3 bucket.")
            .SetArgumentHelpName("NAME");

        AddOption<string>((v) => Prefix = v, ["--prefix"])
            .SetDescription("Optional filename path prefix.")
            .SetArgumentHelpName("PREFIX");

        AddOption<bool>((v) => DisablePathStyle = v, ["--disablePathStyle"])
            .SetDescription("Disable the default of path-style endpoint and use a subdomain endpoint instead.")
            .SetArgumentHelpName("BOOL");

        AddOption<double>((v) => Timeout = v, ["--timeout"])
            .SetDescription("Network timeout in minutes.")
            .SetArgumentHelpName("MINUTES")
            .SetDefault(30);
    }
}
