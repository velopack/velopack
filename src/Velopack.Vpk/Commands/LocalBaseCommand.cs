namespace Velopack.Vpk.Commands;
public abstract class LocalBaseCommand : OutputCommand
{
    public DirectoryInfo Path { get; private set; }

    protected CliOption<DirectoryInfo> PathOption { get; private set; }

    public LocalBaseCommand(string name, string description)
        : base(name, description)
    {
        PathOption = AddOption<DirectoryInfo>((v) => Path = v, "-p", "--path")
            .MustExist()
            .SetRequired();
    }
}
