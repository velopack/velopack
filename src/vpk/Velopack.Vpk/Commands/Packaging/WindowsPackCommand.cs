using Velopack.Packaging;

namespace Velopack.Vpk.Commands.Packaging;

public class WindowsPackCommand : PackCommand
{
    public string Runtimes { get; private set; }

    public string SplashImage { get; private set; }

    public string SplashProgressColor { get; private set; }

    public bool SkipVelopackAppCheck { get; private set; }

    public string SignParameters { get; private set; }

    public string SignExclude { get; private set; }

    public int SignParallel { get; private set; }

    public string SignTemplate { get; private set; }

    public string AzureTrustedSignFile { get; private set; }

    public string Shortcuts { get; private set; }

    public string InstWelcome { get; private set; }

    public string InstLicense { get; private set; }
    public string InstLicenseRtf { get; private set; }

    public string InstReadme { get; private set; }

    public string InstConclusion { get; private set; }

    public InstallLocation InstLocation { get; private set; }

    public string MsiBanner { get; private set; }
    public string MsiLogo { get; private set; }


    public bool BuildMsi { get; private set; }

    public string MsiVersionOverride { get; private set; }

    public string Aumid { get; private set; }

    public WindowsPackCommand()
        : base("pack", "Creates a release from a folder containing application files.", RuntimeOs.Windows)
    {
        AddOption<string>((v) => Runtimes = v, ["-f", "--framework"])
            .SetDescription("List of required runtimes to install during setup. Example: 'net6-x64-desktop,vcredist143-x64'.")
            .SetArgumentHelpName("RUNTIMES");

        AddOption<FileInfo>((v) => SplashImage = v.ToFullNameOrNull(), ["-s", "--splashImage"])
            .SetDescription("Path to image displayed during installation.")
            .SetArgumentHelpName("PATH");

        AddOption<string>((v) => SplashProgressColor = v, ["--splashProgressColor"])
            .SetDescription("Progress bar color (e.g. #FF0000), or 'None' to hide.")
            .SetArgumentHelpName("COLOR");

        AddOption<bool>((v) => SkipVelopackAppCheck = v, ["--skipVeloAppCheck"])
            .SetDescription("Skip the VelopackApp builder verification.")
            .SetHidden();

        AddOption<string>((v) => SignTemplate = v, ["--signTemplate"])
            .SetDescription("Use a custom signing command. {{file}} will be substituted.")
            .SetArgumentHelpName("COMMAND");

        AddOption<string>((v) => SignExclude = v, ["--signExclude"])
            .SetDescription("A regex which excludes matched files from signing.")
            .SetHidden();

        AddOption<int>((v) => SignParallel = v, ["--signParallel"])
            .SetDescription("The number of files to sign in each signing command.")
            .SetArgumentHelpName("NUM")
            .SetHidden()
            .SetDefault(10);

        AddOption<string>((v) => Aumid = v, ["--aumid"])
            .SetDescription("Override the Application User Model ID (AUMID) for shortcuts.")
            .SetArgumentHelpName("AUMID")
            .SetHidden();

        AddOption<string>((v) => Shortcuts = v, ["--shortcuts"])
            .SetDescription("List of locations to install shortcuts to during setup.")
            .SetArgumentHelpName("LOC")
            .SetDefault("Desktop,StartMenuRoot");

        if (VelopackRuntimeInfo.IsWindows) {
            AddOption<string>((v) => SignParameters = v, ["--signParams", "-n"])
                .SetDescription("Sign files via signtool.exe using these parameters.")
                .SetArgumentHelpName("PARAMS");

            AddOption<FileInfo>((v) => AzureTrustedSignFile = v.ToFullNameOrNull(), ["--azureTrustedSignFile"])
                .SetDescription("Path to Azure Trusted Signing metadata.json.")
                .SetArgumentHelpName("PATH");

            AddOption<bool>((v) => BuildMsi = v, ["--msi"])
                .SetDescription("Compile a .msi machine-wide bootstrap package.");

            AddOption<string>(v => MsiVersionOverride = v, ["--msiVersion"])
                .SetDescription("Override the product version for the generated msi.")
                .SetArgumentHelpName("VERSION");

            AddOption<FileInfo>(v => InstWelcome = v.ToFullNameOrNull(), ["--instWelcome"])
                .SetDescription("Set the plain-text installer package welcome content.")
                .SetArgumentHelpName("PATH");

            AddOption<FileInfo>(v => InstLicense = v.ToFullNameOrNull(), ["--instLicense"])
                .SetDescription("Set the installer package license content. Can be either RTF or Markdown.")
                .SetArgumentHelpName("PATH");

            AddOption<FileInfo>(v => InstReadme = v.ToFullNameOrNull(), ["--instReadme"])
                .SetDescription("Set the installer package readme content. Can be RTF, Markdown, or plain text.")
                .SetArgumentHelpName("PATH");

            AddOption<FileInfo>(v => InstConclusion = v.ToFullNameOrNull(), ["--instConclusion"])
                .SetDescription("Set the plain-text installer package conclusion content.")
                .SetArgumentHelpName("PATH");

            AddOption<InstallLocation>(v => InstLocation = v, ["--instLocation"])
                .SetDefault(InstallLocation.Either)
                .SetDescription("Set the installation location.")
                .SetArgumentHelpName("LOCATION");

            AddOption<FileInfo>(v => MsiBanner = v.ToFullNameOrNull(), ["--msiBanner"])
                .SetDescription("Set the top banner bitmap image for the MSI UI dialogs. The resolution must be 493x58.")
                .SetArgumentHelpName("PATH");

            AddOption<FileInfo>(v => MsiLogo = v.ToFullNameOrNull(), ["--msiLogo"])
                .SetDescription("Set the background logo bitmap image for the MSI UI dialogs. The resolution must be 493x312.")
                .SetArgumentHelpName("PATH");
        }
    }
}