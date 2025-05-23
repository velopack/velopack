
using Velopack.Packaging;
using Velopack.Packaging.Compression;

namespace Velopack.Vpk.Commands.Packaging;

public abstract class PackCommand : PlatformCommand
{
    public string PackId { get; private set; }

    protected CliOption<string> PackIdOption { get; private set; }

    public string PackVersion { get; private set; }

    protected CliOption<string> PackVersionOption { get; private set; }

    public string PackDirectory { get; private set; }

    protected CliOption<FileSystemInfo> PackDirectoryOption { get; private set; }

    public string PackAuthors { get; private set; }

    protected CliOption<string> PackAuthorsOption { get; private set; }

    public string PackTitle { get; private set; }

    protected CliOption<string> PackTitleOption { get; private set; }

    public string EntryExecutableName { get; private set; }

    protected CliOption<string> EntryExecutableNameOption { get; private set; }

    public string Icon { get; private set; }

    protected CliOption<FileInfo> IconOption { get; private set; }

    public string ReleaseNotes { get; private set; }

    protected CliOption<FileInfo> ReleaseNotesOption { get; private set; }

    public DeltaMode DeltaMode { get; private set; }

    protected CliOption<DeltaMode> DeltaModeOption { get; private set; }

    public string Exclude { get; private set; }

    protected CliOption<string> ExcludeOption { get; private set; }

    public bool NoPortable { get; private set; }

    protected CliOption<bool> NoPortableOption { get; private set; }

    public bool NoInst { get; private set; }

    protected CliOption<bool> NoInstOption { get; private set; }

    public PackCommand(string name, string description, RuntimeOs targetOs = RuntimeOs.Unknown)
        : base(name, description, targetOs)
    {
        PackIdOption = AddOption<string>((v) => PackId = v, "--packId", "-u")
           .SetDescription("Unique Id for application bundle.")
           .SetArgumentHelpName("ID")
           .SetRequired()
           .RequiresValidNuGetId();

        // TODO add parser straight to SemanticVersion
        PackVersionOption = AddOption<string>((v) => PackVersion = v, "--packVersion", "-v")
            .SetDescription("Current version for application bundle.")
            .SetArgumentHelpName("VERSION")
            .SetRequired()
            .RequiresSemverCompliant();

        PackDirectoryOption = AddOption<FileSystemInfo>((v) => PackDirectory = v.ToFullNameOrNull(), "--packDir", "-p")
            .SetDescription("Directory containing application files for release.")
            .SetArgumentHelpName("DIR")
            .SetRequired();

        PackAuthorsOption = AddOption<string>((v) => PackAuthors = v, "--packAuthors")
            .SetDescription("Company name or comma-delimited list of authors.")
            .SetArgumentHelpName("AUTHORS");

        PackTitleOption = AddOption<string>((v) => PackTitle = v, "--packTitle")
            .SetDescription("Display/friendly name for application.")
            .SetArgumentHelpName("NAME");

        ReleaseNotesOption = AddOption<FileInfo>((v) => ReleaseNotes = v.ToFullNameOrNull(), "--releaseNotes")
            .SetDescription("File with markdown-formatted notes for this version.")
            .SetArgumentHelpName("PATH")
            .MustExist();

        DeltaModeOption = AddOption<DeltaMode>((v) => DeltaMode = v, "--delta")
            .SetDefault(DeltaMode.BestSpeed)
            .SetDescription("Disable or set the delta generation mode.");

        IconOption = AddOption<FileInfo>((v) => Icon = v.ToFullNameOrNull(), "-i", "--icon")
            .SetDescription($"Path to icon file for package.")
            .SetArgumentHelpName("PATH")
            .MustExist();

        EntryExecutableNameOption = AddOption<string>((v) => EntryExecutableName = v, "-e", "--mainExe")
            .SetDescription("The file name (not path) of the main/entry executable.")
            .SetArgumentHelpName("NAME");

        ExcludeOption = AddOption<string>((v) => Exclude = v, "--exclude")
            .SetDescription("A regex which excludes matched files from the package.")
            .SetArgumentHelpName("REGEX")
            .SetDefault(@".*\.pdb");

        NoPortableOption = AddOption<bool>((v) => NoPortable = v, "--noPortable")
            .SetDescription("Skip generating a portable bundle.")
            .SetHidden(true);

        NoInstOption = AddOption<bool>((v) => NoInst = v, "--noInst")
            .SetDescription("Skip generating an installer package.")
            .SetHidden(true);

        this.AreMutuallyExclusive(NoPortableOption, NoInstOption);
    }
}
