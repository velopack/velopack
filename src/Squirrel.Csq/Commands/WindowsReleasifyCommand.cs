namespace Squirrel.Csq.Commands;

public class WindowsReleasifyCommand : WindowsSigningCommand
{
    public string Package { get; set; }

    public bool NoDelta { get; private set; }

    public string Runtimes { get; private set; }

    public string SplashImage { get; private set; }

    public string Icon { get; private set; }

    public string EntryExecutableName { get; private set; }

    public string Channel { get; private set; }

    public WindowsReleasifyCommand()
        : this("releasify", "Take an existing nuget package and convert it into a Squirrel release.")
    {
        AddOption<FileInfo>((v) => Package = v.ToFullNameOrNull(), "-p", "--package")
            .SetDescription("Path to a '.nupkg' package to releasify.")
            .SetArgumentHelpName("PATH")
            .SetRequired()
            .MustExist()
            .RequiresExtension(".nupkg");
    }

    /// <summary>
    /// This constructor is used by the pack command, which requires all the same properties but 
    /// does not allow the user to provide the Package (it is created/populated by Squirrel).
    /// </summary>
    protected WindowsReleasifyCommand(string name, string description)
        : base(name, description)
    {
        AddOption<bool>((v) => NoDelta = v, "--noDelta")
            .SetDescription("Skip the generation of delta packages.");

        AddOption<string>((v) => Runtimes = v, "-f", "--framework")
            .SetDescription("List of required runtimes to install during setup. example: 'net6,vcredist143'.")
            .SetArgumentHelpName("RUNTIMES")
            .MustBeValidFrameworkString();

        AddOption<FileInfo>((v) => SplashImage = v.ToFullNameOrNull(), "-s", "--splashImage")
            .SetDescription("Path to image displayed during installation.")
            .SetArgumentHelpName("PATH")
            .MustExist();

        AddOption<FileInfo>((v) => Icon = v.ToFullNameOrNull(), "-i", "--icon")
            .SetDescription("Path to .ico for Setup.exe and Update.exe.")
            .SetArgumentHelpName("PATH")
            .MustExist()
            .RequiresExtension(".ico");

        AddOption<string>((v) => EntryExecutableName = v, "-e", "--mainExe")
            .SetDescription("The file name of the main/entry executable.")
            .SetArgumentHelpName("NAME")
            .RequiresExtension(".exe")
            .SetRequired();

        AddOption<string>((v) => Channel = v, "-c", "--channel")
            .SetDescription("Release channel to use when creating the package.")
            .SetArgumentHelpName("NAME");
    }
}
