namespace Velopack.Vpk.Commands.Deployment;

public class LocalBaseCommand : OutputCommand
{
    public DirectoryInfo TargetPath { get; private set; }

    public CliOption<DirectoryInfo> TargetPathOption { get; private set; }

    public LocalBaseCommand(string command, string description)
        : base(command, description)
    {
        TargetPathOption = AddOption<DirectoryInfo>((p) => TargetPath = p, "--path")
            .SetDescription("Target file path to copy releases to/from.")
            .SetArgumentHelpName("PATH")
            .SetRequired();
    }
}
