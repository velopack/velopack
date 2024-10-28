namespace Velopack.Vpk.Commands.Deployment;

public class LocalDownloadCommand : LocalBaseCommand
{
    public bool UpdateReleasesFile { get; set; }

    public LocalDownloadCommand()
        : base("local", "Download latest release from a local path or network share.")
    {
        TargetPathOption.MustNotBeEmpty();
        TargetPathOption.MustExist();

        AddOption<bool>((v) => UpdateReleasesFile = v, "--update-releases-file")
            .SetDescription("Create or update the local releases files.")
            .SetDefault(false);
    }
}
