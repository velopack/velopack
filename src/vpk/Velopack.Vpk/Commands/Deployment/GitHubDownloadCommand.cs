namespace Velopack.Vpk.Commands.Deployment;

public class GitHubDownloadCommand : GitHubBaseCommand
{
    public bool UpdateReleasesFile { get; set; }

    public bool Prerelease { get; private set; }

    public GitHubDownloadCommand()
        : base("github", "Download latest release from GitHub repository.")
    {
        AddOption<bool>((v) => Prerelease = v, "--pre")
            .SetDescription("Get latest pre-release instead of stable.");

        AddOption<bool>((v) => UpdateReleasesFile = v, "--update-releases-file")
            .SetDescription("Create or update the local releases files.")
            .SetDefault(false);
    }
}
