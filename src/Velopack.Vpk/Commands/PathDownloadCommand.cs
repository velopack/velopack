namespace Velopack.Vpk.Commands;

public class PathDownloadCommand : OutputCommand
{
    public DirectoryInfo Path { get; private set; }

    public PathDownloadCommand()
        : base("path", "Download latest release from a specific path source.")
    {
        AddOption<DirectoryInfo>((p) => Path = p, "--path")
            .SetDescription("Path to download releases from.")
            .MustExist()
            .SetRequired();
    }
}
