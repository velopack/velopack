namespace Velopack.Vpk.Commands;

public class LinuxPackCommand : PackCommand
{
    public bool PackIsAppDir { get; private set; }

    public LinuxPackCommand()
        : base("pack", "Create's a Linux .AppImage bundle from a folder containing application files.")
    {
        this.RemoveOption(NoPortableOption);
        this.RemoveOption(NoInstOption);

        var appDir = AddOption<DirectoryInfo>((v) => {
            var t = v.ToFullNameOrNull();
            if (t != null) {
                PackDirectory = t;
                PackIsAppDir = true;
            }
        }, "--appDir")
            .SetDescription("Directory containing application in .AppDir format")
            .SetArgumentHelpName("DIR")
            .MustNotBeEmpty();

        this.AtLeastOneRequired(PackDirectoryOption, appDir);
        this.AreMutuallyExclusive(PackDirectoryOption, appDir);
        this.AreMutuallyExclusive(IconOption, appDir);
    }
}
