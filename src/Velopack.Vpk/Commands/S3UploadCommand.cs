
namespace Velopack.Vpk.Commands;

public class S3UploadCommand : S3BaseCommand
{
    public bool Overwrite { get; private set; }

    public S3UploadCommand()
        : base("s3", "Upload releases to a S3 bucket.")
    {
        AddOption<bool>((v) => Overwrite = v, "--overwrite")
            .SetDescription("Replace remote files if local files have changed.");

        ReleaseDirectoryOption.SetRequired();
        ReleaseDirectoryOption.MustNotBeEmpty();
    }
}
