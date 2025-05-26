namespace Velopack.Packaging.Windows.Msi;

public class MsiTemplateData
{
    public string WixId;
    public string SourceDirectoryPath;
    public string RustNativeModulePath;
    public bool Is64Bit;
    public bool IsArm64;
    public int CultureLCID;
    public string UpgradeCodeGuid;
    public string ComponentGenerationSeedGuid;

    public string ProgramFilesFolderName => Is64Bit
        ? "[ProgramFiles64Folder]"
        : "[ProgramFilesFolder]";

    public string AppId;
    public string AppTitle;
    public string AppTitleSanitized => MsiUtil.SanitizeDirectoryString(AppTitle);
    public string AppPublisher;
    public string AppPublisherSanitized => MsiUtil.SanitizeDirectoryString(AppPublisher);
    public string AppMsiVersion;
    public string AppVersion;

    public string StubFileName;
    public bool DesktopShortcut;
    public bool StartMenuShortcut;
    public bool StartMenuRootShortcut;

    public string RuntimeDependencies;
    public bool HasRuntimeDependencies => !string.IsNullOrWhiteSpace(RuntimeDependencies);

    
    public bool InstallLocationEither => InstallForAllUsers && InstallForCurrentUser;
    public bool InstallLocationAllUsersOnly => InstallForAllUsers && !InstallForCurrentUser;
    public bool InstallLocationCurrentUserOnly => !InstallForAllUsers && InstallForCurrentUser;
    public bool InstallForAllUsers;
    public bool InstallForCurrentUser;

    public bool HasIcon => !string.IsNullOrWhiteSpace(IconPath) && File.Exists(IconPath);
    public string IconPath;

    public bool HasLicense => !string.IsNullOrWhiteSpace(LicenseRtfFilePath);
    public string LicenseRtfFilePath;

    public bool HasConclusionMessage => !string.IsNullOrWhiteSpace(ConclusionMessage);
    public string ConclusionMessage;

    public bool HasWelcomeMessage => !string.IsNullOrWhiteSpace(WelcomeMessage);
    public string WelcomeMessage;

    public bool HasReadmeMessage => !string.IsNullOrWhiteSpace(ReadmeMessage);
    public string ReadmeMessage;

    public bool HasTopBannerImage => !string.IsNullOrWhiteSpace(TopBannerImagePath) && File.Exists(TopBannerImagePath);
    public string TopBannerImagePath;

    public bool HasSideBannerImage => !string.IsNullOrWhiteSpace(SideBannerImagePath) && File.Exists(SideBannerImagePath);
    public string SideBannerImagePath;
}