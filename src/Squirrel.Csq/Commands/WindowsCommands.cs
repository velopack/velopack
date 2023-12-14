
namespace Squirrel.Csq.Commands;

public class SigningCommand : BaseCommand
{
    public string SignParameters { get; private set; }

    public bool SignSkipDll { get; private set; }

    public int SignParallel { get; private set; }

    public string SignTemplate { get; private set; }

    protected SigningCommand(string name, string description)
        : base(name, description)
    {
        var signTemplate = AddOption<string>((v) => SignTemplate = v, "--signTemplate")
            .SetDescription("Use a custom signing command. {{file}} will be replaced by the path to sign.")
            .SetArgumentHelpName("COMMAND")
            .MustContain("{{file}}");

        AddOption<bool>((v) => SignSkipDll = v, "--signSkipDll")
            .SetDescription("Only signs EXE files, and skips signing DLL files.");

        if (SquirrelRuntimeInfo.IsWindows) {
            var signParams = AddOption<string>((v) => SignParameters = v, "--signParams", "-n")
                .SetDescription("Sign files via signtool.exe using these parameters.")
                .SetArgumentHelpName("PARAMS");

            this.AreMutuallyExclusive(signTemplate, signParams);

            AddOption<int>((v) => SignParallel = v, "--signParallel")
                .SetDescription("The number of files to sign in each call to signtool.exe.")
                .SetArgumentHelpName("NUM")
                .MustBeBetween(1, 1000)
                .SetDefault(10);
        }
    }
}

public class ReleasifyWindowsCommand : SigningCommand
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

    public ReleasifyWindowsCommand()
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
    protected ReleasifyWindowsCommand(string name, string description)
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

public class PackWindowsCommand : ReleasifyWindowsCommand, INugetPackCommand
{
    public string PackId { get; private set; }

    public string PackVersion { get; private set; }

    public string PackDirectory { get; private set; }

    public string PackAuthors { get; private set; }

    public string PackTitle { get; private set; }

    public bool IncludePdb { get; private set; }

    public string ReleaseNotes { get; private set; }

    public PackWindowsCommand()
        : base("pack", "Creates a Squirrel release from a folder containing application files.")
    {
        AddOption<string>((v) => PackId = v, "--packId", "-u")
            .SetDescription("Unique Id for application bundle.")
            .SetArgumentHelpName("ID")
            .SetRequired()
            .RequiresValidNuGetId();

        // TODO add parser straight to SemanticVersion
        AddOption<string>((v) => PackVersion = v, "--packVersion", "-v")
            .SetDescription("Current version for application bundle.")
            .SetArgumentHelpName("VERSION")
            .SetRequired()
            .RequiresSemverCompliant();

        AddOption<DirectoryInfo>((v) => PackDirectory = v.ToFullNameOrNull(), "--packDir", "-p")
            .SetDescription("Directory containing application files for release.")
            .SetArgumentHelpName("DIR")
            .SetRequired()
            .MustNotBeEmpty();

        AddOption<string>((v) => PackAuthors = v, "--packAuthors")
            .SetDescription("Company name or comma-delimited list of authors.")
            .SetArgumentHelpName("AUTHORS");

        AddOption<string>((v) => PackTitle = v, "--packTitle")
            .SetDescription("Display/friendly name for application.")
            .SetArgumentHelpName("NAME");

        AddOption<bool>((v) => IncludePdb = v, "--includePdb")
            .SetDescription("Add *.pdb files to release package");

        AddOption<FileInfo>((v) => ReleaseNotes = v.ToFullNameOrNull(), "--releaseNotes")
            .SetDescription("File with markdown-formatted notes for this version.")
            .SetArgumentHelpName("PATH")
            .AcceptExistingOnly();
    }
}