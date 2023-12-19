namespace Squirrel.Csq.Commands;

public class OsxReleasifyCommand : BaseCommand
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

    public string Channel { get; private set; }

    public OsxReleasifyCommand()
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
            .AcceptExistingOnly();

        AddOption<bool>((v) => NoDelta = v, "--noDelta")
            .SetDescription("Skip the generation of delta packages.");

        AddOption<string>((v) => Channel = v, "-c", "--channel")
            .SetDescription("Release channel to use when creating the package.")
            .SetArgumentHelpName("NAME");

        AddOption<bool>((v) => NoPackage = v, "--noPkg")
            .SetDescription("Skip generating a .pkg installer.");

        AddOption<FileInfo>((v) => PackageWelcome = v.ToFullNameOrNull(), "--pkgWelcome")
            .SetDescription("Set the installer package welcome content.")
            .SetArgumentHelpName("PATH")
            .AcceptExistingOnly();

        AddOption<FileInfo>((v) => PackageReadme = v.ToFullNameOrNull(), "--pkgReadme")
            .SetDescription("Set the installer package readme content.")
            .SetArgumentHelpName("PATH")
            .AcceptExistingOnly();

        AddOption<FileInfo>((v) => PackageLicense = v.ToFullNameOrNull(), "--pkgLicense")
            .SetDescription("Set the installer package license content.")
            .SetArgumentHelpName("PATH")
            .AcceptExistingOnly();

        AddOption<FileInfo>((v) => PackageConclusion = v.ToFullNameOrNull(), "--pkgConclusion")
            .SetDescription("Set the installer package conclusion content.")
            .SetArgumentHelpName("PATH")
            .AcceptExistingOnly();

        AddOption<string>((v) => SigningAppIdentity = v, "--signAppIdentity")
            .SetDescription("The subject name of the cert to use for app code signing.")
            .SetArgumentHelpName("SUBJECT");

        AddOption<string>((v) => SigningInstallIdentity = v, "--signInstallIdentity")
            .SetDescription("The subject name of the cert to use for installation packages.")
            .SetArgumentHelpName("SUBJECT");

        AddOption<FileInfo>((v) => SigningEntitlements = v.ToFullNameOrNull(), "--signEntitlements")
            .SetDescription("Path to entitlements file for hardened runtime signing.")
            .SetArgumentHelpName("PATH")
            .AcceptExistingOnly()
            .RequiresExtension(".entitlements");

        AddOption<string>((v) => NotaryProfile = v, "--notaryProfile")
            .SetDescription("Name of profile containing Apple credentials stored with notarytool.")
            .SetArgumentHelpName("NAME");
    }
}