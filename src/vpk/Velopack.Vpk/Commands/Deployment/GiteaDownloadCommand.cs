namespace Velopack.Vpk.Commands.Deployment;
public class GiteaDownloadCommand : GiteaBaseCommand
{
    public bool UpdateReleasesFile { get; set; }

    public bool Prerelease { get; private set; }

    public GiteaDownloadCommand()
        : base("gitea", "Download latest release from Gitea repository.")
    {
        AddOption<bool>((v) => Prerelease = v, "--pre")
            .SetDescription("Get latest pre-release instead of stable.");

        AddOption<bool>((v) => UpdateReleasesFile = v, "--update-releases-file")
            .SetDescription("Create or update the local releases files.")
            .SetDefault(false);
    }
}