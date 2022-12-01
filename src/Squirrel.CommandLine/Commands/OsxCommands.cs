using System.CommandLine;
using System.IO;

namespace Squirrel.CommandLine.Commands
{
    public class BundleOsxCommand : BaseCommand
    {
        public string PackId { get; private set; }

        public string PackVersion { get; private set; }

        public DirectoryInfo PackDirectory { get; private set; }

        public string PackAuthors { get; private set; }

        public string PackTitle { get; private set; }

        public string EntryExecutableName { get; private set; }

        public FileInfo Icon { get; private set; }

        public string BundleId { get; private set; }

        public BundleOsxCommand()
            : base("bundle", "Create's an OSX .app bundle from a folder containing application files.")
        {
            AddOption<string>(new[] { "--packId", "-u" }, (v) => PackId = v)
                .SetDescription("Unique Squirrel Id for application bundle.")
                .SetArgumentHelpName("ID")
                .SetRequired()
                .RequiresValidNuGetId();

            // TODO add parser straight to SemanticVersion?
            AddOption<string>(new[] { "--packVersion", "-v" }, (v) => PackVersion = v)
                .SetDescription("Current version for application bundle.")
                .SetArgumentHelpName("VERSION")
                .SetRequired()
                .RequiresSemverCompliant();

            AddOption<DirectoryInfo>(new[] { "--packDir", "-p" }, (v) => PackDirectory = v)
                .SetDescription("Directory containing application files for release.")
                .SetArgumentHelpName("DIRECTORY")
                .SetRequired()
                .MustNotBeEmpty();

            AddOption<string>("--packAuthors", (v) => PackAuthors = v)
                .SetDescription("Company name or comma-delimited list of authors.")
                .SetArgumentHelpName("AUTHORS");

            AddOption<string>("--packTitle", (v) => PackTitle = v)
                .SetDescription("Display/friendly name for application.")
                .SetArgumentHelpName("NAME");

            AddOption<string>(new[] { "-e", "--mainExe" }, (v) => EntryExecutableName = v)
                .SetDescription("The file name of the main/entry executable.")
                .SetArgumentHelpName("NAME")
                .SetRequired();

            AddOption<FileInfo>(new[] { "-i", "--icon" }, (v) => Icon = v)
                .SetDescription("Path to the .icns file for this bundle.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly()
                .SetRequired()
                .RequiresExtension(".icns");

            AddOption<string>("--bundleId", (v) => BundleId = v)
                .SetDescription("Optional Apple bundle Id.")
                .SetArgumentHelpName("ID");
        }
    }

    public class ReleasifyOsxCommand : BaseCommand
    {
        public DirectoryInfo BundleDirectory { get; private set; }

        public bool IncludePdb { get; private set; }

        public FileInfo ReleaseNotes { get; private set; }

        public bool NoDelta { get; private set; }

        public bool NoPackage { get; private set; }

        public FileInfo PackageWelcome { get; private set; }

        public FileInfo PackageReadme { get; private set; }

        public FileInfo PackageLicense { get; private set; }

        public FileInfo PackageConclusion { get; private set; }

        public string SigningAppIdentity { get; private set; }

        public string SigningInstallIdentity { get; private set; }

        public FileInfo SigningEntitlements { get; private set; }

        public string NotaryProfile { get; private set; }

        public ReleasifyOsxCommand()
            : base("releasify", "Converts an application bundle into a Squirrel release and installer.")
        {
            AddOption<DirectoryInfo>(new[] { "-b", "--bundle" }, (v) => BundleDirectory = v)
                .SetDescription("The bundle to convert into a Squirrel release.")
                .SetArgumentHelpName("PATH")
                .MustNotBeEmpty()
                .RequiresExtension(".app")
                .SetRequired();

            AddOption<bool>("--includePdb", (v) => IncludePdb = v)
                .SetDescription("Add *.pdb files to release package.");

            AddOption<FileInfo>("--releaseNotes", (v) => ReleaseNotes = v)
                .SetDescription("File with markdown-formatted notes for this version.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly();

            AddOption<bool>("--noDelta", (v) => NoDelta = v)
                .SetDescription("Skip the generation of delta packages.");

            AddOption<bool>("--noPkg", (v) => NoPackage = v)
                .SetDescription("Skip generating a .pkg installer.");

            AddOption<FileInfo>("--pkgWelcome", (v) => PackageWelcome = v)
                .SetDescription("Set the installer package welcome content.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly();

            AddOption<FileInfo>("--pkgReadme", (v) => PackageReadme = v)
                .SetDescription("Set the installer package readme content.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly();

            AddOption<FileInfo>("--pkgLicense", (v) => PackageLicense = v)
                .SetDescription("Set the installer package license content.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly();

            AddOption<FileInfo>("--pkgConclusion", (v) => PackageConclusion = v)
                .SetDescription("Set the installer package conclusion content.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly();

            AddOption<string>("--signAppIdentity", (v) => SigningAppIdentity = v)
                .SetDescription("The subject name of the cert to use for app code signing.")
                .SetArgumentHelpName("SUBJECT");

            AddOption<string>("--signInstallIdentity", (v) => SigningInstallIdentity = v)
                .SetDescription("The subject name of the cert to use for installation packages.")
                .SetArgumentHelpName("SUBJECT");

            AddOption<FileInfo>("--signEntitlements", (v) => SigningEntitlements = v)
                .SetDescription("Path to entitlements file for hardened runtime signing.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly()
                .RequiresExtension(".entitlements");

            AddOption<string>("--notaryProfile", (v) => NotaryProfile = v)
                .SetDescription("Name of profile containing Apple credentials stored with notarytool.")
                .SetArgumentHelpName("NAME");
        }
    }
}