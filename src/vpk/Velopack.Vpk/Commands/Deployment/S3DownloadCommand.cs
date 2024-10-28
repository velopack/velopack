namespace Velopack.Vpk.Commands.Deployment;

public class S3DownloadCommand : S3BaseCommand
{
    public bool UpdateReleasesFile { get; set; }

    public S3DownloadCommand()
        : base("s3", "Download latest release from an S3 bucket.")
    {
        AddOption<bool>((v) => UpdateReleasesFile = v, "--update-releases-file")
            .SetDescription("Create or update the local releases files.")
            .SetDefault(false);
    }
}
