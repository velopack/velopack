using System.Threading;

namespace Velopack.Vpk.Commands.Deployment;

public class HttpDownloadCommand : OutputCommand
{
    public string Url { get; private set; }

    public double Timeout { get; private set; }

    public string[] Headers { get; private set; }

    public bool AllowEmptyChannel { get; private set; }

    public HttpDownloadCommand()
        : base("http", "Download latest release from a HTTP source.")
    {
        AddOption<string>((v) => Url = v, ["--url"])
            .SetArgumentHelpName("URL")
            .SetDescription("Url to download remote releases from.");

        AddOption<double>((v) => Timeout = v, ["--timeout"])
            .SetDescription("Network timeout in minutes.")
            .SetArgumentHelpName("MINUTES")
            .SetDefault(30);

        AddOption<string[]>((v) => Headers = v, ["--header"])
            .SetDescription("Add a custom header to all http requests (eg. 'Authorization: Bearer ...'). Can be used multiple times.")
            .SetArgumentHelpName("NAME:VALUE");

        AddOption<bool>((v) => AllowEmptyChannel = v, ["--allowEmptyChannel"])
            .SetDescription("Exit successfully with an empty result if the remote releases file for the channel does not exist.");
    }
}
