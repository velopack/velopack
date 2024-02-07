namespace Velopack.Vpk.Commands;

public class LocalDownloadCommand : LocalBaseCommand
{
    public LocalDownloadCommand()
        : base("local", "Download latest release from a local path source.")
    {
        PathOption.SetDescription("Path to download releases from.");
    }
}
