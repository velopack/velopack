using Velopack.Packaging;

namespace Velopack.Vpk.Commands.Packaging;

public class WindowsPackCommand : PackCommand
{
    public string Runtimes { get; private set; }

    public string SplashImage { get; private set; }

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
    public bool BuildMsiDeploymentTool { get; private set; }

    public string MsiVersionOverride { get; private set; }

    public WindowsPackCommand()
        : base("pack", "Creates a release from a folder containing application files.", RuntimeOs.Windows)
    {
        EntryExecutableNameOption.RequiresExtension(".exe");
        IconOption.RequiresExtension(".ico");

        AddOption<string>((v) => Runtimes = v, "-f", "--framework")
            .SetDescription("List of required runtimes to install during setup. Example: 'net6-x64-desktop,vcredist143-x64'.")
            .SetArgumentHelpName("RUNTIMES");

        AddOption<FileInfo>((v) => SplashImage = v.ToFullNameOrNull(), "-s", "--splashImage")
            .SetDescription("Path to image displayed during installation.")
            .SetArgumentHelpName("PATH")
            .MustExist();

        AddOption<bool>((v) => SkipVelopackAppCheck = v, "--skipVeloAppCheck")
            .SetDescription("Skip the VelopackApp builder verification.")
            .SetHidden();

        var signTemplate = AddOption<string>((v) => SignTemplate = v, "--signTemplate")
          .SetDescription("Use a custom signing command. {{file}} will be substituted.")
          .SetArgumentHelpName("COMMAND");

        AddOption<string>((v) => SignExclude = v, "--signExclude")
            .SetDescription("A regex which excludes matched files from signing.")
            .SetHidden();

        AddOption<int>((v) => SignParallel = v, "--signParallel")
             .SetDescription("The number of files to sign in each signing command.")
             .SetArgumentHelpName("NUM")
             .MustBeBetween(1, 1000)
             .SetHidden()
             .SetDefault(10);

        AddOption<string>((v) => Shortcuts = v, "--shortcuts")
            .SetDescription("List of locations to install shortcuts to during setup.")
            .SetArgumentHelpName("LOC")
            .SetDefault("Desktop,StartMenuRoot");

        if (VelopackRuntimeInfo.IsWindows) {
            var signParams = AddOption<string>((v) => SignParameters = v, "--signParams", "-n")
                .SetDescription("Sign files via signtool.exe using these parameters.")
                .SetArgumentHelpName("PARAMS");

            var azTrustedSign = AddOption<FileInfo>((v) => AzureTrustedSignFile = v.ToFullNameOrNull(), "--azureTrustedSignFile")
                .SetDescription("Path to Azure Trusted Signing metadata.json.")
                .SetArgumentHelpName("PATH");

            this.AreMutuallyExclusive(signTemplate, signParams, azTrustedSign);

            AddOption<bool>((v) => BuildMsiDeploymentTool = v, "--msiDeploymentTool")
                .SetDescription("Compile a .msi machine-wide deployment tool.")
                .SetHidden();

            AddOption<bool>((v) => BuildMsi = v, "--msi")
                .SetDescription("Compile a .msi machine-wide bootstrap package.");
            
            AddOption<string>(v => MsiVersionOverride = v, "--msiVersion")
                .SetDescription("Override the product version for the generated msi.")
                .SetArgumentHelpName("VERSION")
                .MustBeValidMsiVersion();

            AddOption<FileInfo>(v => InstWelcome = v.ToFullNameOrNull(), "--instWelcome")
                .SetDescription("Set the installer package welcome content. Most formatting is not supported.")
                .RequiresExtension(".md")
                .SetArgumentHelpName("PATH");

            AddOption<FileInfo>(v => InstLicense = v.ToFullNameOrNull(), "--instLicense")
                .SetDescription("Set the installer package license content. This will be rendered as RTF content for the MSI. Formatting is done using https://github.com/uniederer/MarkdigExtensions.RtfRenderer.")
                .RequiresExtension(".md")
                .SetArgumentHelpName("PATH");

            AddOption<FileInfo>(v => InstLicenseRtf = v.ToFullNameOrNull(), "--instLicenseRtf")
                .SetDescription("Set the installer package license RTF content.")
                .RequiresExtension(".rtf")
                .SetArgumentHelpName("PATH")
                .SetHidden();

            AddOption<FileInfo>(v => InstReadme = v.ToFullNameOrNull(), "--instReadme")
                .SetDescription("Set the installer package readme content. Most formatting is not supported.")
                .RequiresExtension(".md")
                .SetArgumentHelpName("PATH");

            AddOption<FileInfo>(v => InstConclusion = v.ToFullNameOrNull(), "--instConclusion")
                .SetDescription("Set the installer package conclusion content. Most formatting is not supported.")
                .RequiresExtension(".md")
                .SetArgumentHelpName("PATH");

            AddOption<InstallLocation>(v => InstLocation = v, "--instLocation")
                .SetDefault(InstallLocation.Either)
                .SetDescription("Set the installation location.")
                .SetArgumentHelpName("LOCATION");

            AddOption<FileInfo>(v => MsiBanner = v.ToFullNameOrNull(), "--msiBanner")
                .SetDescription("Set the top banner bitmap image for the MSI UI dialogs. The resolution must be 493x58.")
                .RequiresExtension(".bmp")
                .SetArgumentHelpName("PATH");

            AddOption<FileInfo>(v => MsiLogo = v.ToFullNameOrNull(), "--msiLogo")
                .SetDescription("Set the background logo bitmap image for the MSI UI dialogs. The resolution must be 493x312.")
                .RequiresExtension(".bmp")
                .SetArgumentHelpName("PATH");
        }
    }
}