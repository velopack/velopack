using System;

namespace Squirrel.CommandLine.Commands
{
    public class HttpDownloadCommand : BaseCommand
    {
        public string Url { get; private set; }

        public HttpDownloadCommand()
            : base("http", "Download latest release from a HTTP source.")
        {
            AddOption<Uri>((v) => Url = v.ToAbsoluteOrNull(), "--url")
                .SetDescription("Url to download remote releases from.")
                .MustBeValidHttpUri()
                .SetRequired();
        }
    }
}
