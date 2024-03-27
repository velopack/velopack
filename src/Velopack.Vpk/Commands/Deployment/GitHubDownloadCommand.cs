namespace Velopack.Vpk.Commands.Deployment;

public class GitHubDownloadCommand : GitHubBaseCommand
{
    public bool Prerelease { get; private set; }

    public GitHubDownloadCommand()
        : base("github", "Download latest release from GitHub repository.")
    {
        AddOption<bool>((v) => Prerelease = v, "--pre")
            .SetDescription("Get latest pre-release instead of stable.");
    }
}
