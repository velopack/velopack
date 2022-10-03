using System;
using System.CommandLine;
using System.CommandLine.Invocation;
using System.Threading.Tasks;

namespace Squirrel.CommandLine.Commands
{
    public class HttpDownloadCommand : BaseCommand
    {
        public string Url { get; private set; }

        public HttpDownloadCommand()
            : base("http", "Download latest release from a HTTP source.")
        {
            AddOption<Uri>("--url", (v) => Url = v?.AbsoluteUri)
                .SetDescription("Url to download remote releases from.")
                .MustBeValidHttpUri();
        }
    }
}
