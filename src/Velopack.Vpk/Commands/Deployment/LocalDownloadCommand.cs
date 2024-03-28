namespace Velopack.Vpk.Commands.Deployment;

public class LocalDownloadCommand : LocalBaseCommand
{
    public LocalDownloadCommand()
        : base("local", "Download latest release from a local path or network share.")
    {
        TargetPathOption.MustNotBeEmpty();
        TargetPathOption.MustExist();
    }
}
