namespace Velopack.Vpk.Commands.Deployment;

public class S3UploadCommand : S3BaseCommand
{
    public int KeepMaxReleases { get; private set; }
    public int KeepMaxDeltaReleases { get; private set; }

    public S3UploadCommand()
        : base("s3", "Upload releases to a S3 bucket.")
    {
        AddOption<int>((x) => KeepMaxReleases = x, "--keepMaxReleases")
            .SetDescription("The maximum number of releases to keep in the bucket, anything older will be deleted.")
            .SetArgumentHelpName("COUNT");

        AddOption<int>((x) => KeepMaxDeltaReleases = x, "--keepMaxDeltaReleases")
         .SetDescription("The maximum number of delta releases to keep in the target directory, anything older will be deleted.");

        ReleaseDirectoryOption.SetRequired();
        ReleaseDirectoryOption.MustNotBeEmpty();
    }
}
