using System;

namespace Squirrel.CommandLine.Commands
{
    public class GitHubBaseCommand : BaseCommand
    {
        public string RepoUrl { get; private set; }

        public string Token { get; private set; }

        protected GitHubBaseCommand(string name, string description)
            : base(name, description)
        {
            AddOption<Uri>("--repoUrl", (v) => RepoUrl = v?.AbsoluteUri)
                .SetDescription("Full url to the github repository (eg. 'https://github.com/myname/myrepo').")
                .SetRequired()
                .MustBeValidHttpUri();

            AddOption<string>("--token", (v) => Token = v)
                .SetDescription("OAuth token to use as login credentials.");
        }
    }

    public class GitHubDownloadCommand : GitHubBaseCommand
    {
        public bool Pre { get; private set; }

        public GitHubDownloadCommand()
            : base("github", "Download latest release from GitHub repository.")
        {
            AddOption<bool>("--pre", (v) => Pre = v)
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
            AddOption<bool>("--publish", (v) => Publish = v)
                .SetDescription("Publish release instead of creating draft.");

            AddOption<string>("--releaseName", (v) => ReleaseName = v)
                .SetDescription("A custom name for created release.")
                .SetArgumentHelpName("NAME");

            ReleaseDirectoryOption.MustNotBeEmpty();
        }
    }
}
