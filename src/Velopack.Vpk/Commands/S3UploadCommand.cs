
namespace Velopack.Vpk.Commands;

public class S3UploadCommand : S3BaseCommand
{
    public int KeepMaxReleases { get; private set; }

    public S3UploadCommand()
        : base("s3", "Upload releases to a S3 bucket.")
    {
        AddOption<int>((x) => KeepMaxReleases = x, "--keepMaxReleases")
            .SetDescription("The maximum number of releases to keep in the bucket, anything older will be deleted.")
            .SetArgumentHelpName("COUNT");

        ReleaseDirectoryOption.SetRequired();
        ReleaseDirectoryOption.MustNotBeEmpty();
    }
}
