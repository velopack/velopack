namespace Squirrel.Csq.Commands;

public class WindowsReleasifyCommand : WindowsSigningCommand
{
    public string Package { get; set; }

    public string BaseUrl { get; private set; }

    public string DebugSetupExe { get; private set; }

    public bool NoDelta { get; private set; }

    public string Runtimes { get; private set; }

    public string SplashImage { get; private set; }

    public string Icon { get; private set; }

    public string[] SquirrelAwareExecutableNames { get; private set; }

    public string AppIcon { get; private set; }

    public bool BuildMsi { get; private set; }

    public string MsiVersion { get; private set; }

    public WindowsReleasifyCommand()
        : this("releasify", "Take an existing nuget package and convert it into a Squirrel release.")
    {
        AddOption<FileInfo>((v) => Package = v.ToFullNameOrNull(), "-p", "--package")
            .SetDescription("Path to a '.nupkg' package to releasify.")
            .SetArgumentHelpName("PATH")
            .SetRequired()
            .AcceptExistingOnly()
            .RequiresExtension(".nupkg");
    }

    /// <summary>
    /// This constructor is used by the pack command, which requires all the same properties but 
    /// does not allow the user to provide the Package (it is created/populated by Squirrel).
    /// </summary>
    protected WindowsReleasifyCommand(string name, string description)
        : base(name, description)
    {
        AddOption<Uri>((v) => BaseUrl = v.ToAbsoluteOrNull(), "-b", "--baseUrl")
            .SetDescription("Provides a base URL to prefix the RELEASES file packages with.")
            .SetHidden()
            .MustBeValidHttpUri();

        AddOption<FileInfo>((v) => DebugSetupExe = v.ToFullNameOrNull(), "--debugSetupExe")
            .SetDescription("Uses the Setup.exe at this {PATH} to create the bundle, and then replaces it with the bundle. " +
                            "Used for locally debugging Setup.exe with a real bundle attached.")
            .SetArgumentHelpName("PATH")
            .SetHidden()
            .AcceptExistingOnly()
            .RequiresExtension(".exe");

        AddOption<bool>((v) => NoDelta = v, "--noDelta")
            .SetDescription("Skip the generation of delta packages.");

        AddOption<string>((v) => Runtimes = v, "-f", "--framework")
            .SetDescription("List of required runtimes to install during setup. example: 'net6,vcredist143'.")
            .SetArgumentHelpName("RUNTIMES")
            .MustBeValidFrameworkString();

        AddOption<FileInfo>((v) => SplashImage = v.ToFullNameOrNull(), "-s", "--splashImage")
            .SetDescription("Path to image displayed during installation.")
            .SetArgumentHelpName("PATH")
            .AcceptExistingOnly();

        AddOption<FileInfo>((v) => Icon = v.ToFullNameOrNull(), "-i", "--icon")
            .SetDescription("Path to .ico for Setup.exe and Update.exe.")
            .SetArgumentHelpName("PATH")
            .AcceptExistingOnly()
            .RequiresExtension(".ico");

        AddOption<string[]>((v) => SquirrelAwareExecutableNames = v ?? new string[0], "-e", "--mainExe")
            .SetDescription("Name of one or more SquirrelAware executables.")
            .SetArgumentHelpName("NAME");

        AddOption<FileInfo>((v) => AppIcon = v.ToFullNameOrNull(), "--appIcon")
            .SetDescription("Path to .ico for 'Apps and Features' list.")
            .SetArgumentHelpName("PATH")
            .AcceptExistingOnly()
            .RequiresExtension(".ico");

        if (SquirrelRuntimeInfo.IsWindows) {
            AddOption<bool>((v) => BuildMsi = v, "--msi")
                .SetDescription("Compile a .msi machine-wide deployment tool.")
                .SetArgumentHelpName("BITNESS");

            AddOption<string>((v) => MsiVersion = v, "--msiVersion")
                .SetDescription("Override the product version for the generated msi.")
                .SetArgumentHelpName("VERSION")
                .MustBeValidMsiVersion();
        }
    }
}
