using Microsoft.Extensions.Logging;
using Velopack.Deployment;

namespace Velopack.Vpk.Commands;

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
