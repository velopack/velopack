using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack.Compression;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.NuGet;
using Velopack.Util;
using Velopack.Windows;

namespace Velopack.Packaging.Windows.Commands;

public class WindowsPackCommandRunner : PackageBuilder<WindowsPackOptions>
{
    public WindowsPackCommandRunner(ILogger logger, IFancyConsole console)
        : base(RuntimeOs.Windows, logger, console)
    {
    }

    protected override Task CodeSign(Action<int> progress, string packDir)
    {
        Regex fileExcludeRegex = Options.SignExclude != null ? new Regex(Options.SignExclude) : null;
        var filesToSign = new DirectoryInfo(packDir).GetAllFilesRecursively()
            .Where(x => PathUtil.FileIsLikelyPEImage(x.Name))
            .Where(x => fileExcludeRegex == null || !fileExcludeRegex.IsMatch(x.FullName))
            .Select(x => x.FullName)
            .ToArray();

        SignFilesImpl(progress, filesToSign);

        return Task.CompletedTask;
    }

    protected override Task<string> PreprocessPackDir(Action<int> progress, string packDir)
    {
        if (!Options.SkipVelopackAppCheck) {
            var compat = new CompatUtil(Log, Console);
            compat.Verify(MainExePath);
        } else {
            Log.Info("Skipping VelopackApp.Build.Run() check.");
        }

        // add nuspec metadata
        ExtraNuspecMetadata["runtimeDependencies"] = GetRuntimeDependencies();
        ExtraNuspecMetadata["shortcutLocations"] = GetShortcutLocations();
        ExtraNuspecMetadata["shortcutAmuid"] = CoreUtil.GetAppUserModelId(Options.PackId);

        // copy files to temp dir, so we can modify them
        var dir = TempDir.CreateSubdirectory("PreprocessPackDirWin");
        CopyFiles(new DirectoryInfo(packDir), dir, progress, true);
        File.WriteAllText(Path.Combine(dir.FullName, CoreUtil.SpecVersionFileName), GenerateNuspecContent());
        packDir = dir.FullName;

        var updatePath = Path.Combine(TempDir.FullName, "Update.exe");
        File.Copy(HelperFile.GetUpdatePath(Options.TargetRuntime, Log), updatePath, true);

        // check for and delete clickonce manifest
        var clickonceManifests = Directory.EnumerateFiles(packDir, "*.application")
            .Where(f => File.ReadAllText(f).Contains("clickonce"))
            .ToArray();
        if (clickonceManifests.Any()) {
            foreach (var manifest in clickonceManifests) {
                Log.Warn(
                    $"ClickOnce manifest found in pack directory: '{Path.GetFileName(manifest)}'. " +
                    $"Velopack does not support building ClickOnce applications, and so will delete this file automatically. " +
                    $"It is recommended that you remove ClickOnce from your .csproj to avoid this warning.");
                File.Delete(manifest);
            }
        }

        // update icon for Update.exe if requested
        if (Options.Icon != null) {
            var editor = new ResourceEdit(updatePath, Log);
            editor.SetExeIcon(Options.Icon);
            editor.Commit();
        }

        File.Copy(updatePath, Path.Combine(packDir, "Squirrel.exe"), true);

        // create a stub for portable packages
        var mainExeName = Options.EntryExecutableName;
        var mainPath = Path.Combine(packDir, mainExeName);
        var stubPath = Path.Combine(packDir, Path.GetFileNameWithoutExtension(mainExeName) + "_ExecutionStub.exe");
        CreateExecutableStubForExe(mainPath, stubPath);

        return Task.FromResult(packDir);
    }

    protected string GetShortcutLocations()
    {
        if (String.IsNullOrWhiteSpace(Options.Shortcuts))
            return null;

        try {
            var shortcuts = GetShortcuts();

            if (shortcuts.Count == 0)
                return null;

            var shortcutString = string.Join(",", shortcuts.Select(x => x.ToString()));
            Log.Debug($"Shortcut Locations: {shortcutString}");
            return shortcutString;
        } catch (Exception ex) {
            throw new UserInfoException(
                $"Invalid shortcut locations '{Options.Shortcuts}'. " +
                $"Valid values for comma delimited list are: {string.Join(", ", Enum.GetNames(typeof(ShortcutLocation)))}." +
                $"Error was {ex.Message}");
        }
    }

    protected string GetRuntimeDependencies()
    {
        if (string.IsNullOrWhiteSpace(Options.Runtimes))
            return "";

        var providedRuntimes = Options.Runtimes.ToLower()
            .Split([',', ';'], StringSplitOptions.RemoveEmptyEntries);

        var valid = new string[] {
            "webview2",
            "vcredist100-x86",
            "vcredist100-x64",
            "vcredist110-x86",
            "vcredist110-x64",
            "vcredist120-x86",
            "vcredist120-x64",
            "vcredist140-x86",
            "vcredist140-x64",
            "vcredist141-x86",
            "vcredist141-x64",
            "vcredist142-x86",
            "vcredist142-x64",
            "vcredist143-x86",
            "vcredist143-x64",
            "vcredist143-arm64",
            "vcredist144-x86",
            "vcredist144-x64",
            "vcredist144-arm64",
            "net45",
            "net451",
            "net452",
            "net46",
            "net461",
            "net462",
            "net47",
            "net471",
            "net472",
            "net48",
            "net481",
        };

        List<string> validated = [];

        foreach (var str in providedRuntimes) {
            if (valid.Contains(str)) {
                validated.Add(str);
                continue;
            }

#pragma warning disable CS0618 // Type or member is obsolete
            if (Runtimes.DotnetInfo.TryParse(str, out var dotnetInfo)) {
                if (dotnetInfo.MinVersion.Major < 5)
                    throw new UserInfoException($"The framework/runtime dependency '{str}' is not valid. Only .NET 5+ is supported.");
                validated.Add(dotnetInfo.Id);
                continue;
            }
#pragma warning restore CS0618 // Type or member is obsolete

            throw new UserInfoException(
                $"The framework/runtime dependency '{str}' is not valid. See https://docs.velopack.io/packaging/bootstrapping");
        }

        foreach (var str in validated) {
            Log.Info("Runtime Dependency: " + str);
        }

        return String.Join(",", validated);
    }

    protected override Task CreateSetupPackage(Action<int> progress, string releasePkg, string packDir, string targetSetupExe, Func<string, VelopackAssetType, string> createAsset)
    {
        void setupExeProgress(int x)
        {
            if (Options.BuildMsi) {
                progress(x / 2);
            } else {
                progress(x);
            }
        }
        void msiProgress(int value)
        {
            progress(50 + value / 2);
        }

        var bundledZip = new ZipPackage(releasePkg);
        IoUtil.Retry(() => File.Copy(HelperFile.SetupPath, targetSetupExe, true));
        setupExeProgress(10);

        var editor = new ResourceEdit(targetSetupExe, Log);
        editor.SetVersionInfo(bundledZip);
        if (Options.Icon != null) {
            editor.SetExeIcon(Options.Icon);
        }

        editor.Commit();

        setupExeProgress(25);
        Log.Debug($"Creating Setup bundle");
        SetupBundle.CreatePackageBundle(targetSetupExe, releasePkg);
        setupExeProgress(50);
        Log.Debug("Signing Setup bundle");
        SignFilesImpl(CoreUtil.CreateProgressDelegate( setupExeProgress, 50, 100), targetSetupExe);
        Log.Debug($"Setup bundle created '{Path.GetFileName(targetSetupExe)}'.");
        setupExeProgress(100);

        if (Options.BuildMsiDeploymentTool && VelopackRuntimeInfo.IsWindows) {
            var msiName = DefaultName.GetSuggestedMsiDeploymentToolName(Options.PackId, Options.Channel, TargetOs);
            var msiPath = createAsset(msiName, VelopackAssetType.MsiDeploymentTool);
            CompileWixTemplateToMsiDeploymentTool(msiProgress, targetSetupExe, msiPath);
        }

        if (Options.BuildMsi && VelopackRuntimeInfo.IsWindows) {
            var msiName = DefaultName.GetSuggestedMsiName(Options.PackId, Options.Channel, TargetOs);
            var msiPath = createAsset(msiName, VelopackAssetType.Msi);
            var portablePackage = new DirectoryInfo(Path.Combine(TempDir.FullName, "CreatePortablePackage"));
            if (portablePackage.Exists) {
                CompileWixTemplateToMsi(msiProgress, portablePackage, msiPath);
            } else {
                Log.Warn("Portable package not found, skipping MSI creation.");
            }
        }

        return Task.CompletedTask;
    }

    protected override async Task CreatePortablePackage(Action<int> progress, string packDir, string outputPath)
    {
        var dir = TempDir.CreateSubdirectory("CreatePortablePackage");
        File.Copy(Path.Combine(packDir, "Squirrel.exe"), Path.Combine(dir.FullName, "Update.exe"), true);
        var current = dir.CreateSubdirectory("current");

        CopyFiles(new DirectoryInfo(packDir), current, CoreUtil.CreateProgressDelegate(progress, 0, 30));

        File.Delete(Path.Combine(current.FullName, "Squirrel.exe"));

        // move the stub to the root of the portable package
        var stubPath = Path.Combine(
            current.FullName,
            Path.GetFileNameWithoutExtension(Options.EntryExecutableName) + "_ExecutionStub.exe");
        File.Move(stubPath, Path.Combine(dir.FullName, GetPortableStubFileName()));

        // create a .portable file to indicate this is a portable package
        File.Create(Path.Combine(dir.FullName, ".portable")).Close();

        await EasyZip.CreateZipFromDirectoryAsync(Log.ToVelopackLogger(), outputPath, dir.FullName, CoreUtil.CreateProgressDelegate(progress, 40, 100));
        progress(100);
    }

    protected override Dictionary<string, string> GetReleaseMetadataFiles()
    {
        var dict = new Dictionary<string, string>();
        if (Options.Icon != null) dict["setup.ico"] = Options.Icon;
        if (Options.SplashImage != null) dict["splashimage" + Path.GetExtension(Options.SplashImage)] = Options.SplashImage;
        return dict;
    }

    private void CreateExecutableStubForExe(string exeToCopy, string targetStubPath)
    {
        if (!File.Exists(exeToCopy)) {
            throw new ArgumentException($"Cannot create StubExecutable for '{exeToCopy}' because it does not exist.");
        }

        try {
            IoUtil.Retry(() => File.Copy(HelperFile.StubExecutablePath, targetStubPath, true));
            var edit = new ResourceEdit(targetStubPath, Log);
            edit.CopyResourcesFrom(exeToCopy);
            edit.Commit();
        } catch (Exception ex) {
            Log.Error(ex, $"Error creating StubExecutable and copying resources for '{exeToCopy}'. This stub may or may not work properly.");
        }
    }

    private void SignFilesImpl(Action<int> progress, params string[] filePaths)
    {
        var signParams = Options.SignParameters;
        var signTemplate = Options.SignTemplate;
        var signParallel = Options.SignParallel;
        var trustedSignMetadataPath = Options.AzureTrustedSignFile;
        var helper = new CodeSign(Log);

        if (string.IsNullOrEmpty(signParams) && string.IsNullOrEmpty(signTemplate) && string.IsNullOrEmpty(trustedSignMetadataPath)) {
            Log.Warn($"No signing parameters provided, {filePaths.Length} file(s) will not be signed.");
            return;
        }

        if (!string.IsNullOrEmpty(signTemplate)) {
            helper.Sign(filePaths, signTemplate, signParallel, progress, true);
        }

        // signtool.exe does not work if we're not on windows.
        if (!VelopackRuntimeInfo.IsWindows) return;

        if (!string.IsNullOrEmpty(trustedSignMetadataPath)) {
            Log.Info($"Use Azure Trusted Signing service for code signing. Metadata file path: {trustedSignMetadataPath}");

            string dlibPath = GetDlibPath(CancellationToken.None);
            signParams =
                $"/fd SHA256 /tr http://timestamp.acs.microsoft.com /v /debug /td SHA256 /dlib {HelperFile.AzureDlibFileName} /dmdf \"{trustedSignMetadataPath}\"";
            helper.Sign(filePaths, signParams, signParallel, progress, false);
        } else if (!string.IsNullOrEmpty(signParams)) {
            helper.Sign(filePaths, signParams, signParallel, progress, false);
        }
    }

    [SupportedOSPlatform("windows")]
    private string GetDlibPath(CancellationToken cancellationToken)
    {
        // DLib library is required for Azure Trusted Signing. It must be in the same directory as SignTool.exe.
        // https://learn.microsoft.com/azure/trusted-signing/how-to-signing-integrations#download-and-install-the-trusted-signing-dlib-package
        var signToolPath = HelperFile.SignToolPath;
        var signToolDirectory = Path.GetDirectoryName(signToolPath);
        var dlibPath = Path.Combine(signToolDirectory, HelperFile.AzureDlibFileName);
        if (File.Exists(dlibPath)) {
            return dlibPath;
        }

        throw new NotSupportedException("Azure Trusted Signing is not supported in this version of Velopack.");

        // Log.Info($"Downloading Azure Trusted Signing dlib to '{dlibPath}'");
        // var dl = new NuGetDownloader();
        //
        // using MemoryStream nupkgStream = new();
        // await dl.DownloadPackageToStream("Microsoft.Trusted.Signing.Client", "1.*", nupkgStream, cancellationToken);
        //
        // nupkgStream.Position = 0;
        //
        // string parentDir = NugetUtil.BinDirectory + Path.AltDirectorySeparatorChar + "x64" + Path.AltDirectorySeparatorChar;
        //
        // ZipArchive zipPackage = new(nupkgStream);
        // var entries = zipPackage.Entries.Where(x => x.FullName.StartsWith(parentDir, StringComparison.OrdinalIgnoreCase));
        // foreach (var entry in entries) {
        //     var relativePath = entry.FullName.Substring(parentDir.Length);
        //     entry.ExtractToFile(Path.Combine(signToolDirectory, relativePath), true);
        // }
        // return dlibPath;
    }

    [SupportedOSPlatform("windows")]
    private void CompileWixTemplateToMsi(Action<int> progress,
        DirectoryInfo portableDirectory, string msiFilePath)
    {
        bool packageAs64Bit =
            Options.TargetRuntime.Architecture is not RuntimeCpu.x86;

        Log.Info($"Compiling msi installer in {(packageAs64Bit ? "64-bit" : "32-bit")} mode");

        var outputDirectory = portableDirectory.Parent.CreateSubdirectory("msi");
        var culture = CultureInfo.GetCultureInfo("en-US").TextInfo.ANSICodePage;

        // WiX Identifiers may contain ASCII characters A-Z, a-z, digits, underscores (_), or
        // periods(.). Every identifier must begin with either a letter or an underscore.
        var wixId = Regex.Replace(Options.PackId, @"[^\w\.]", "_");
        if (char.GetUnicodeCategory(wixId[0]) == UnicodeCategory.DecimalDigitNumber)
            wixId = "_" + wixId;

        var msiVersion = Options.MsiVersionOverride;
        if (string.IsNullOrWhiteSpace(msiVersion)) {
            var parsedVersion = SemanticVersion.Parse(Options.PackVersion);
            msiVersion = $"{parsedVersion.Major}.{parsedVersion.Minor}.{parsedVersion.Patch}.0";
        }

        static string SanitizeDirectoryString(string name)
            => string.Join("_", name.Split(Path.GetInvalidPathChars()));

        static string FormatXmlMessage(string message)
            => string.IsNullOrWhiteSpace(message) ? "" : message.Replace("\r", "&#10;").Replace("\n", "&#13;");

        static string GetFileContent(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath))
                return "";
            string fileContents = File.ReadAllText(filePath, Encoding.UTF8);
            return FormatXmlMessage(fileContents);
        }

        List<string> wixExtensions = ["WixToolset.UI.wixext"];

        var shortcuts = GetShortcuts().ToHashSet();
        string title = GetEffectiveTitle();
        string authors = GetEffectiveAuthors();
        string stub = GetPortableStubFileName();
        string conclusionMessage = GetFileContent(Options.InstConclusion);
        string license = Options.InstLicense;
        bool hasLicense = !string.IsNullOrWhiteSpace(license);
        string bannerImage = string.IsNullOrWhiteSpace(Options.MsiBanner) ? HelperFile.WixAssetsTopBanner : Options.MsiBanner;
        string dialogImage = string.IsNullOrWhiteSpace(Options.MsiLogo) ? HelperFile.WixAssetsDialogBackground : Options.MsiLogo;

        //Scope can be perMachine or perUser or perUserOrMachine, https://docs.firegiant.com/wix/schema/wxs/packagescopetype/
        //For now just hard coding to perMachine
        string wixPackage = $"""
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs" xmlns:ui="http://wixtoolset.org/schemas/v4/wxs/ui">
              <Package Name="{title}"
                       Manufacturer="{authors}"
                       Version="{msiVersion}"
                       Codepage="{culture}"
                       Language="1033"
                       Scope="perMachine"
                       UpgradeCode="{GuidUtil.CreateGuidFromHash($"{Options.PackId}:UpgradeCode")}"
                       >
                <Media Id="1" Cabinet="app.cab" EmbedCab="yes" />
                <StandardDirectory Id="{(packageAs64Bit ? "ProgramFiles64Folder" : "ProgramFiles6432Folder")}">
                  <Directory Id="INSTALLFOLDER" Name="{SanitizeDirectoryString(authors)}">
                    <Directory Name="current" />
                    <Directory Id="PACKAGES_DIR" Name="packages" />
                  </Directory>
                </StandardDirectory>
                {(shortcuts.Contains(ShortcutLocation.Desktop) ? $"""
                <StandardDirectory Id="DesktopFolder">
                  <Component Id="ApplicationDesktopShortcut">
                    <Shortcut Id="ApplicationDesktopShortcut"
                              Name="{title}"
                              Description="Desktop shortcut for {title}"
                              Target="[INSTALLFOLDER]{stub}"
                              WorkingDirectory="INSTALLFOLDER"/>
                    <RemoveFolder Id="CleanUpDesktopShortcut" Directory="INSTALLFOLDER" On="uninstall"/>
                    <RegistryValue Root="HKCU" Key="Software\{SanitizeDirectoryString(authors)}\{Options.PackId}.DesktopShortcut" Name="installed" Type="integer" Value="1" KeyPath="yes"/>
                  </Component>
                </StandardDirectory>
                """ : "")}
                {(shortcuts.Contains(ShortcutLocation.StartMenu) ? $"""
                <StandardDirectory Id="StartMenuFolder">
                  <Component Id="ApplicationStartMenuShortcut">
                    <Shortcut Id="ApplicationStartMenuShortcut"
                              Name="{title}"
                              Description="Start Menu shortcut for {title}"
                              Target="[INSTALLFOLDER]{stub}"
                              WorkingDirectory="INSTALLFOLDER"/>
                    <RemoveFolder Id="CleanUpStartMenuShortcut" Directory="INSTALLFOLDER" On="uninstall"/>
                    <RegistryValue Root="HKCU" Key="Software\{SanitizeDirectoryString(authors)}\{Options.PackId}.StartMenuShortcut" Name="installed" Type="integer" Value="1" KeyPath="yes"/>
                  </Component>
                </StandardDirectory>
                """ : "")}

                {(!string.IsNullOrWhiteSpace(Options.Icon) ? $"""
                <Icon Id="appicon" SourceFile="{Options.Icon}"/>
                <Property Id="ARPPRODUCTICON" Value="appicon" />
                """ : "")}

                {(hasLicense ? $"""
                <WixVariable
                  Id="WixUILicenseRtf"
                  Value="{license}"
                  />
                """ : "")}

                <WixVariable
                  Id="WixUIBannerBmp"
                  Value="{bannerImage}"
                  />

                <WixVariable
                  Id="WixUIDialogBmp"
                  Value="{dialogImage}"
                  />

                <!-- Message on last screen after install -->
                {(!string.IsNullOrWhiteSpace(conclusionMessage) ? $"""
                <Property Id="WIXUI_EXITDIALOGOPTIONALTEXT" Value="{conclusionMessage}" />
                """: "")}

                <!-- Default checked state of launch app check box to true -->
                <Property Id="WIXUI_EXITDIALOGOPTIONALCHECKBOX" Value="1" />
            

                <!-- Check box for launching -->
                <Property
                  Id="WIXUI_EXITDIALOGOPTIONALCHECKBOXTEXT"
                  Value="Launch {title}"
                  />

                <UI>
                  <ui:WixUI
                      Id="WixUI_InstallDir"
                      InstallDirectory="INSTALLFOLDER"
                      />

                  <Publish Dialog="ExitDialog"
                      Control="Finish"
                      Event="DoAction"
                      Value="LaunchApplication"
                      Condition="WIXUI_EXITDIALOGOPTIONALCHECKBOX = 1 and NOT Installed" />
                </UI>

                <Files Include="{portableDirectory.FullName}\**" />

                <CustomAction Id="RemoveTempDirectory" Directory="TempFolder" Impersonate="yes" ExeCommand="cmd.exe /C rmdir /S /Q &quot;%TEMP%\velopack_{Options.PackId}&quot;" Execute="deferred" Return="ignore" />
                <CustomAction Id="RemoveAppDirectory" Directory="ProgramFiles64Folder" Impersonate="no" ExeCommand="cmd.exe /C for /d %D in (&quot;[INSTALLFOLDER]*&quot;) do @if /i not &quot;%~nxD&quot;==&quot;current&quot; rmdir /s /q &quot;%D&quot; &amp; for %F in (&quot;[INSTALLFOLDER]*&quot;) do @del /q &quot;%F&quot;" Execute="deferred" Return="ignore" />
                <CustomAction Id="LaunchApplication" Directory="INSTALLFOLDER" Impersonate="yes" ExeCommand="&quot;[INSTALLFOLDER]{stub}&quot;" Execute="immediate" Return="ignore" />

                <InstallExecuteSequence>
                  <Custom Action="RemoveAppDirectory" Before="RemoveFolders" Condition="(REMOVE=&quot;ALL&quot;) AND (NOT UPGRADINGPRODUCTCODE)" />
                  <Custom Action="RemoveTempDirectory" Before="InstallFinalize" Condition="(REMOVE=&quot;ALL&quot;) AND (NOT UPGRADINGPRODUCTCODE)" />
                </InstallExecuteSequence>
              </Package>

              <!-- Based on: https://github.com/wixtoolset/wix/blob/v5.0.2/src/ext/UI/wixlib/WixUI_InstallDir.wxs -->
              <?foreach WIXUIARCH in X86;X64;A64 ?>
                <Fragment>
                  <UI Id="WixUI_Velopack_$(WIXUIARCH)">
                    {(hasLicense ? $"""
                    <Publish Dialog="LicenseAgreementDlg" Control="Print" Event="DoAction" Value="WixUIPrintEul$(WIXUIARCH)" />
                    """ : "")}

                    <Publish Dialog="BrowseDlg" Control="OK" Event="DoAction" Value="WixUIValidateP$(WIXUIARCH)" Order="3" Condition="NOT WIXUI_DONTVALIDATEPATH" />
                    <Publish Dialog="InstallDirDlg" Control="Next" Event="DoAction" Value="WixUIValidatePa$(WIXUIARCH)" Order="2" Condition="NOT WIXUI_DONTVALIDATEPATH" />
                  </UI>

                  <UIRef Id="WixUI_Velopack" />
                </Fragment>
              <?endforeach?>

              <Fragment>
                <UI Id="file WixUI_Velopack">
                  <TextStyle Id="WixUI_Font_Normal" FaceName="Tahoma" Size="8" />
                  <TextStyle Id="WixUI_Font_Bigger" FaceName="Tahoma" Size="12" />
                  <TextStyle Id="WixUI_Font_Title" FaceName="Tahoma" Size="9" Bold="yes" />
                  
                  <Property Id="DefaultUIFont" Value="WixUI_Font_Normal" />
                  
                  <DialogRef Id="BrowseDlg" />
                  <DialogRef Id="DiskCostDlg" />
                  <DialogRef Id="ErrorDlg" />
                  <DialogRef Id="FatalError" />
                  <DialogRef Id="FilesInUse" />
                  <DialogRef Id="MsiRMFilesInUse" />
                  <DialogRef Id="PrepareDlg" />
                  <DialogRef Id="ProgressDlg" />
                  <DialogRef Id="ResumeDlg" />
                  <DialogRef Id="UserExit" />
                  <Publish Dialog="BrowseDlg" Control="OK" Event="SpawnDialog" Value="InvalidDirDlg" Order="4" Condition="NOT WIXUI_DONTVALIDATEPATH AND WIXUI_INSTALLDIR_VALID&lt;&gt;&quot;1&quot;" />
                  
                  <Publish Dialog="ExitDialog" Control="Finish" Event="EndDialog" Value="Return" Order="999" />
                  {(hasLicense ? """
                  <Publish Dialog="WelcomeDlg" Control="Next" Event="NewDialog" Value="LicenseAgreementDlg" Condition="NOT Installed" />
                  """ :"""
                  <Publish Dialog="WelcomeDlg" Control="Next" Event="NewDialog" Value="InstallDirDlg" Condition="NOT Installed" />
                  """)}
                  
                  <Publish Dialog="WelcomeDlg" Control="Next" Event="NewDialog" Value="VerifyReadyDlg" Condition="Installed AND PATCH" />
                  
                  {(hasLicense ? $"""
                  <Publish Dialog="LicenseAgreementDlg" Control="Back" Event="NewDialog" Value="WelcomeDlg" />
                  <Publish Dialog="LicenseAgreementDlg" Control="Next" Event="NewDialog" Value="InstallDirDlg" Condition="LicenseAccepted = &quot;1&quot;" /> 
                  """ : "")}
                  
                  {(hasLicense ? $"""
                  <Publish Dialog="InstallDirDlg" Control="Back" Event="NewDialog" Value="LicenseAgreementDlg" />
                  """ : """
                  <Publish Dialog="InstallDirDlg" Control="Back" Event="NewDialog" Value="WelcomeDlg" />
                  """)}
                  
                  <Publish Dialog="InstallDirDlg" Control="Next" Event="SetTargetPath" Value="[WIXUI_INSTALLDIR]" Order="1" />
                  <Publish Dialog="InstallDirDlg" Control="Next" Event="SpawnDialog" Value="InvalidDirDlg" Order="3" Condition="NOT WIXUI_DONTVALIDATEPATH AND WIXUI_INSTALLDIR_VALID&lt;&gt;&quot;1&quot;" />
                  <Publish Dialog="InstallDirDlg" Control="Next" Event="NewDialog" Value="VerifyReadyDlg" Order="4" Condition="WIXUI_DONTVALIDAEPATH OR WIXUI_INSTALLDIR_VALID=&quot;1&quot;" />
                  <Publish Dialog="InstallDirDlg" Control="ChangeFolder" Property="_BrowseProperty" Value="[WIXUI_INSTALLDIR]" Order="1" />
                  <Publish Dialog="InstallDirDlg" Control="ChangeFolder" Event="SpawnDialog" Value="BrowseDlg" Order="2" />
                  <Publish Dialog="VerifyReadyDlg" Control="Back" Event="NewDialog" Value="InstallDirDlg" Order="1" Condition="NOT Installed" />
                  <Publish Dialog="VerifyReadyDlg" Control="Back" Event="NewDialog" Value="MaintenanceTypeDlg" Order="2" Condition="Installed AND NOT PATCH" />
                  <Publish Dialog="VerifyReadyDlg" Control="Back" Event="NewDialog" Value="WelcomeDlg" Order="2" Condition="Installed AND PATCH" />
                  
                  <Publish Dialog="MaintenanceWelcomeDlg" Control="Next" Event="NewDialog" Value="MaintenanceTypeDlg" />
                  
                  <Publish Dialog="MaintenanceTypeDlg" Control="RepairButton" Event="NewDialog" Value="VerifyReadyDlg" />
                  <Publish Dialog="MaintenanceTypeDlg" Control="RemoveButton" Event="NewDialog" Value="VerifyReadyDlg" />
                  <Publish Dialog="MaintenanceTypeDlg" Control="Back" Event="NewDialog" Value="MaintenanceWelcomeDlg" />
                  
                  <Property Id="ARPNOMODIFY" Value="1" />
                </UI>

                <UIRef Id="WixUI_Common" />
              </Fragment>
            </Wix>
            """;

        string welcomeMessage = GetFileContent(Options.InstWelcome);
        string readmeMessage = GetFileContent(Options.InstReadme);

        string localizedStrings = $"""
            <WixLocalization Culture="en-US" Codepage="1252" xmlns="http://wixtoolset.org/schemas/v4/wxl">
              {(!string.IsNullOrWhiteSpace(welcomeMessage) ? $"""
              <!-- Message on first welcome dialog; covers both initial install and update -->
              <String
                Id="WelcomeDlgDescription"
                Value="{welcomeMessage}"
                />
              <String
                Id="WelcomeUpdateDlgDescriptionUpdate"
                Value="{welcomeMessage}"
                />
              """ : "")}

              {(!string.IsNullOrWhiteSpace(readmeMessage) ? $"""
              <!-- Message on the completion dialog (last screen after install) -->
              <String
                Id="VerifyReadyDlgInstallText"
                Value="{readmeMessage}"
                />
              """ : "")}
            </WixLocalization>
            """;

        var wxs = Path.Combine(outputDirectory.FullName, wixId + ".wxs");
        var localization = Path.Combine(outputDirectory.FullName, wixId + "_en-US.wxs");
        try {
            File.WriteAllText(wxs, wixPackage, Encoding.UTF8);
            File.WriteAllText(localization, localizedStrings, Encoding.UTF8);

            progress(30);

            Log.Info("Compiling WiX Template (dotnet build)");

            foreach(var extension in wixExtensions) {
                //TODO: Should extensions be versioned independently?
                AddWixExtension(extension, HelperFile.WixVersion);
            }

            //When localization is supported in Velopack, we will need to add -culture here:
            //https://docs.firegiant.com/wix/tools/wixext/wixui/
            var buildCommand = $"{HelperFile.WixPath} build -platform {(packageAs64Bit ? "x64" : "x86")} -outputType Package -pdbType none {string.Join(" ", wixExtensions.Select(x => $"-ext {x}"))} -loc \"{localization}\" -out \"{msiFilePath}\" \"{wxs}\"";

            _ = Exe.RunHostedCommand(buildCommand);

            progress(90);

        } finally {
            IoUtil.DeleteFileOrDirectoryHard(wxs, throwOnFailure: false);
        }
        progress(100);


        static void AddWixExtension(string extension, string version)
        {
            var addCommand = $"{HelperFile.WixPath} extension add -g {extension}/{version}";
            _ = Exe.RunHostedCommand(addCommand);
        }
    }

    [SupportedOSPlatform("windows")]
    private void CompileWixTemplateToMsiDeploymentTool(Action<int> progress,
        string setupExePath, string msiFilePath)
    {
        bool packageAs64Bit = 
            Options.TargetRuntime.Architecture is RuntimeCpu.x64 or RuntimeCpu.arm64;

        Log.Info($"Compiling machine-wide msi deployment tool in {(packageAs64Bit ? "64-bit" : "32-bit")} mode");

        var outputDirectory = Path.GetDirectoryName(setupExePath);
        var setupName = Path.GetFileNameWithoutExtension(setupExePath);
        var culture = CultureInfo.GetCultureInfo("en-US").TextInfo.ANSICodePage;

        // WiX Identifiers may contain ASCII characters A-Z, a-z, digits, underscores (_), or
        // periods(.). Every identifier must begin with either a letter or an underscore.
        var wixId = Regex.Replace(Options.PackId, @"[^\w\.]", "_");
        if (char.GetUnicodeCategory(wixId[0]) == UnicodeCategory.DecimalDigitNumber)
            wixId = "_" + wixId;

        Regex stacheRegex = new(@"\{\{(?<key>[^\}]+)\}\}", RegexOptions.Compiled);

        var wxsFile = Path.Combine(outputDirectory, wixId + ".wxs");
        var objFile = Path.Combine(outputDirectory, wixId + ".wixobj");


        var msiVersion = Options.MsiVersionOverride;
        if (string.IsNullOrWhiteSpace(msiVersion)) {
            var parsedVersion = SemanticVersion.Parse(Options.PackVersion);
            msiVersion = $"{parsedVersion.Major}.{parsedVersion.Minor}.{parsedVersion.Patch}.0";
        }

        try {
            // apply dictionary to wsx template
            var templateText = File.ReadAllText(HelperFile.WixTemplatePath);

            var templateResult = stacheRegex.Replace(templateText, match => {
                string key = match.Groups["key"].Value;
                return key switch {
                    "Id" => wixId,
                    "Title" => GetEffectiveTitle(),
                    "Author" => GetEffectiveAuthors(),
                    "Version" => msiVersion,
                    "Summary" => GetEffectiveTitle(),
                    "Codepage" => $"{culture}",
                    "Platform" => packageAs64Bit ? "x64" : "x86",
                    "ProgramFilesFolder" => packageAs64Bit ? "ProgramFiles64Folder" : "ProgramFilesFolder",
                    "Win64YesNo" => packageAs64Bit ? "yes" : "no",
                    "SetupName" => setupName,
                    _ when key.StartsWith("IdAsGuid", StringComparison.OrdinalIgnoreCase) => GuidUtil.CreateGuidFromHash($"{Options.PackId}:{key.Substring("IdAsGuid".Length)}").ToString(),
                    _ => match.Value,
                };
            });

            File.WriteAllText(wxsFile, templateResult, Encoding.UTF8);

            // Candle preprocesses and compiles WiX source files into object files (.wixobj).
            Log.Info("Compiling WiX Template (candle.exe)");
            var candleCommand = $"{HelperFile.WixCandlePath} -nologo -out \"{objFile}\" \"{wxsFile}\"";
            _ = Exe.RunHostedCommand(candleCommand);

            progress(45);

            // Light links and binds one or more .wixobj files and creates a Windows Installer database (.msi or .msm). 
            Log.Info("Linking WiX Template (light.exe)");
            var lightCommand = $"{HelperFile.WixLightPath} -spdb -sval -out \"{msiFilePath}\" \"{objFile}\"";
            _ = Exe.RunHostedCommand(lightCommand);

            progress(90);

        } finally {
            IoUtil.DeleteFileOrDirectoryHard(wxsFile, throwOnFailure: false);
            IoUtil.DeleteFileOrDirectoryHard(objFile, throwOnFailure: false);
        }
        progress(100);

    }

    protected override string[] GetMainExeSearchPaths(string packDirectory, string mainExeName)
    {
        return [
            Path.Combine(packDirectory, mainExeName),
            Path.Combine(packDirectory, mainExeName) + ".exe",
        ];
    }

    private string GetPortableStubFileName() => (Options.PackTitle ?? Options.PackId) + ".exe";

    private IReadOnlyList<ShortcutLocation> GetShortcuts() => [.. Options.Shortcuts.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Select(x => x.Trim())
                .Select(x => {
                    if (Enum.TryParse(x, true, out ShortcutLocation location)) {
                        return location;
                    }
                    return ShortcutLocation.None;
                })
                .Where(x => x != ShortcutLocation.None)
        ];
}