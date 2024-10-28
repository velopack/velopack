namespace Velopack.Vpk.Commands.Deployment;

public class HttpDownloadCommand : OutputCommand
{
    public bool UpdateReleasesFile { get; set; }

    public string Url { get; private set; }

    public HttpDownloadCommand()
        : base("http", "Download latest release from a HTTP source.")
    {
        AddOption<Uri>((v) => Url = v.ToAbsoluteOrNull(), "--url")
            .SetDescription("Url to download remote releases from.")
            .MustBeValidHttpUri()
            .SetRequired();

        AddOption<bool>((v) => UpdateReleasesFile = v, "--update-releases-file")
            .SetDescription("Create or update the local releases files.")
            .SetDefault(false);
    }
}
