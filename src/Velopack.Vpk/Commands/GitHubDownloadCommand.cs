namespace Squirrel.Csq.Commands;

public class GitHubDownloadCommand : GitHubBaseCommand
{
    public bool Pre { get; private set; }

    public GitHubDownloadCommand()
        : base("github", "Download latest release from GitHub repository.")
    {
        AddOption<bool>((v) => Pre = v, "--pre")
            .SetDescription("Get latest pre-release instead of stable.");
    }
}
