namespace Velopack.Vpk.Commands;

public class OsxPackCommand : OsxBundleCommand
{
    public string InstWelcome { get; private set; }

    public string InstReadme { get; private set; }

    public string InstLicense { get; private set; }

    public string InstConclusion { get; private set; }

    public string SignAppIdentity { get; private set; }

    public string SignInstallIdentity { get; private set; }

    public string SignEntitlements { get; private set; }

    public string NotaryProfile { get; private set; }

    public OsxPackCommand()
        : base("pack", "Converts application files into a release and installer.")
    {
        AddOption<FileInfo>((v) => InstWelcome = v.ToFullNameOrNull(), "--instWelcome")
            .SetDescription("Set the installer package welcome content.")
            .SetArgumentHelpName("PATH")
            .MustExist();

        AddOption<FileInfo>((v) => InstReadme = v.ToFullNameOrNull(), "--instReadme")
            .SetDescription("Set the installer package readme content.")
            .SetArgumentHelpName("PATH")
            .MustExist();

        AddOption<FileInfo>((v) => InstLicense = v.ToFullNameOrNull(), "--instLicense")
            .SetDescription("Set the installer package license content.")
            .SetArgumentHelpName("PATH")
            .MustExist();

        AddOption<FileInfo>((v) => InstConclusion = v.ToFullNameOrNull(), "--instConclusion")
            .SetDescription("Set the installer package conclusion content.")
            .SetArgumentHelpName("PATH")
            .MustExist();

        AddOption<string>((v) => SignAppIdentity = v, "--signAppIdentity")
            .SetDescription("The subject name of the cert to use for app code signing.")
            .SetArgumentHelpName("SUBJECT");

        AddOption<string>((v) => SignInstallIdentity = v, "--signInstallIdentity")
            .SetDescription("The subject name of the cert to use for installation packages.")
            .SetArgumentHelpName("SUBJECT");

        AddOption<FileInfo>((v) => SignEntitlements = v.ToFullNameOrNull(), "--signEntitlements")
            .SetDescription("Path to entitlements file for hardened runtime signing.")
            .SetArgumentHelpName("PATH")
            .MustExist()
            .RequiresExtension(".entitlements");

        AddOption<string>((v) => NotaryProfile = v, "--notaryProfile")
            .SetDescription("Name of profile containing Apple credentials stored with notarytool.")
            .SetArgumentHelpName("NAME");
    }
}