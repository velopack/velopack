namespace Velopack.Vpk.Commands;

public class LinuxPackCommand : PackCommand
{
    public string Categories { get; private set; }

    public LinuxPackCommand()
        : base("pack", "Create a Linux .AppImage bundle from application files.")
    {
        this.RemoveOption(NoPortableOption);
        this.RemoveOption(NoInstOption);

        AddOption<string>((v) => Categories = v, "--categories")
            .SetDescription("Categories from the freedesktop.org Desktop Menu spec")
            .SetArgumentHelpName("NAMES");
    }
}
