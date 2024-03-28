namespace Velopack.Vpk.Commands.Deployment;

public class LocalBaseCommand : OutputCommand
{
    public DirectoryInfo TargetPath { get; private set; }

    public LocalBaseCommand(string command, string description)
        : base(command, description)
    {
        AddOption<DirectoryInfo>((p) => TargetPath = p, "--path")
            .SetDescription("File path to copy releases from.")
            .SetArgumentHelpName("PATH")
            .MustNotBeEmpty()
            .MustExist()
            .SetRequired();
    }
}
