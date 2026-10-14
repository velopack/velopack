namespace Velopack.Vpk.Commands.Deployment;

public class S3DownloadCommand : S3BaseCommand
{
    public bool InfoJson { get; private set; }

    public S3DownloadCommand()
        : base("s3", "Download latest release from an S3 bucket.")
    {
        AddOption<bool>((v) => InfoJson = v, ["--info-json"])
            .SetDescription("Print the latest release as JSON instead of downloading.");
    }
}
