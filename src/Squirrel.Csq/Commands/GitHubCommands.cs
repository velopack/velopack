using Microsoft.Extensions.Logging;
using Squirrel.Deployment;

namespace Squirrel.Csq.Commands;

public abstract class GitHubBaseCommand : BaseCommand
{
    public string RepoUrl { get; private set; }

    public string Token { get; private set; }

    protected GitHubBaseCommand(string name, string description)
        : base(name, description)
    {
        AddOption<Uri>((v) => RepoUrl = v.ToAbsoluteOrNull(), "--repoUrl")
            .SetDescription("Full url to the github repository (eg. 'https://github.com/myname/myrepo').")
            .SetRequired()
            .MustBeValidHttpUri();

        AddOption<string>((v) => Token = v, "--token")
            .SetDescription("OAuth token to use as login credentials.");
    }
}

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

public class GitHubUploadCommand : GitHubBaseCommand
{
    public bool Publish { get; private set; }

    public string ReleaseName { get; private set; }

    public GitHubUploadCommand()
        : base("github", "Upload releases to a GitHub repository.")
    {
        AddOption<bool>((v) => Publish = v, "--publish")
            .SetDescription("Publish release instead of creating draft.");

        AddOption<string>((v) => ReleaseName = v, "--releaseName")
            .SetDescription("A custom name for created release.")
            .SetArgumentHelpName("NAME");

        ReleaseDirectoryOption.SetRequired();
        ReleaseDirectoryOption.MustNotBeEmpty();
    }
}
