namespace Velopack.Vpk.Commands;

public class LocalDownloadCommand : OutputCommand
{
    public DirectoryInfo Path { get; private set; }

    public LocalDownloadCommand()
        : base("local", "Download latest release from a local path source.")
    {
        AddOption<DirectoryInfo>((p) => Path = p, "--path")
            .SetDescription("Path to download releases from.")
            .MustExist()
            .SetRequired();
    }
}
