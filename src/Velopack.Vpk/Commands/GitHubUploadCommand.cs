namespace Velopack.Vpk.Commands;

public class GitHubUploadCommand : GitHubBaseCommand
{
    public bool Publish { get; private set; }

    public string ReleaseName { get; private set; }

    public bool Pre { get; private set; }

    public GitHubUploadCommand()
        : base("github", "Upload releases to a GitHub repository.")
    {
        AddOption<bool>((v) => Publish = v, "--publish")
            .SetDescription("Create and publish instead of leaving as draft.");

        AddOption<bool>((v) => Pre = v, "--pre")
            .SetDescription("Create as pre-release instead of stable.");

        AddOption<string>((v) => ReleaseName = v, "--releaseName")
            .SetDescription("A custom name for created release.")
            .SetArgumentHelpName("NAME");

        ReleaseDirectoryOption.SetRequired();
        ReleaseDirectoryOption.MustNotBeEmpty();
    }
}
