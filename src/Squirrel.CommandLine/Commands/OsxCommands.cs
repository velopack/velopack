using System.CommandLine;
using System.IO;

namespace Squirrel.CommandLine.Commands
{
    public class BundleOsxCommand : BaseCommand
    {
        public string PackId { get; private set; }

        public string PackVersion { get; private set; }

        public string PackDirectory { get; private set; }

        public string PackAuthors { get; private set; }

        public string PackTitle { get; private set; }

        public string EntryExecutableName { get; private set; }

        public string Icon { get; private set; }

        public string BundleId { get; private set; }

        public BundleOsxCommand()
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
                .ExistingOnly()
                .SetRequired()
                .RequiresExtension(".icns");

            AddOption<string>((v) => BundleId = v, "--bundleId")
                .SetDescription("Optional Apple bundle Id.")
                .SetArgumentHelpName("ID");
        }
    }

    public class ReleasifyOsxCommand : BaseCommand
    {
        public string BundleDirectory { get; private set; }

        public bool IncludePdb { get; private set; }

        public string ReleaseNotes { get; private set; }

        public bool NoDelta { get; private set; }

        public bool NoPackage { get; private set; }

        public string PackageWelcome { get; private set; }

        public string PackageReadme { get; private set; }

        public string PackageLicense { get; private set; }

        public string PackageConclusion { get; private set; }

        public string SigningAppIdentity { get; private set; }

        public string SigningInstallIdentity { get; private set; }

        public string SigningEntitlements { get; private set; }

        public string NotaryProfile { get; private set; }

        public ReleasifyOsxCommand()
            : base("releasify", "Converts an application bundle into a Squirrel release and installer.")
        {
            AddOption<DirectoryInfo>((v) => BundleDirectory = v.ToFullNameOrNull(), "-b", "--bundle")
                .SetDescription("The bundle to convert into a Squirrel release.")
                .SetArgumentHelpName("PATH")
                .MustNotBeEmpty()
                .RequiresExtension(".app")
                .SetRequired();

            AddOption<bool>((v) => IncludePdb = v, "--includePdb")
                .SetDescription("Add *.pdb files to release package.");

            AddOption<FileInfo>((v) => ReleaseNotes = v.ToFullNameOrNull(), "--releaseNotes")
                .SetDescription("File with markdown-formatted notes for this version.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly();

            AddOption<bool>((v) => NoDelta = v, "--noDelta")
                .SetDescription("Skip the generation of delta packages.");

            AddOption<bool>((v) => NoPackage = v, "--noPkg")
                .SetDescription("Skip generating a .pkg installer.");

            AddOption<FileInfo>((v) => PackageWelcome = v.ToFullNameOrNull(), "--pkgWelcome")
                .SetDescription("Set the installer package welcome content.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly();

            AddOption<FileInfo>((v) => PackageReadme = v.ToFullNameOrNull(), "--pkgReadme")
                .SetDescription("Set the installer package readme content.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly();

            AddOption<FileInfo>((v) => PackageLicense = v.ToFullNameOrNull(), "--pkgLicense")
                .SetDescription("Set the installer package license content.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly();

            AddOption<FileInfo>((v) => PackageConclusion = v.ToFullNameOrNull(), "--pkgConclusion")
                .SetDescription("Set the installer package conclusion content.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly();

            AddOption<string>((v) => SigningAppIdentity = v, "--signAppIdentity")
                .SetDescription("The subject name of the cert to use for app code signing.")
                .SetArgumentHelpName("SUBJECT");

            AddOption<string>((v) => SigningInstallIdentity = v, "--signInstallIdentity")
                .SetDescription("The subject name of the cert to use for installation packages.")
                .SetArgumentHelpName("SUBJECT");

            AddOption<FileInfo>((v) => SigningEntitlements = v.ToFullNameOrNull(), "--signEntitlements")
                .SetDescription("Path to entitlements file for hardened runtime signing.")
                .SetArgumentHelpName("PATH")
                .ExistingOnly()
                .RequiresExtension(".entitlements");

            AddOption<string>((v) => NotaryProfile = v, "--notaryProfile")
                .SetDescription("Name of profile containing Apple credentials stored with notarytool.")
                .SetArgumentHelpName("NAME");
        }
    }
}