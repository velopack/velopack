namespace Velopack.Vpk.Commands.Deployment;

public abstract class GitHubBaseCommand : OutputCommand
{
    public string RepoUrl { get; private set; }

    public string Token { get; private set; }

    public double Timeout { get; private set; }

    protected GitHubBaseCommand(string name, string description)
        : base(name, description)
    {
        var repoUrl = AddOption<Uri>((v) => RepoUrl = v.ToAbsoluteOrNull(), "--repoUrl")
            .SetDescription("Full url to the github repository (eg. 'https://github.com/myname/myrepo').")
            .SetRequired()
            .MustBeValidHttpUri();

        AddOption<string>((v) => Token = v, "--token")
            .SetDescription("OAuth token to use as login credentials.");

        AddOption<double>((v) => Timeout = v, "--timeout")
            .SetDescription("Network timeout in minutes.")
            .SetArgumentHelpName("MINUTES")
            .SetDefault(30);
    }
}
