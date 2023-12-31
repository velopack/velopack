
namespace Velopack.Vpk.Commands;

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
