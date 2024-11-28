using System.Threading;

namespace Velopack.Vpk.Commands.Deployment;

public class HttpDownloadCommand : OutputCommand
{
    public string Url { get; private set; }

    public double Timeout { get; private set; }

    public HttpDownloadCommand()
        : base("http", "Download latest release from a HTTP source.")
    {
        AddOption<Uri>((v) => Url = v.ToAbsoluteOrNull(), "--url")
            .SetDescription("Url to download remote releases from.")
            .MustBeValidHttpUri()
            .SetRequired();

        AddOption<double>((v) => Timeout = v, "--timeout")
            .SetDescription("Network timeout in minutes.")
            .SetArgumentHelpName("MINUTES")
            .SetDefault(30);
    }
}
