using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;
using Squirrel.CommandLine.Sync;

namespace Squirrel.CommandLine.Deployment
{
    public class HttpCommand : Command
    {
        public HttpCommand() : base("http", "Download from HTTP")
        {
            Add(new HttpDownloadCommand());
        }
    }

    internal class HttpDownloadCommand : BaseCommand
    {
        public Option<Uri> Url { get; }

        public HttpDownloadCommand()
            : base("down", "Download latest release from HTTP")
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
            await new SimpleWebRepository(options).DownloadRecentPackages();
        }
    }
}
