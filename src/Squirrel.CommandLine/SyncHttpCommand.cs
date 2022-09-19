using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Squirrel.CommandLine.Sync;

namespace Squirrel.CommandLine
{
    internal class SyncHttpCommand : BaseCommand
    {
        public Option<Uri> Url { get; }
        
        public SyncHttpCommand()
            : base("http-down", "Download latest release from HTTP")
        {
            Url = new Option<Uri>("--url", "URL to download from") {
                ArgumentHelpName = "URL",
                IsRequired = true,
            };
            Url.MustBeValidHttpUri();
            Add(Url);

            this.SetHandler(Execute);
        }

        private protected void SetOptionsValues(InvocationContext context, SyncHttpOptions options)
        {
            base.SetOptionsValues(context, options);
            options.url = context.ParseResult.GetValueForOption(Url)?.AbsoluteUri;
        }

        public async Task Execute(InvocationContext context)
        {
            SyncHttpOptions options = new();
            SetOptionsValues(context, options);
            await Download(new SimpleWebRepository(options));
        }
        
        private static Task Download<T>(T repo) where T : IPackageRepository => repo.DownloadRecentPackages();
    }
}
