namespace Velopack.Vpk.Commands.Deployment;
public class GiteaDownloadCommand : GiteaBaseCommand
{
    public bool Prerelease { get; private set; }

    public GiteaDownloadCommand()
        : base("gitea", "Download latest release from Gitea repository.")
    {
        AddOption<bool>((v) => Prerelease = v, "--pre")
            .SetDescription("Get latest pre-release instead of stable.");
    }
}