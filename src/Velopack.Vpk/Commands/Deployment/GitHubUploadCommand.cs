namespace Velopack.Vpk.Commands.Deployment;

public class GitHubUploadCommand : GitHubBaseCommand
{
    public bool Publish { get; private set; }

    public string ReleaseName { get; private set; }

    public string TagName { get; private set; }

    public string TargetCommitish { get; private set; }

    public bool Prerelease { get; private set; }

    public bool Merge { get; private set; }

    public TimeSpan? Timeout { get; private set; }

    public GitHubUploadCommand()
        : base("github", "Upload releases to a GitHub repository.")
    {
        AddOption<bool>((v) => Publish = v, "--publish")
            .SetDescription("Create and publish instead of leaving as draft.");

        AddOption<bool>((v) => Prerelease = v, "--pre")
            .SetDescription("Create as pre-release instead of stable.");

        AddOption<bool>((v) => Merge = v, "--merge")
            .SetDescription("Allow merging this upload with an existing release.");

        AddOption<string>((v) => ReleaseName = v, "--releaseName")
            .SetDescription("A custom name for the release.")
            .SetArgumentHelpName("NAME");

        AddOption<string>((v) => TagName = v, "--tag")
            .SetDescription("A custom tag for the release.")
            .SetArgumentHelpName("NAME");

        AddOption<string>((v) => TargetCommitish = v, "--targetCommitish")
           .SetDescription("A commitish value for tag (branch or commit SHA).")
           .SetArgumentHelpName("NAME");

        AddOption<TimeSpan>((v) => Timeout = v, "--timeout")
            .SetDescription("The maximum time to wait for the upload to complete.")
            .SetArgumentHelpName("TIMEOUT");

        ReleaseDirectoryOption.SetRequired();
        ReleaseDirectoryOption.MustNotBeEmpty();
    }
}
