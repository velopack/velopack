
using Velopack.Packaging;

namespace Velopack.Vpk.Commands;

public class WindowsPackCommand : WindowsReleasifyCommand, INugetPackCommand
{
    public string PackId { get; private set; }

    public string PackVersion { get; private set; }

    public string PackDirectory { get; private set; }

    public string PackAuthors { get; private set; }

    public string PackTitle { get; private set; }

    public bool IncludePdb { get; private set; }

    public string ReleaseNotes { get; private set; }

    public WindowsPackCommand()
        : base("pack", "Creates a release from a folder containing application files.")
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
            .MustExist();
    }
}