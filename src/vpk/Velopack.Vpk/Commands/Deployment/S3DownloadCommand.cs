﻿namespace Velopack.Vpk.Commands.Deployment;

public class S3DownloadCommand : S3BaseCommand
{
    public S3DownloadCommand()
        : base("s3", "Download latest release from an S3 bucket.")
    {
    }
}
