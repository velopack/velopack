using System.CommandLine;
using Squirrel.CommandLine.Sync;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using System;

namespace Squirrel.CommandLine.Deployment
{
    public class GitHubBaseCommand : BaseCommand
    {
        public Option<Uri> RepoUrl { get; }
        public Option<string> Token { get; }

        protected GitHubBaseCommand(string name, string description)
            : base(name, description)
        {
            RepoUrl = new Option<Uri>("--repoUrl", "Full url to the github repository\nexample: 'https://github.com/myname/myrepo'.") {
                IsRequired = true
            };
            RepoUrl.MustBeValidHttpUri();
            Add(RepoUrl);

            Token = new Option<string>("--token", "OAuth token to use as login credentials.");
            Add(Token);
        }

        private protected void SetOptionsValues(InvocationContext context, SyncGithubOptions options)
        {
            base.SetOptionsValues(context, options);
            options.repoUrl = context.ParseResult.GetValueForOption(RepoUrl)?.AbsoluteUri;
            options.token = context.ParseResult.GetValueForOption(Token);
        }
    }

    public class GitHubDownloadCommand : GitHubBaseCommand
    {
        public Option<bool> Pre { get; }

        public GitHubDownloadCommand()
            : base("github", "Download latest release from GitHub repository.")
        {
            Pre = new Option<bool>("--pre", "Get latest pre-release instead of stable.");
            Add(Pre);

            this.SetHandler(Execute);
        }

        private protected new void SetOptionsValues(InvocationContext context, SyncGithubOptions options)
        {
            base.SetOptionsValues(context, options);
            options.pre = context.ParseResult.GetValueForOption(Pre);
        }

        private async Task Execute(InvocationContext context)
        {
            SyncGithubOptions options = new();
            SetOptionsValues(context, options);
            await new GitHubRepository(options).DownloadRecentPackages();
        }
    }

    public class GitHubUploadCommand : GitHubBaseCommand
    {
        public Option<bool> Publish { get; }
        public Option<string> ReleaseName { get; }

        public GitHubUploadCommand()
            : base("github", "Upload latest release to a GitHub repository.")
        {
            Publish = new Option<bool>("--publish", "Publish release instead of creating draft.");
            Add(Publish);

            ReleaseName = new Option<string>("--releaseName", "A custom {NAME} for created release.") {
                ArgumentHelpName = "NAME"
            };
            Add(ReleaseName);

            this.SetHandler(Execute);
        }

        //Intentionally hiding base member
        private protected new void SetOptionsValues(InvocationContext context, SyncGithubOptions options)
        {
            base.SetOptionsValues(context, options);
            options.publish = context.ParseResult.GetValueForOption(Publish);
            options.releaseName = context.ParseResult.GetValueForOption(ReleaseName);
        }

        private async Task Execute(InvocationContext context)
        {
            SyncGithubOptions options = new();
            SetOptionsValues(context, options);
            await new GitHubRepository(options).UploadMissingPackages();
        }
    }
}
