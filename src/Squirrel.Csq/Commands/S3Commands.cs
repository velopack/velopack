
namespace Squirrel.Csq.Commands;

public class S3BaseCommand : BaseCommand
{
    public string KeyId { get; private set; }

    public string Secret { get; private set; }

    public string Region { get; private set; }

    public string Endpoint { get; private set; }

    public string Bucket { get; private set; }

    public string PathPrefix { get; private set; }

    protected S3BaseCommand(string name, string description)
        : base(name, description)
    {
        AddOption<string>((v) => KeyId = v, "--keyId")
            .SetDescription("Authentication identifier or access key.")
            .SetArgumentHelpName("KEYID")
            .SetRequired();

        AddOption<string>((v) => Secret = v, "--secret")
            .SetDescription("Authentication secret key.")
            .SetArgumentHelpName("KEY")
            .SetRequired();

        var region = AddOption<string>((v) => Region = v, "--region")
            .SetDescription("AWS service region (eg. us-west-1).")
            .SetArgumentHelpName("REGION");

        region.Validators.Add(MustBeValidAwsRegion);

        var endpoint = AddOption<Uri>((v) => Endpoint = v.ToAbsoluteOrNull(), "--endpoint")
            .SetDescription("Custom service url (backblaze, digital ocean, etc).")
            .SetArgumentHelpName("URL")
            .MustBeValidHttpUri();

        this.AreMutuallyExclusive(region, endpoint);
        this.AtLeastOneRequired(region, endpoint);

        AddOption<string>((v) => Bucket = v, "--bucket")
            .SetDescription("Name of the S3 bucket.")
            .SetArgumentHelpName("NAME")
            .SetRequired();

        AddOption<string>((v) => PathPrefix = v, "--pathPrefix")
            .SetDescription("A sub-folder used for files in the bucket, for creating release channels (eg. 'stable' or 'dev').")
            .SetArgumentHelpName("PREFIX");
    }

    private static void MustBeValidAwsRegion(OptionResult result)
    {
        for (var i = 0; i < result.Tokens.Count; i++) {
            var region = result.Tokens[i].Value;
            if (!string.IsNullOrWhiteSpace(region)) {
                var r = Amazon.RegionEndpoint.GetBySystemName(result.Tokens[0].Value);
                if (r is null || r.DisplayName == "Unknown") {
                    result.AddError($"Region '{region}' lookup failed, is this a valid AWS region?");
                }
            } else {
                result.AddError("A region value is required");
            }
        }
    }
}

public class S3DownloadCommand : S3BaseCommand
{
    public S3DownloadCommand()
        : base("s3", "Download latest release from an S3 bucket.")
    {
    }
}

public class S3UploadCommand : S3BaseCommand
{
    public bool Overwrite { get; private set; }

    public int KeepMaxReleases { get; private set; }

    public S3UploadCommand()
        : base("s3", "Upload releases to a S3 bucket.")
    {
        AddOption<bool>((v) => Overwrite = v, "--overwrite")
            .SetDescription("Replace remote files if local files have changed.");

        AddOption<int>((v) => KeepMaxReleases = v, "--keepMaxReleases")
            .SetDescription("Apply a retention policy which keeps only the specified number of old versions in remote source.")
            .SetArgumentHelpName("NUMBER");

        ReleaseDirectoryOption.SetRequired();
        ReleaseDirectoryOption.MustNotBeEmpty();
    }
}
