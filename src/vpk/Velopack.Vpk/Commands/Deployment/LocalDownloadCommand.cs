namespace Velopack.Vpk.Commands.Deployment;

public class LocalDownloadCommand : LocalBaseCommand
{
    public bool InfoJson { get; private set; }

    public LocalDownloadCommand()
        : base("local", "Download latest release from a local path or network share.")
    {
        AddOption<bool>((v) => InfoJson = v, ["--info-json"])
            .SetDescription("Print the latest release as JSON instead of downloading.");
    }
}
