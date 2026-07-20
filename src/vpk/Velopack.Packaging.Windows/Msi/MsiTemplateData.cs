namespace Velopack.Packaging.Windows.Msi;

public class MsiTemplateData
{
    public string WixId;
    public string SourceDirectoryPath;
    public string RustNativeModulePath;
    public bool Is64Bit;
    public bool IsArm64;
    public string UpgradeCodeGuid;
    public string ComponentGenerationSeedGuid;

    public string ProgramFilesFolderName => Is64Bit
        ? "[ProgramFiles64Folder]"
        : "[ProgramFilesFolder]";

    public string AppId;
    public string AppTitle;
    public string AppTitleSanitized => MsiBuilder.SanitizeDirectoryString(AppTitle);
    public string AppTitleEscaped => MsiBuilder.EscapeMsiFormattedString(AppTitle);
    public string AppPublisher;
    public string AppPublisherSanitized => MsiBuilder.SanitizeDirectoryString(AppPublisher);
    public string AppPublisherEscaped => MsiBuilder.EscapeMsiFormattedString(AppPublisher);
    public string AppPublisherSanitizedEscaped => MsiBuilder.EscapeMsiFormattedString(AppPublisherSanitized);
    public string AppMsiVersion;
    public string AppVersion;

    public string StubFileName;
    public string StubFileNameEscaped => MsiBuilder.EscapeMsiFormattedString(StubFileName);
    public string MainExeFileName;
    public bool DesktopShortcut;
    public bool StartMenuShortcut;
    public bool StartMenuRootShortcut;
    public bool StartupShortcut;

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

    public bool HasReadme => !string.IsNullOrWhiteSpace(ReadmeRtfFilePath);
    public string ReadmeRtfFilePath;

    /// <summary>
    /// The narrow strip across the top of most MSI dialogs (WiX binary WixUI_Bmp_Banner, 493x58).
    /// Set from --msiTopBanner; a default is substituted at compile time if unset or missing.
    /// </summary>
    public string BannerBmpPath;
    public bool HasBannerBmp => !string.IsNullOrWhiteSpace(BannerBmpPath) && File.Exists(BannerBmpPath);

    /// <summary>
    /// The full background of the MSI welcome/completion dialogs (WiX binary WixUI_Bmp_Dialog, 493x312).
    /// Set from --msiDialogBackground; a default is substituted at compile time if unset or missing.
    /// </summary>
    public string DialogBmpPath;
    public bool HasDialogBmp => !string.IsNullOrWhiteSpace(DialogBmpPath) && File.Exists(DialogBmpPath);
    public string ExclamIcoPath;
    public string UpIcoPath;
    public string NewIcoPath;
}