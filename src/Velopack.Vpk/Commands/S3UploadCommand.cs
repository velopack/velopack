
namespace Velopack.Vpk.Commands;

public class S3UploadCommand : S3BaseCommand
{
    public S3UploadCommand()
        : base("s3", "Upload releases to a S3 bucket.")
    {
        ReleaseDirectoryOption.SetRequired();
        ReleaseDirectoryOption.MustNotBeEmpty();
    }
}
