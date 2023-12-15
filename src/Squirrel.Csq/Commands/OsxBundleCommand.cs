namespace Squirrel.Csq.Commands;

public class OsxBundleCommand : BaseCommand
{
    public string PackId { get; private set; }

    public string PackVersion { get; private set; }

    public string PackDirectory { get; private set; }

    public string PackAuthors { get; private set; }

    public string PackTitle { get; private set; }

    public string EntryExecutableName { get; private set; }

    public string Icon { get; private set; }

    public string BundleId { get; private set; }

    public OsxBundleCommand()
        : base("bundle", "Create's an OSX .app bundle from a folder containing application files.")
    {
        AddOption<string>((v) => PackId = v, "--packId", "-u")
            .SetDescription("Unique Squirrel Id for application bundle.")
            .SetArgumentHelpName("ID")
            .SetRequired()
            .RequiresValidNuGetId();

        // TODO add parser straight to SemanticVersion?
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

        AddOption<string>((v) => EntryExecutableName = v, "-e", "--mainExe")
            .SetDescription("The file name of the main/entry executable.")
            .SetArgumentHelpName("NAME")
            .SetRequired();

        AddOption<FileInfo>((v) => Icon = v.ToFullNameOrNull(), "-i", "--icon")
            .SetDescription("Path to the .icns file for this bundle.")
            .SetArgumentHelpName("PATH")
            .AcceptExistingOnly()
            .SetRequired()
            .RequiresExtension(".icns");

        AddOption<string>((v) => BundleId = v, "--bundleId")
            .SetDescription("Optional Apple bundle Id.")
            .SetArgumentHelpName("ID");
    }
}
