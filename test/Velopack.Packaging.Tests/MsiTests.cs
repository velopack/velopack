using System.Diagnostics;
using System.Runtime.Versioning;
using Microsoft.Win32;
using Velopack.Core;
using Velopack.Packaging.Commands;
using Velopack.Packaging.Windows.Commands;
using Velopack.Util;
using Velopack.Vpk;
using Velopack.Vpk.Logging;
using Velopack.TestCommon;
using WixToolset.Dtf.WindowsInstaller;

namespace Velopack.Packaging.Tests;

[SupportedOSPlatform("windows")]
public class MsiTests
{
    private readonly ITestOutputHelper _output;

    public MsiTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static void WaitUntilInstallDirUnlocked(string installDir)
    {
        // After an apply, Update.exe / the restarted app / the coverage collector may still be
        // exiting in the background. While any of them run from the install dir, the MSI cleanup
        // action cannot remove the directory, so wait until every exe can be opened exclusively.
        TestHelper.WaitUntil(() => {
            foreach (var exe in Directory.EnumerateFiles(installDir, "*.exe", SearchOption.AllDirectories)) {
                File.Open(exe, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose();
            }
        }, pollDelayMs: 1000);
    }

    private static string RunMsiExec(string rawArgs, ILogger logger, int? exitCode = 0)
    {
        var outputFile = PathHelper.GetTestRootPath($"run.{WindowsTestHelper.RandomString(8)}.log");

        try {
            var fix = new ProcessStartInfo("cmd.exe");
            fix.CreateNoWindow = true;
            fix.WorkingDirectory = Environment.CurrentDirectory;
            fix.Arguments = $"/c msiexec.exe {rawArgs} > \"{outputFile}\" 2>&1";

            Stopwatch sw = new Stopwatch();
            sw.Start();

            logger.Info($"TEST: Running cmd.exe {fix.Arguments}");
            using var p = Process.Start(fix);

            var timeout = TimeSpan.FromMinutes(3);
            if (!p.WaitForExit(timeout))
                throw new TimeoutException($"Process did not exit within {timeout.TotalSeconds}s.");

            var elapsed = sw.Elapsed;
            sw.Stop();

            logger.Info($"TEST: Process exited with code {p.ExitCode} in {elapsed.TotalSeconds}s");

            using var fs = IoUtil.Retry(
                () => File.Open(outputFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None),
                10,
                1000,
                logger.ToVelopackLogger());

            using var reader = new StreamReader(fs);
            var output = reader.ReadToEnd();

            if (String.IsNullOrWhiteSpace(output)) {
                logger.Warn($"TEST: Process output was empty");
            } else {
                logger.Info($"TEST: Process output: {Environment.NewLine}{output.Trim()}{Environment.NewLine}");
            }

            if (exitCode.HasValue && p.ExitCode != exitCode.Value) {
                throw new Exception($"Process exited with code {p.ExitCode} but expected {exitCode.Value}");
            }

            return output.Trim();
        } finally {
            try {
                File.Delete(outputFile);
            } catch { }
        }
    }

    private static string RunCoveredDotnetDeelevated(string exe, string[] args, string workingDir, ILogger logger, int? exitCode = 0)
    {
        // Runs dotnet-coverage with the target exe as a truly non-elevated user via explorer.exe.
        // explorer.exe delegates to the existing (non-elevated) shell process, so the child
        // process gets a proper non-elevated token (TokenIsElevated = false).
        // Note: "runas /trustlevel:0x20000" does NOT work for this because the restricted token
        // still reports TokenIsElevated=true (it was derived from an elevated session).
        var outputFile = PathHelper.GetTestRootPath($"run.{WindowsTestHelper.RandomString(8)}.log");
        var coverageFile = PathHelper.GetTestRootPath($"coverage.rundotnet.{WindowsTestHelper.RandomString(8)}.xml");
        var batchFile = PathHelper.GetTestRootPath($"run.{WindowsTestHelper.RandomString(8)}.cmd");

        if (!File.Exists(exe))
            throw new Exception($"File {exe} does not exist.");

        try {
            var argStr = string.Join(" ", args.Select(a => a.Contains(' ') ? $"\"{a}\"" : a));
            File.WriteAllText(batchFile,
                $"@cd /d \"{workingDir}\"\r\n" +
                $"@\"dotnet-coverage\" collect -o \"{coverageFile}\" -f cobertura \"{exe}\" {argStr} > \"{outputFile}\" 2>&1\r\n");

            // Launch the batch file via explorer.exe, which delegates to the non-elevated shell.
            var fix = new ProcessStartInfo("explorer.exe");
            fix.Arguments = $"\"{batchFile}\"";

            logger.Info($"TEST: Running de-elevated via explorer.exe: \"{batchFile}\"");
            using var p = Process.Start(fix);
            p?.WaitForExit(TimeSpan.FromSeconds(10)); // explorer.exe exits almost immediately

            // Poll for the output file (the batch file runs asynchronously via the shell)
            using var fs = IoUtil.Retry(
                () => File.Open(outputFile, FileMode.Open, FileAccess.ReadWrite, FileShare.None),
                30,
                1000,
                logger.ToVelopackLogger());

            using var reader = new StreamReader(fs);
            var output = reader.ReadToEnd();

            if (String.IsNullOrWhiteSpace(output)) {
                logger.Warn($"TEST: Process output was empty");
            } else {
                logger.Info($"TEST: Process output: {Environment.NewLine}{output.Trim()}{Environment.NewLine}");
            }

            return String.Join(
                Environment.NewLine,
                output
                    .Split('\n')
                    .Where(l => !l.Contains("Code coverage results"))
                    .Select(l => l.Trim())
            ).Trim();
        } finally {
            try { File.Delete(outputFile); } catch { }
            try { File.Delete(batchFile); } catch { }
            try { File.Delete(coverageFile); } catch { }
        }
    }

    private static async Task PackTestAppWithMsi(string id, string version, string testString,
        string releaseDir, ILogger logger, InstallLocation instLocation, string packTitle = null,
        string fileAssociations = null, string urlProtocols = null)
    {
        using var _ = TempUtil.GetTempDirectory(out var workDir);

        {
            var publishDir = Path.Combine(workDir, "publish");
            TestApp.PreparePublishDir(RID.Parse("win-x64"), testString, publishDir, logger);

            var options = new WindowsPackOptions {
                EntryExecutableName = "TestApp.exe",
                ReleaseDir = new DirectoryInfo(releaseDir),
                PackId = id,
                PackVersion = version,
                PackTitle = packTitle,
                TargetRuntime = RID.Parse("win-x64"),
                PackDirectory = publishDir,
                BuildMsi = true,
                InstLocation = instLocation,
                FileAssociations = fileAssociations,
                UrlProtocols = urlProtocols,
            };

            var runner = WindowsTestHelper.GetPackRunner(logger);
            await runner.Run(options);
        }
    }

    private static (bool found, string displayVersion) FindUninstallEntry(RegistryKey root, string appId)
    {
        using var key = root.OpenSubKey($@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MSI:{appId}");
        if (key == null) return (false, null);
        return (true, key.GetValue("DisplayVersion") as string);
    }

    // mirrors make_progid in src/bins/src/windows/associations.rs for ids/extensions which need no sanitizing
    private static string ProgId(string id, string ext) => $"{id}.{ext.TrimStart('.')}";

    /// <summary>
    /// Asserts a protocol key and a file association (ProgId + .ext default) registered by this install exist under
    /// <paramref name="classes"/>, carrying the install dir as ownership marker and the main exe as command target.
    /// </summary>
    private static void AssertAssociationsRegistered(RegistryKey classes, string id, string installDir, string appPath,
        string[] exts, string[] schemes)
    {
        var expectedRoot = Path.GetFullPath(installDir).TrimEnd('\\');
        foreach (var scheme in schemes) {
            using var key = classes.OpenSubKey(scheme);
            Assert.True(key != null, $"protocol key '{scheme}' is missing in {classes.Name}");
            Assert.Equal("", key.GetValue("URL Protocol"));
            // the marker must be exactly what Update.exe writes: no trailing separator from MSI's [INSTALLFOLDER]
            Assert.Equal(expectedRoot, key.GetValue("VelopackRootDir") as string, StringComparer.OrdinalIgnoreCase);
            using var command = key.OpenSubKey(@"shell\open\command");
            Assert.True(command != null, $"'{scheme}\\shell\\open\\command' is missing");
            Assert.StartsWith($"\"{appPath}\" ", command.GetValue("") as string, StringComparison.OrdinalIgnoreCase);
        }

        foreach (var ext in exts) {
            var progId = ProgId(id, ext);
            using var progIdKey = classes.OpenSubKey(progId);
            Assert.True(progIdKey != null, $"ProgId key '{progId}' is missing in {classes.Name}");
            Assert.Equal(expectedRoot, progIdKey.GetValue("VelopackRootDir") as string, StringComparer.OrdinalIgnoreCase);
            using var command = progIdKey.OpenSubKey(@"shell\open\command");
            Assert.True(command != null, $"'{progId}\\shell\\open\\command' is missing");
            Assert.StartsWith($"\"{appPath}\" ", command.GetValue("") as string, StringComparison.OrdinalIgnoreCase);
            using var extKey = classes.OpenSubKey(ext);
            Assert.True(extKey != null, $"extension key '{ext}' is missing in {classes.Name}");
            // nobody else handles our random extension, so we must have become the default
            Assert.Equal(progId, extKey.GetValue(""));
        }
    }

    private static void AssertAssociationsRemoved(RegistryKey classes, string id, string[] exts, string[] schemes)
    {
        foreach (var scheme in schemes) {
            using var key = classes.OpenSubKey(scheme);
            Assert.True(key == null, $"protocol key '{scheme}' should have been removed from {classes.Name}");
        }

        foreach (var ext in exts) {
            using var progIdKey = classes.OpenSubKey(ProgId(id, ext));
            Assert.True(progIdKey == null, $"ProgId key '{ProgId(id, ext)}' should have been removed from {classes.Name}");
            // nothing else referenced the extension, so the whole key goes too
            using var extKey = classes.OpenSubKey(ext);
            Assert.True(extKey == null, $"extension key '{ext}' should have been removed from {classes.Name}");
        }
    }

    private static void DeleteAssociationKeys(RegistryKey classes, string id, ILogger logger, string[] exts, string[] schemes)
    {
        foreach (var name in exts.Select(e => ProgId(id, e)).Concat(exts).Concat(schemes)) {
            try {
                classes.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
            } catch (Exception ex) {
                logger.Warn($"TEST: failed to clean up {classes.Name}\\{name}: {ex.Message}");
            }
        }
    }

    [Fact]
    public async Task TestPackGeneratesMsi()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<MsiTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);

        var exe = "testapp.exe";
        var pdb = Path.ChangeExtension(exe, ".pdb");
        var id = "Test.Squirrel-App";
        var version = "1.2.3";

        PathHelper.CopyRustAssetTo(exe, tmpOutput);
        PathHelper.CopyRustAssetTo(pdb, tmpOutput);

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = version,
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = tmpOutput,
            Shortcuts = "Desktop,StartMenuRoot",
            BuildMsi = true
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        await runner.Run(options);

        string msiPath = Path.Combine(tmpReleaseDir, $"{id}-win.msi");
        Assert.True(File.Exists(msiPath));
        using Database db = new Database(msiPath);
        var msiVersion = db.ExecuteScalar("SELECT `Value` FROM `Property` WHERE `Property` = 'ProductVersion'") as string;
        Assert.Equal("1.2.3.0", msiVersion);
    }

    [Fact]
    public async Task TestPackGeneratesMsiWithInstallerPages()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<MsiTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = TempUtil.GetTempDirectory(out var tmpAssets);

        var exe = "testapp.exe";
        var pdb = Path.ChangeExtension(exe, ".pdb");
        var id = "Test.Squirrel-App";
        var version = "1.2.3";

        PathHelper.CopyRustAssetTo(exe, tmpOutput);
        PathHelper.CopyRustAssetTo(pdb, tmpOutput);

        var welcomeFile = Path.Combine(tmpAssets, "welcome.txt");
        var readmeFile = Path.Combine(tmpAssets, "readme.txt");
        var licenseFile = Path.Combine(tmpAssets, "license.txt");
        var conclusionFile = Path.Combine(tmpAssets, "conclusion.txt");
        File.WriteAllText(welcomeFile, "WELCOME_TEXT_MARKER");
        File.WriteAllText(readmeFile, "README_TEXT_MARKER");
        File.WriteAllText(licenseFile, "LICENSE_TEXT_MARKER");
        File.WriteAllText(conclusionFile, "CONCLUSION_TEXT_MARKER");

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = version,
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = tmpOutput,
            Shortcuts = "Desktop,StartMenuRoot",
            BuildMsi = true,
            InstWelcome = welcomeFile,
            InstReadme = readmeFile,
            InstLicense = licenseFile,
            InstConclusion = conclusionFile,
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        await runner.Run(options);

        string msiPath = Path.Combine(tmpReleaseDir, $"{id}-win.msi");
        Assert.True(File.Exists(msiPath));

        using Database db = new Database(msiPath);

        // Welcome -> override of MsiWelcomeDescription property
        var welcomeProp = db.ExecuteScalar("SELECT `Value` FROM `Property` WHERE `Property` = 'MsiWelcomeDescription'") as string;
        Assert.Equal("WELCOME_TEXT_MARKER", welcomeProp);

        // Readme -> ReadmeDlg dialog should exist (content is embedded via WixVariable RTF, not an MSI property)
        var readmeDialog = db.ExecuteScalar("SELECT `Dialog` FROM `Dialog` WHERE `Dialog` = 'ReadmeDlg'") as string;
        Assert.Equal("ReadmeDlg", readmeDialog);

        // Conclusion -> WIXUI_EXITDIALOGOPTIONALTEXT property
        var conclusionProp = db.ExecuteScalar(
            "SELECT `Value` FROM `Property` WHERE `Property` = 'WIXUI_EXITDIALOGOPTIONALTEXT'") as string;
        Assert.Equal("CONCLUSION_TEXT_MARKER", conclusionProp);

        // License -> LicenseAgreementDlg dialog is always present; ensure WixUILicenseRtf was set
        // (the WixVariable is compiled into WixVariable table)
        var licenseDialog = db.ExecuteScalar("SELECT `Dialog` FROM `Dialog` WHERE `Dialog` = 'LicenseAgreementDlg'") as string;
        Assert.Equal("LicenseAgreementDlg", licenseDialog);
    }

    [Fact]
    public async Task TestPackGeneratesMsiWithCorrectBannerAndDialogImages()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<MsiTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = TempUtil.GetTempDirectory(out var tmpAssets);

        var exe = "testapp.exe";
        var id = "Test.Squirrel-App";

        PathHelper.CopyRustAssetTo(exe, tmpOutput);
        PathHelper.CopyRustAssetTo(Path.ChangeExtension(exe, ".pdb"), tmpOutput);

        // WiX embeds the files verbatim without decoding them, so marker bytes are enough
        // to tell which file ended up on which WiX binary
        var bannerFile = Path.Combine(tmpAssets, "custom_banner.bmp");
        var dialogFile = Path.Combine(tmpAssets, "custom_dialog.bmp");
        var bannerBytes = "TOP_BANNER_493x58_MARKER"u8.ToArray();
        var dialogBytes = "DIALOG_BACKGROUND_493x312_MARKER"u8.ToArray();
        File.WriteAllBytes(bannerFile, bannerBytes);
        File.WriteAllBytes(dialogFile, dialogBytes);

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = "1.2.3",
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = tmpOutput,
            BuildMsi = true,
            MsiTopBanner = bannerFile,
            MsiDialogBackground = dialogFile,
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        await runner.Run(options);

        string msiPath = Path.Combine(tmpReleaseDir, $"{id}-win.msi");
        Assert.True(File.Exists(msiPath));

        using Database db = new Database(msiPath);

        // --msiTopBanner -> the 493x58 top strip, --msiDialogBackground -> the 493x312
        // welcome/finish dialog background. these were applied swapped before (#1005)
        Assert.Equal(bannerBytes, ReadMsiBinaryData(db, "WixUI_Bmp_Banner"));
        Assert.Equal(dialogBytes, ReadMsiBinaryData(db, "WixUI_Bmp_Dialog"));
    }

    private static byte[] ReadMsiBinaryData(Database db, string name)
    {
        using var view = db.OpenView($"SELECT `Data` FROM `Binary` WHERE `Name` = '{name}'");
        view.Execute();
        using var record = view.Fetch();
        Assert.NotNull(record);
        using var stream = record.GetStream(1);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        return ms.ToArray();
    }

    [Fact]
    public async Task TestPackGeneratesMsiWithSpecifiedVersion()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<MsiTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);

        var exe = "testapp.exe";
        var pdb = Path.ChangeExtension(exe, ".pdb");
        var id = "Test.Squirrel-App";
        var version = "1.0.0";

        PathHelper.CopyRustAssetTo(exe, tmpOutput);
        PathHelper.CopyRustAssetTo(pdb, tmpOutput);

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = version,
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = tmpOutput,
            Shortcuts = "Desktop,StartMenuRoot",
            BuildMsi = true,
            MsiVersionOverride = "4.5.6.0"
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        await runner.Run(options);

        string msiPath = Path.Combine(tmpReleaseDir, $"{id}-win.msi");
        Assert.True(File.Exists(msiPath));

        using Database db = new Database(msiPath);
        var msiVersion = db.ExecuteScalar("SELECT `Value` FROM `Property` WHERE `Property` = 'ProductVersion'") as string;
        Assert.Equal("4.5.6.0", msiVersion);
    }

    [Theory]
    [InlineData(InstallLocation.PerUser)]
    [InlineData(InstallLocation.PerMachine)]
    [InlineData(InstallLocation.Either)]
    public async Task TestPackGeneratesMsiWithQuietDefaultInstallFolder(InstallLocation instLocation)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<MsiTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);

        var exe = "testapp.exe";
        var id = "Test.Squirrel-App";

        PathHelper.CopyRustAssetTo(exe, tmpOutput);
        PathHelper.CopyRustAssetTo(Path.ChangeExtension(exe, ".pdb"), tmpOutput);

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = "1.2.3",
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = tmpOutput,
            BuildMsi = true,
            InstLocation = instLocation,
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        await runner.Run(options);

        string msiPath = Path.Combine(tmpReleaseDir, $"{id}-win.msi");
        Assert.True(File.Exists(msiPath));

        // quiet/passive installs don't run the UI publishes which normally set the default
        // INSTALLFOLDER, so the MSI must contain execute-sequence custom actions applying the
        // same defaults (#945)
        const string perUserFolder = "[LocalAppDataFolder][ApplicationFolderName]";
        const string perMachineFolder = "[ProgramFiles64Folder][ApplicationFolderName]";
        const string baseCondition = "NOT Installed AND NOT VELOPACK_INSTALLDIR AND UILevel<5";

        (string Action, string Target, string Condition)[] expected = instLocation switch {
            InstallLocation.PerUser => [("SetQuietDefaultInstallFolder", perUserFolder, baseCondition)],
            InstallLocation.PerMachine => [("SetQuietDefaultInstallFolder", perMachineFolder, baseCondition)],
            _ => [
                ("SetQuietDefaultInstallFolderPerUser", perUserFolder, $"{baseCondition} AND NOT ALLUSERS=1"),
                ("SetQuietDefaultInstallFolderPerMachine", perMachineFolder, $"{baseCondition} AND ALLUSERS=1"),
            ],
        };

        using Database db = new Database(msiPath);
        foreach (var (action, target, condition) in expected) {
            var caSource = db.ExecuteScalar($"SELECT `Source` FROM `CustomAction` WHERE `Action` = '{action}'") as string;
            Assert.Equal("INSTALLFOLDER", caSource);
            var caTarget = db.ExecuteScalar($"SELECT `Target` FROM `CustomAction` WHERE `Action` = '{action}'") as string;
            Assert.Equal(target, caTarget);
            var seqCondition = db.ExecuteScalar($"SELECT `Condition` FROM `InstallExecuteSequence` WHERE `Action` = '{action}'") as string;
            Assert.Equal(condition, seqCondition);
        }
    }

    [Fact]
    public async Task TestPackGeneratesMsiWithUpgradeAwareCleanup()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<MsiTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);

        var exe = "testapp.exe";
        var id = "Test.Squirrel-App";

        PathHelper.CopyRustAssetTo(exe, tmpOutput);
        PathHelper.CopyRustAssetTo(Path.ChangeExtension(exe, ".pdb"), tmpOutput);

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = "1.2.3",
            PackTitle = "Squirrel Test Title",
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = tmpOutput,
            BuildMsi = true,
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        await runner.Run(options);

        string msiPath = Path.Combine(tmpReleaseDir, $"{id}-win.msi");
        Assert.True(File.Exists(msiPath));

        using Database db = new Database(msiPath);

        // cleanup paths (LocalAppData fallback dir, temp dir, MSI:{id} ARP key) are derived from
        // the pack ID, never from the display title
        var rustAppId = db.ExecuteScalar("SELECT `Value` FROM `Property` WHERE `Property` = 'RustAppId'") as string;
        Assert.Equal(id, rustAppId);

        // the app uninstall hook and the per-user cleanup must only run on a real uninstall, not
        // when the old product is removed as part of a major upgrade (#1004)
        const string uninstallOnly = "(REMOVE=\"ALL\") AND NOT UPGRADINGPRODUCTCODE";
        foreach (var action in new[] { "SetUninstallHookData", "UninstallHookDeferred", "SetUserRustCleanupData", "UserRustCleanup" }) {
            var condition = db.ExecuteScalar($"SELECT `Condition` FROM `InstallExecuteSequence` WHERE `Action` = '{action}'") as string;
            Assert.Equal(uninstallOnly, condition);
        }

        // RustCleanup runs on uninstall AND upgrade; the marshaled UPGRADINGPRODUCTCODE tells it
        // whether to remove everything or only purge the `current` payload dir
        var cleanupCondition = db.ExecuteScalar("SELECT `Condition` FROM `InstallExecuteSequence` WHERE `Action` = 'RustCleanup'") as string;
        Assert.Equal("(REMOVE=\"ALL\")", cleanupCondition);
        var cleanupData = db.ExecuteScalar("SELECT `Target` FROM `CustomAction` WHERE `Action` = 'SetRustCleanupData'") as string;
        // [ALLUSERS] scopes the ARP key sweep to the hive of the install being removed
        Assert.Equal("[INSTALLFOLDER]\"[RustAppId]\"[TempFolder]\"[LocalAppDataFolder]\"[UPGRADINGPRODUCTCODE]\"[ALLUSERS]", cleanupData);
        var userCleanupData = db.ExecuteScalar("SELECT `Target` FROM `CustomAction` WHERE `Action` = 'SetUserRustCleanupData'") as string;
        Assert.Equal("[INSTALLFOLDER]\"[RustAppId]\"[TempFolder]\"[LocalAppDataFolder]\"\"[ALLUSERS]", userCleanupData);

        // RustCleanup must run non-impersonated (elevated for per-machine installs) so it can
        // remove a Program Files install dir; UserRustCleanup must run impersonated so it resolves
        // the installing user's profile (shortcuts, %LocalAppData% fallback dir) instead of the
        // SYSTEM profile (#989)
        const int msidbCustomActionTypeNoImpersonate = 2048;
        var cleanupType = Convert.ToInt32(db.ExecuteScalar("SELECT `Type` FROM `CustomAction` WHERE `Action` = 'RustCleanup'"));
        Assert.True((cleanupType & msidbCustomActionTypeNoImpersonate) != 0, "RustCleanup should be non-impersonated");
        var userCleanupType = Convert.ToInt32(db.ExecuteScalar("SELECT `Type` FROM `CustomAction` WHERE `Action` = 'UserRustCleanup'"));
        Assert.True((userCleanupType & msidbCustomActionTypeNoImpersonate) == 0, "UserRustCleanup should be impersonated");

        // ordering: hook runs while the app files still exist; cleanup runs after MSI removed its
        // own files and before RemoveFolders
        int Seq(string action) => Convert.ToInt32(db.ExecuteScalar($"SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action` = '{action}'"));
        Assert.True(Seq("UninstallHookDeferred") < Seq("RemoveFiles"), "uninstall hook should run before RemoveFiles");
        Assert.True(Seq("RemoveFiles") < Seq("RustCleanup"), "RustCleanup should run after RemoveFiles");
        Assert.True(Seq("RustCleanup") < Seq("UserRustCleanup"), "UserRustCleanup should run after RustCleanup");
        Assert.True(Seq("UserRustCleanup") < Seq("RemoveFolders"), "UserRustCleanup should run before RemoveFolders");

        // the upgrade-mode `current` purge relies on the old product being removed (and cleaned)
        // before the new payload is laid down: MajorUpgrade is authored with afterInstallValidate
        Assert.True(Seq("InstallValidate") < Seq("RemoveExistingProducts"), "RemoveExistingProducts must run after InstallValidate");
        Assert.True(Seq("RemoveExistingProducts") < Seq("InstallInitialize"),
            "RemoveExistingProducts must run before InstallInitialize (afterInstallValidate)");

        // same-version upgrades are allowed: the upgrade row's max version is inclusive, so a
        // prerelease and its release (same msi version) upgrade rather than install side by side
        const int msidbUpgradeAttributesVersionMaxInclusive = 0x200;
        var upgradeAttrs = Convert.ToInt32(db.ExecuteScalar(
            "SELECT `Attributes` FROM `Upgrade` WHERE `ActionProperty` = 'WIX_UPGRADE_DETECTED'"));
        Assert.True((upgradeAttrs & msidbUpgradeAttributesVersionMaxInclusive) != 0, "same-version upgrades should be allowed");

        // the downgrade launch condition uses the localized message set by RustSetLocaleStrings,
        // which runs in both sequences so silent installs get it too
        var downgradeText = db.ExecuteScalar("SELECT `Description` FROM `LaunchCondition` WHERE `Condition` = 'NOT WIX_DOWNGRADE_DETECTED'") as string;
        Assert.Equal("[MsiDowngradeError]", downgradeText);
        Assert.True(Seq("RustSetLocaleStrings") < Seq("LaunchConditions"), "localized strings must be set before LaunchConditions");
        // only needed for the first-install launch condition: must not run (and risk failing) on uninstall
        var localeCondition = db.ExecuteScalar("SELECT `Condition` FROM `InstallExecuteSequence` WHERE `Action` = 'RustSetLocaleStrings'") as string;
        Assert.Equal("NOT Installed", localeCondition);

        // an MSI upgrade gives the app --veloapp-updated (like an in-app update), a fresh install --veloapp-install
        foreach (var action in new[] { "SetInstallHookData", "InstallHookDeferred" }) {
            var condition = db.ExecuteScalar($"SELECT `Condition` FROM `InstallExecuteSequence` WHERE `Action` = '{action}'") as string;
            Assert.Equal("NOT REMOVE AND NOT WIX_UPGRADE_DETECTED", condition);
        }
        foreach (var action in new[] { "SetUpdatedHookData", "UpdatedHookDeferred" }) {
            var condition = db.ExecuteScalar($"SELECT `Condition` FROM `InstallExecuteSequence` WHERE `Action` = '{action}'") as string;
            Assert.Equal("NOT REMOVE AND WIX_UPGRADE_DETECTED", condition);
        }
        var updatedHookTarget = db.ExecuteScalar("SELECT `Target` FROM `CustomAction` WHERE `Action` = 'UpdatedHookDeferred'") as string;
        Assert.Equal("UpdatedHookDeferred", updatedHookTarget);

        // VELOPACK_INSTALLDIR only applies on first install; on maintenance/uninstall the
        // INSTALLFOLDER handed to the elevated cleanup must come from the registered install state
        var overrideCondition = db.ExecuteScalar("SELECT `Condition` FROM `InstallExecuteSequence` WHERE `Action` = 'SetINSTALLFOLDER'") as string;
        Assert.Equal("VELOPACK_INSTALLDIR AND NOT Installed", overrideCondition);
    }

    [Fact]
    public async Task TestPackGeneratesMsiWithAssociationActions()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<MsiTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);

        var exe = "testapp.exe";
        var id = "Test.Squirrel-App";

        PathHelper.CopyRustAssetTo(exe, tmpOutput);
        PathHelper.CopyRustAssetTo(Path.ChangeExtension(exe, ".pdb"), tmpOutput);

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = "1.2.3",
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = tmpOutput,
            BuildMsi = true,
            FileAssociations = ".vpkmsi",
            UrlProtocols = "vpkmsi",
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        await runner.Run(options);

        string msiPath = Path.Combine(tmpReleaseDir, $"{id}-win.msi");
        Assert.True(File.Exists(msiPath));

        using (Database db = new Database(msiPath)) {
            const int msidbCustomActionTypeContinue = 64; // Return="ignore"
            const int msidbCustomActionTypeRollback = 256;
            const int msidbCustomActionTypeInScript = 1024; // deferred and rollback
            const int msidbCustomActionTypeNoImpersonate = 2048;
            const int msidbCustomActionTypeProperty = 51; // type-51 "set property" (Set*Data)
            int Seq(string action) =>
                Convert.ToInt32(db.ExecuteScalar($"SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action` = '{action}'"));
            string Condition(string action) =>
                db.ExecuteScalar($"SELECT `Condition` FROM `InstallExecuteSequence` WHERE `Action` = '{action}'") as string;
            int Type(string action) => Convert.ToInt32(db.ExecuteScalar($"SELECT `Type` FROM `CustomAction` WHERE `Action` = '{action}'"));

            // one action per (operation, scope): the per-machine variants run non-impersonated (SYSTEM can write
            // HKLM), the per-user variants impersonated (HKCU must be the installing user's), same DLL entry.
            // Rollback undoes a sync with the removal entry point when the install fails after it.
            var ops = new[] {
                ("Sync", "SyncAssociationsDeferred"), ("Remove", "RemoveAssociationsDeferred"), ("Rollback", "RemoveAssociationsDeferred")
            };
            foreach (var (op, entry) in ops) {
                foreach (var scope in new[] { "Machine", "User" }) {
                    var action = $"{op}Associations{scope}";
                    var type = Type(action);
                    Assert.Equal(scope == "Machine", (type & msidbCustomActionTypeNoImpersonate) != 0);
                    Assert.True((type & msidbCustomActionTypeInScript) != 0, $"{action} must be in-script (deferred/rollback)");
                    Assert.Equal(op == "Rollback", (type & msidbCustomActionTypeRollback) != 0);
                    Assert.True((type & msidbCustomActionTypeContinue) != 0, $"{action} must never fail the install");
                    Assert.Equal(entry, db.ExecuteScalar($"SELECT `Target` FROM `CustomAction` WHERE `Action` = '{action}'") as string);
                    // the in-script action learns the install dir and scope from its CustomActionData
                    Assert.Equal(msidbCustomActionTypeProperty, Type($"Set{action}Data") & 0x3F);
                    Assert.Equal(action, db.ExecuteScalar($"SELECT `Source` FROM `CustomAction` WHERE `Action` = 'Set{action}Data'") as string);
                    var data = db.ExecuteScalar($"SELECT `Target` FROM `CustomAction` WHERE `Action` = 'Set{action}Data'") as string;
                    Assert.Equal("[INSTALLFOLDER]\"[ALLUSERS]", data);
                    Assert.Equal(Condition(action), Condition($"Set{action}Data"));
                    Assert.True(Seq($"Set{action}Data") < Seq(action), $"Set{action}Data must run before {action}");
                }
            }

            Assert.Equal("NOT REMOVE AND ALLUSERS=1", Condition("SyncAssociationsMachine"));
            Assert.Equal("NOT REMOVE AND NOT ALLUSERS=1", Condition("SyncAssociationsUser"));
            Assert.Equal(Condition("SyncAssociationsMachine"), Condition("RollbackAssociationsMachine"));
            Assert.Equal(Condition("SyncAssociationsUser"), Condition("RollbackAssociationsUser"));
            // removal has no UPGRADINGPRODUCTCODE guard: during a major upgrade the old product removes its set
            // (before the new product's InstallFiles, as RemoveExistingProducts is afterInstallValidate) and the
            // new product then registers its own, which is how dropped associations get cleaned
            Assert.Equal("(REMOVE=\"ALL\") AND ALLUSERS=1", Condition("RemoveAssociationsMachine"));
            Assert.Equal("(REMOVE=\"ALL\") AND NOT ALLUSERS=1", Condition("RemoveAssociationsUser"));

            foreach (var scope in new[] { "Machine", "User" }) {
                // sync once the payload and the channel patch are in place; remove while sq.version still exists
                Assert.True(Seq("InstallFiles") < Seq($"SyncAssociations{scope}"), "sync must run after InstallFiles");
                Assert.True(Seq("PatchChannelDeferred") < Seq($"SyncAssociations{scope}"), "sync must run after PatchChannelDeferred");
                Assert.True(Seq($"RemoveAssociations{scope}") < Seq("RemoveFiles"), "remove must run before RemoveFiles");
                // the rollback script runs in reverse order: scheduled between InstallFiles and the sync, the rollback
                // removes the registrations before InstallFiles is rolled back, while current\sq.version is still there
                Assert.True(Seq("InstallFiles") < Seq($"RollbackAssociations{scope}"), "rollback must be scheduled after InstallFiles");
                Assert.True(Seq($"RollbackAssociations{scope}") < Seq($"SyncAssociations{scope}"), "rollback must be scheduled before the sync");
            }
            Assert.True(Seq("RemoveExistingProducts") < Seq("InstallInitialize"), "old product must be removed before the new install");
        }

        // a package without associations gets none of these actions
        using var _3 = TempUtil.GetTempDirectory(out var tmpReleaseDir2);
        options.ReleaseDir = new DirectoryInfo(tmpReleaseDir2);
        options.FileAssociations = null;
        options.UrlProtocols = null;
        await WindowsTestHelper.GetPackRunner(logger).Run(options);
        msiPath = Path.Combine(tmpReleaseDir2, $"{id}-win.msi");
        using (Database db = new Database(msiPath)) {
            Assert.Empty(db.ExecuteStringQuery("SELECT `Action` FROM `CustomAction` WHERE `Action` = 'SyncAssociationsUser'"));
            Assert.Empty(db.ExecuteStringQuery("SELECT `Action` FROM `CustomAction` WHERE `Action` = 'RollbackAssociationsUser'"));
            Assert.Empty(db.ExecuteStringQuery("SELECT `Action` FROM `InstallExecuteSequence` WHERE `Action` = 'RemoveAssociationsMachine'"));
        }
    }

    [Fact]
    public async Task TestMsiPerUserInstallSyncsAssociations()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<MsiTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);

        string id = "MsiAssocTest";
        var installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), id);
        var msiPath = Path.Combine(releaseDir, $"{id}-win.msi");
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");

        // unique names per run, so a leftover registration from an aborted run can never satisfy an assertion
        var suffix = WindowsTestHelper.RandomString(8).ToLowerInvariant();
        var extKeep = $".vpkmsi{suffix}a";
        var extDrop = $".vpkmsi{suffix}b";
        var extNew = $".vpkmsi{suffix}c";
        var schemeKeep = $"vpkmsi{suffix}a";
        var schemeDrop = $"vpkmsi{suffix}b";
        var schemeNew = $"vpkmsi{suffix}c";
        var unrelatedKey = $"VelopackTestUnrelated.{suffix}";
        string[] allExts = [extKeep, extDrop, extNew];
        string[] allSchemes = [schemeKeep, schemeDrop, schemeNew];

        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes", true);
        try {
            using (var unrelated = classes.CreateSubKey(unrelatedKey)) {
                unrelated.SetValue("", "keep me");
            }

            // install v1 per-user: the impersonated custom action registers in the installing user's HKCU
            await PackTestAppWithMsi(id, "1.0.0", "version 1 test", releaseDir, logger, InstallLocation.PerUser,
                fileAssociations: $"{extKeep},{extDrop}", urlProtocols: $"{schemeKeep},{schemeDrop}");
            logger.Info("TEST: Installing v1 MSI per-user...");
            RunMsiExec($"/i \"{msiPath}\" /qn", logger);
            Assert.True(File.Exists(appPath), $"TestApp.exe not found at {appPath}");
            AssertAssociationsRegistered(classes, id, installDir, appPath, [extKeep, extDrop], [schemeKeep, schemeDrop]);
            using (var hklmScheme = Registry.LocalMachine.OpenSubKey($@"Software\Classes\{schemeKeep}")) {
                Assert.True(hklmScheme == null, "a per-user install must not register in HKLM");
            }
            logger.Info("TEST: v1 registrations verified");

            // in-app update to v2 with a changed set: Update.exe must detect the per-user MSI scope (ARP entry in
            // HKCU) and sync there, removing what v2 dropped
            await PackTestAppWithMsi(id, "2.0.0", "version 2 test", releaseDir, logger, InstallLocation.PerUser,
                fileAssociations: $"{extKeep},{extNew}", urlProtocols: schemeKeep);
            WindowsTestHelper.RunCoveredDotnet(appPath, ["download", releaseDir], installDir, logger);
            WindowsTestHelper.RunCoveredDotnet(appPath, ["apply", releaseDir], installDir, logger, exitCode: null);
            TestHelper.WaitUntil(() => {
                var version = WindowsTestHelper.RunCoveredDotnet(appPath, ["version"], installDir, logger);
                Assert.EndsWith(Environment.NewLine + "2.0.0", version);
            }, pollDelayMs: 1000);
            // the associations are synced in the background after the directory swap, so poll for the end state
            TestHelper.WaitUntil(() => {
                AssertAssociationsRegistered(classes, id, installDir, appPath, [extKeep, extNew], [schemeKeep]);
                AssertAssociationsRemoved(classes, id, [extDrop], [schemeDrop]);
            }, pollDelayMs: 1000);
            logger.Info("TEST: v2 (in-app update) registrations verified");

            // major upgrade via the v3 MSI with yet another set: the old product's removal action drops the
            // currently registered (v2) set, then the new product registers v3's
            await PackTestAppWithMsi(id, "3.0.0", "version 3 test", releaseDir, logger, InstallLocation.PerUser,
                fileAssociations: extKeep, urlProtocols: $"{schemeKeep},{schemeNew}");
            WaitUntilInstallDirUnlocked(installDir);
            logger.Info("TEST: Upgrading via v3 MSI...");
            RunMsiExec($"/i \"{msiPath}\" /qn", logger);
            var chk3version = WindowsTestHelper.RunCoveredDotnet(appPath, ["version"], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "3.0.0", chk3version);
            AssertAssociationsRegistered(classes, id, installDir, appPath, [extKeep], [schemeKeep, schemeNew]);
            AssertAssociationsRemoved(classes, id, [extDrop, extNew], [schemeDrop]);
            logger.Info("TEST: v3 (MSI upgrade) registrations verified");

            // uninstall removes everything this install owns and nothing else
            WaitUntilInstallDirUnlocked(installDir);
            logger.Info("TEST: Uninstalling v3 MSI...");
            RunMsiExec($"/x \"{msiPath}\" /qn", logger);
            Assert.False(Directory.Exists(installDir), $"Install directory should have been removed: {installDir}");
            AssertAssociationsRemoved(classes, id, allExts, allSchemes);
            using (var unrelated = classes.OpenSubKey(unrelatedKey)) {
                Assert.NotNull(unrelated);
                Assert.Equal("keep me", unrelated.GetValue(""));
            }
            logger.Info("TEST: uninstall verified");
        } finally {
            try {
                if (File.Exists(msiPath)) {
                    RunMsiExec($"/x \"{msiPath}\" /qn", logger, exitCode: null);
                }
            } catch {
                // best effort cleanup
            }

            try {
                if (Directory.Exists(installDir)) {
                    IoUtil.Retry(() => IoUtil.DeleteFileOrDirectoryHard(installDir), 10, 1000);
                }
            } catch {
                // best effort cleanup
            }

            DeleteAssociationKeys(classes, id, logger, allExts, allSchemes.Append(unrelatedKey).ToArray());
        }
    }

    // Requires elevation and UAC approval. Explicit tests are skipped unless asked for; to run from CLI:
    //  dotnet test --project test/Velopack.Packaging.Tests -- --filter-method "*TestMsiPerMachineInstallRegistersAssociationsInHklm*" --explicit only
    [Fact(Explicit = true)]
    public async Task TestMsiPerMachineInstallRegistersAssociationsInHklm()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<MsiTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);

        string id = "MsiPerMachineAssocTest";
        var installDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), id);
        var fallbackDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), id);
        var msiPath = Path.Combine(releaseDir, $"{id}-win.msi");
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");

        var suffix = WindowsTestHelper.RandomString(8).ToLowerInvariant();
        string[] exts = [$".vpkmsim{suffix}"];
        string[] schemes = [$"vpkmsim{suffix}"];

        using var machineClasses = Registry.LocalMachine.OpenSubKey(@"Software\Classes", true);
        using var userClasses = Registry.CurrentUser.CreateSubKey(@"Software\Classes", true);
        try {
            await PackTestAppWithMsi(id, "1.0.0", "version 1 test", releaseDir, logger, InstallLocation.Either,
                fileAssociations: string.Join(",", exts), urlProtocols: string.Join(",", schemes));

            // per-machine (ALLUSERS=1): the non-impersonated custom action registers in HKLM, not in HKCU
            logger.Info("TEST: Installing MSI per-machine...");
            RunMsiExec($"/i \"{msiPath}\" /qn ALLUSERS=1", logger);
            Assert.True(File.Exists(appPath), $"TestApp.exe not found at {appPath}");
            AssertAssociationsRegistered(machineClasses, id, installDir, appPath, exts, schemes);
            AssertAssociationsRemoved(userClasses, id, exts, schemes);
            logger.Info("TEST: HKLM registrations verified");

            WaitUntilInstallDirUnlocked(installDir);
            logger.Info("TEST: Uninstalling MSI...");
            RunMsiExec($"/x \"{msiPath}\" /qn", logger);
            Assert.False(Directory.Exists(installDir), $"Install directory should have been removed: {installDir}");
            AssertAssociationsRemoved(machineClasses, id, exts, schemes);
            logger.Info("TEST: uninstall verified");
        } finally {
            try {
                if (File.Exists(msiPath)) {
                    RunMsiExec($"/x \"{msiPath}\" /qn", logger, exitCode: null);
                }
            } catch {
                // best effort cleanup
            }

            foreach (var dir in new[] { installDir, fallbackDir }) {
                try {
                    if (Directory.Exists(dir)) {
                        IoUtil.Retry(() => IoUtil.DeleteFileOrDirectoryHard(dir), 10, 1000);
                    }
                } catch {
                    // best effort cleanup
                }
            }

            DeleteAssociationKeys(machineClasses, id, logger, exts, schemes);
            DeleteAssociationKeys(userClasses, id, logger, exts, schemes);
        }
    }

    [Fact]
    public async Task TestPackGeneratesMsiWithBracketsInTitle()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<MsiTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);

        var exe = "testapp.exe";
        var id = "Test.Squirrel-App";
        var title = "BracketApp [Staging]";

        PathHelper.CopyRustAssetTo(exe, tmpOutput);
        PathHelper.CopyRustAssetTo(Path.ChangeExtension(exe, ".pdb"), tmpOutput);

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = "1.2.3",
            PackTitle = title,
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = tmpOutput,
            Shortcuts = "Desktop,StartMenuRoot",
            BuildMsi = true,
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        await runner.Run(options);

        string msiPath = Path.Combine(tmpReleaseDir, $"{id}-win.msi");
        Assert.True(File.Exists(msiPath));

        // in MSI "Formatted" columns, square brackets are property-reference syntax and must be
        // escaped as [\[] / [\]] or they get stripped, breaking shortcut targets and ARP (#946)
        const string escapedStub = @"[INSTALLFOLDER]BracketApp [\[]Staging[\]].exe";

        using Database db = new Database(msiPath);

        var shortcutTargets = db.ExecuteStringQuery("SELECT `Target` FROM `Shortcut`");
        Assert.Equal(2, shortcutTargets.Count);
        Assert.All(shortcutTargets, t => Assert.Equal(escapedStub, t));

        // shortcut names are Filename columns (not Formatted), brackets must remain literal
        var shortcutNames = db.ExecuteStringQuery("SELECT `Name` FROM `Shortcut`");
        Assert.All(shortcutNames, n => Assert.Contains(title, n));

        var displayName = db.ExecuteScalar("SELECT `Value` FROM `Registry` WHERE `Name` = 'DisplayName'") as string;
        Assert.Equal(@"BracketApp [\[]Staging[\]]", displayName);

        var displayIcon = db.ExecuteScalar("SELECT `Value` FROM `Registry` WHERE `Name` = 'DisplayIcon'") as string;
        Assert.Equal(escapedStub, displayIcon);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TestPackMsiLaunchTargetFollowsNoStub(bool noStub)
    {
        // https://github.com/velopack/velopack/issues/1060
        // With --noStub there is no launcher at the install root, so the shortcuts, DisplayIcon
        // and the finish-dialog launch (RustStubFileName) must target current\<main exe>.
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<MsiTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);

        var exe = "testapp.exe";
        var id = "Test.Squirrel-App";

        PathHelper.CopyRustAssetTo(exe, tmpOutput);

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = "1.2.3",
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = tmpOutput,
            Shortcuts = "Desktop,StartMenu,StartMenuRoot,Startup",
            BuildMsi = true,
            NoStub = noStub,
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        await runner.Run(options);

        string msiPath = Path.Combine(tmpReleaseDir, $"{id}-win.msi");
        Assert.True(File.Exists(msiPath));

        var launchFile = noStub ? @"current\testapp.exe" : $"{id}.exe";
        var workDir = noStub ? "CURRENTFOLDER" : "INSTALLFOLDER";

        using Database db = new Database(msiPath);

        var shortcutTargets = db.ExecuteStringQuery("SELECT `Target` FROM `Shortcut`");
        Assert.Equal(4, shortcutTargets.Count);
        Assert.All(shortcutTargets, t => Assert.Equal($"[INSTALLFOLDER]{launchFile}", t));

        var shortcutWorkDirs = db.ExecuteStringQuery("SELECT `WkDir` FROM `Shortcut`");
        Assert.All(shortcutWorkDirs, w => Assert.Equal(workDir, w));

        var displayIcon = db.ExecuteScalar("SELECT `Value` FROM `Registry` WHERE `Name` = 'DisplayIcon'") as string;
        Assert.Equal($"[INSTALLFOLDER]{launchFile}", displayIcon);

        var launchProp = db.ExecuteScalar("SELECT `Value` FROM `Property` WHERE `Property` = 'RustStubFileName'") as string;
        Assert.Equal(launchFile, launchProp);

        // File.FileName is "short|long" when the long name is not 8.3, so compare the long part.
        var fileNames = db.ExecuteStringQuery("SELECT `FileName` FROM `File`").Select(n => n.Split('|').Last()).ToArray();
        Assert.Contains("testapp.exe", fileNames);
        Assert.Equal(!noStub, fileNames.Contains($"{id}.exe"));
        Assert.DoesNotContain(fileNames, n => n.EndsWith("_ExecutionStub.exe"));
    }

    [Fact]
    public async Task TestMsiPerUserInstallAndUpdate()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<MsiTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);

        string id = "MsiPerUserTest";
        var installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), id);
        var msiPath = Path.Combine(releaseDir, $"{id}-win.msi");
        // save v1 MSI path separately — packing v2 overwrites msiPath, and users typically
        // uninstall using the same MSI file they installed with (Windows caches it too).
        var v1MsiPath = Path.Combine(releaseDir, $"{id}-v1.msi");
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");
        var hookTempDir = Path.Combine(Path.GetTempPath(), $"velopack_hooks_{id}");

        try {
            // clean up any leftover hook files from previous runs
            if (Directory.Exists(hookTempDir))
                IoUtil.DeleteFileOrDirectoryHard(hookTempDir);

            // pack v1
            await PackTestAppWithMsi(id, "1.0.0", "version 1 test", releaseDir, logger, InstallLocation.PerUser);
            Assert.True(File.Exists(msiPath), $"MSI not found at {msiPath}");

            // save a copy before packing v2 overwrites msiPath
            File.Copy(msiPath, v1MsiPath, true);

            // install via msiexec per-user. no INSTALLFOLDER is passed on purpose: silent installs
            // must default to %LocalAppData%\{packId} even though the UI events don't fire (#945)
            logger.Info("TEST: Installing MSI per-user...");
            RunMsiExec($"/i \"{v1MsiPath}\" /qn", logger);

            // verify install
            Assert.True(File.Exists(appPath), $"TestApp.exe not found at {appPath}");
            var chk1version = WindowsTestHelper.RunCoveredDotnet(appPath, ["version"], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "1.0.0", chk1version);
            logger.Info("TEST: v1 installed and verified");

            // verify install hook was executed
            var installHookFile = Path.Combine(installDir, "args.txt");
            Assert.True(File.Exists(installHookFile), $"Install hook file not found at {installHookFile}");
            var installHookContent = File.ReadAllText(installHookFile).Trim();
            Assert.Contains("OnAfterInstallFastCallback: --veloapp-install 1.0.0", installHookContent);
            logger.Info("TEST: install hook verified");

            // verify uninstall registry is in HKCU (not HKLM) for per-user install
            var (hkcuFound, hkcuVersion) = FindUninstallEntry(Registry.CurrentUser, id);
            var (hklmFound, _) = FindUninstallEntry(Registry.LocalMachine, id);
            Assert.True(hkcuFound, "Uninstall entry should exist in HKCU for per-user MSI install");
            Assert.False(hklmFound, "Uninstall entry should NOT exist in HKLM for per-user MSI install");
            Assert.Equal("1.0.0", hkcuVersion);
            logger.Info("TEST: registry entry verified in HKCU with version 1.0.0");

            // per-user installs to writable dir, so packages dir should be in install dir
            string packagesPath = Path.Combine(installDir, "packages");
            var chk1pkgdir = WindowsTestHelper.RunCoveredDotnet(appPath, ["packagesdir"], installDir, logger);
            Assert.EndsWith(Environment.NewLine + packagesPath, chk1pkgdir);
            logger.Info("TEST: packages dir verified at " + packagesPath);

            // no updates available yet
            var chk1check = WindowsTestHelper.RunCoveredDotnet(appPath, ["check", releaseDir], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "no updates", chk1check);

            // pack v2
            await PackTestAppWithMsi(id, "2.0.0", "version 2 test", releaseDir, logger, InstallLocation.PerUser);

            // check for updates
            var chk2check = WindowsTestHelper.RunCoveredDotnet(appPath, ["check", releaseDir], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "update: 2.0.0", chk2check);
            logger.Info("TEST: found v2 update");

            // download update (this puts the nupkg in the locator's packages dir)
            WindowsTestHelper.RunCoveredDotnet(appPath, ["download", releaseDir], installDir, logger);

            // verify nupkg ended up in the correct packages dir
            var nupkgFileName = $"{id}-2.0.0-full.nupkg";
            Assert.True(File.Exists(Path.Combine(packagesPath, nupkgFileName)),
                $"Downloaded nupkg not found in expected packages dir: {packagesPath}");
            logger.Info("TEST: nupkg downloaded to correct packages dir");

            // apply update; update.exe swaps the app in a separate process, so poll for the new version
            WindowsTestHelper.RunCoveredDotnet(appPath, ["apply", releaseDir], installDir, logger, exitCode: null);
            TestHelper.WaitUntil(() => {
                var chk2version = WindowsTestHelper.RunCoveredDotnet(appPath, ["version"], installDir, logger);
                Assert.EndsWith(Environment.NewLine + "2.0.0", chk2version);
            }, pollDelayMs: 1000);
            logger.Info("TEST: v2 update verified");

            // verify registry was updated to v2
            var (hkcuFound2, hkcuVersion2) = FindUninstallEntry(Registry.CurrentUser, id);
            Assert.True(hkcuFound2, "Uninstall entry should still exist in HKCU after update");
            Assert.Equal("2.0.0", hkcuVersion2);
            logger.Info("TEST: registry entry verified in HKCU with version 2.0.0");

            // uninstall using the same MSI that was used to install.
            // (msiexec /x requires the MSI's ProductCode to match a registered product.)
            WaitUntilInstallDirUnlocked(installDir);
            logger.Info("TEST: Uninstalling MSI...");
            RunMsiExec($"/x \"{v1MsiPath}\" /qn", logger);

            // verify uninstall hook was executed (check temp dir since install dir is deleted by MSI)
            var uninstallHookFile = Path.Combine(hookTempDir, "args.txt");
            Assert.True(File.Exists(uninstallHookFile), $"Uninstall hook file not found at {uninstallHookFile}");
            var uninstallHookContent = File.ReadAllText(uninstallHookFile).Trim();
            Assert.Contains("OnBeforeUninstallFastCallback: --veloapp-uninstall", uninstallHookContent);
            logger.Info("TEST: uninstall hook verified");

            // verify install dir was cleaned up
            Assert.False(Directory.Exists(installDir), $"Install directory should have been removed: {installDir}");
            logger.Info("TEST: install directory cleaned up");
        } finally {
            // cleanup: uninstall MSI (best effort, may already be uninstalled)
            try {
                if (File.Exists(v1MsiPath)) {
                    RunMsiExec($"/x \"{v1MsiPath}\" /qn", logger, exitCode: null);
                }
            } catch {
                // best effort cleanup
            }

            try {
                if (Directory.Exists(installDir)) {
                    IoUtil.Retry(() => IoUtil.DeleteFileOrDirectoryHard(installDir), 10, 1000);
                }
            } catch {
                // best effort cleanup
            }

            try {
                if (Directory.Exists(hookTempDir)) {
                    IoUtil.DeleteFileOrDirectoryHard(hookTempDir);
                }
            } catch {
                // best effort cleanup
            }
        }
    }

    [Fact]
    public async Task TestMsiUpgradeKeepsUserDataAndUninstallCleansEverything()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<MsiTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);

        string id = "MsiUpgradeTest";
        // a title distinct from the ID: shortcuts/stub use the title, but cleanup paths
        // (LocalAppData fallback, temp, ARP key) must be derived from the ID
        string title = "Msi Upgrade Test App";
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var installDir = Path.Combine(localAppData, id);
        var titleDir = Path.Combine(localAppData, title);
        var msiPath = Path.Combine(releaseDir, $"{id}-win.msi");
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");
        var hookTempDir = Path.Combine(Path.GetTempPath(), $"velopack_hooks_{id}");
        var hookFile = Path.Combine(hookTempDir, "args.txt");
        var velopackTempDir = Path.Combine(Path.GetTempPath(), $"velopack_{id}");
        var desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        var msiShortcut = Path.Combine(desktop, $"{title}.lnk");
        var userShortcut = Path.Combine(desktop, $"{title} User Copy.lnk");

        try {
            // clean up any leftover hook files from previous runs
            if (Directory.Exists(hookTempDir))
                IoUtil.DeleteFileOrDirectoryHard(hookTempDir);

            // pack + install v1
            await PackTestAppWithMsi(id, "1.0.0", "version 1 test", releaseDir, logger, InstallLocation.PerUser, title);
            logger.Info("TEST: Installing v1 MSI...");
            RunMsiExec($"/i \"{msiPath}\" /qn", logger);
            Assert.True(File.Exists(appPath), $"TestApp.exe not found at {appPath}");
            Assert.True(File.Exists(msiShortcut), $"Desktop shortcut not found at {msiShortcut}");
            Assert.Contains("OnAfterInstallFastCallback: --veloapp-install 1.0.0", File.ReadAllText(hookFile));
            File.Delete(hookFile);

            // a package newer than the incoming MSI (e.g. downloaded by an in-app update) must be
            // removed by the upgrade, or the app would auto-apply it on next start and undo the MSI
            var packagesDir = Path.Combine(installDir, "packages");
            Directory.CreateDirectory(packagesDir);
            var newerPackage = Path.Combine(packagesDir, $"{id}-9.9.9-full.nupkg");
            File.WriteAllText(newerPackage, "not a real package");
            var betaIdFile = Path.Combine(packagesDir, ".betaId");
            File.WriteAllText(betaIdFile, "staged rollout id");

            // simulate user state: a data dir outside `current` (must survive MSI upgrades) and a
            // stray file inside `current` (must be purged so the upgrade lays down a clean payload)
            var userDataFile = Path.Combine(installDir, "user-data", "settings.json");
            Directory.CreateDirectory(Path.Combine(installDir, "user-data"));
            File.WriteAllText(userDataFile, "{}");
            var strayPayloadFile = Path.Combine(installDir, "current", "stray-old-file.txt");
            File.WriteAllText(strayPayloadFile, "left behind by an in-app update");

            // upgrade by running the v2 MSI directly (not via in-app update)
            await PackTestAppWithMsi(id, "2.0.0", "version 2 test", releaseDir, logger, InstallLocation.PerUser, title);
            WaitUntilInstallDirUnlocked(installDir);
            logger.Info("TEST: Upgrading via v2 MSI...");
            RunMsiExec($"/i \"{msiPath}\" /qn", logger);

            var chk2version = WindowsTestHelper.RunCoveredDotnet(appPath, ["version"], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "2.0.0", chk2version);
            Assert.True(File.Exists(userDataFile), "User data outside `current` should survive an MSI upgrade");
            Assert.False(File.Exists(strayPayloadFile), "Files inside `current` should be purged by an MSI upgrade");
            var (found, displayVersion) = FindUninstallEntry(Registry.CurrentUser, id);
            Assert.True(found, "Uninstall entry should exist in HKCU after upgrade");
            Assert.Equal("2.0.0", displayVersion);

            Assert.False(File.Exists(newerPackage), "Downloaded packages should be purged by an MSI upgrade");
            Assert.True(File.Exists(betaIdFile), "Non-package files in packages/ should survive an MSI upgrade");

            // an MSI upgrade is an update from the app's point of view: --veloapp-updated, and neither the
            // install hook nor the uninstall hook (the old product is removed as part of the upgrade)
            var hookContent = File.Exists(hookFile) ? File.ReadAllText(hookFile) : "";
            Assert.Contains("OnAfterUpdateFastCallback: --veloapp-updated 2.0.0", hookContent);
            Assert.DoesNotContain("OnAfterInstallFastCallback", hookContent);
            Assert.DoesNotContain("OnBeforeUninstallFastCallback", hookContent);
            logger.Info("TEST: v2 upgrade verified, user data intact, no uninstall hook");

            // simulate a user-created shortcut pointing into the install dir; uninstall cleanup
            // should sweep it even though the MSI did not create it
            File.Copy(msiShortcut, userShortcut, true);

            // an unrelated dir named after the display title must NOT be deleted by cleanup
            Directory.CreateDirectory(titleDir);
            File.WriteAllText(Path.Combine(titleDir, "unrelated.txt"), "not velopack's data");

            // a foreign value under the ARP key: MSI only removes the values it authored, so the
            // cleanup sweep must delete the remainder of the key
            using (var arpKey = Registry.CurrentUser.OpenSubKey(
                       $@"Software\Microsoft\Windows\CurrentVersion\Uninstall\MSI:{id}", writable: true)) {
                Assert.NotNull(arpKey);
                arpKey.SetValue("ForeignValue", "written by someone else");
            }

            // something in the velopack temp dir, so the assertion below proves the cleanup removed it
            Directory.CreateDirectory(velopackTempDir);
            File.WriteAllText(Path.Combine(velopackTempDir, "leftover.txt"), "temp");

            // uninstall
            WaitUntilInstallDirUnlocked(installDir);
            logger.Info("TEST: Uninstalling v2 MSI...");
            RunMsiExec($"/x \"{msiPath}\" /qn", logger);

            Assert.True(File.Exists(hookFile), $"Uninstall hook file not found at {hookFile}");
            Assert.Contains("OnBeforeUninstallFastCallback: --veloapp-uninstall", File.ReadAllText(hookFile));
            Assert.False(Directory.Exists(installDir),
                $"Install directory (incl. user data) should have been removed: {installDir}");
            var (found2, _) = FindUninstallEntry(Registry.CurrentUser, id);
            Assert.False(found2, "Uninstall entry should be removed from HKCU after uninstall");
            Assert.False(File.Exists(msiShortcut), "MSI desktop shortcut should be removed on uninstall");
            Assert.False(File.Exists(userShortcut), "User-created shortcut into the install dir should be removed on uninstall");
            Assert.False(Directory.Exists(velopackTempDir), $"Velopack temp dir should have been removed: {velopackTempDir}");
            Assert.True(File.Exists(Path.Combine(titleDir, "unrelated.txt")),
                "Unrelated dir named after the display title must not be touched by cleanup");
            logger.Info("TEST: uninstall cleanup verified");
        } finally {
            // cleanup: uninstall MSI (best effort, may already be uninstalled)
            try {
                if (File.Exists(msiPath)) {
                    RunMsiExec($"/x \"{msiPath}\" /qn", logger, exitCode: null);
                }
            } catch {
                // best effort cleanup
            }

            foreach (var dir in new[] { installDir, hookTempDir, titleDir }) {
                try {
                    if (Directory.Exists(dir)) {
                        IoUtil.Retry(() => IoUtil.DeleteFileOrDirectoryHard(dir), 10, 1000);
                    }
                } catch {
                    // best effort cleanup
                }
            }

            foreach (var lnk in new[] { msiShortcut, userShortcut }) {
                try {
                    File.Delete(lnk);
                } catch {
                    // best effort cleanup
                }
            }
        }
    }

    [Fact]
    public async Task TestMsiInstallToCustomDirViaVelopackInstallDir()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<MsiTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var customParent);

        string id = "MsiCustomDirTest";
        // a custom install dir that is NOT the default %LocalAppData%\{id} location
        var customDir = Path.Combine(customParent, "Custom", "WLYS");
        var defaultDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), id);
        var msiPath = Path.Combine(releaseDir, $"{id}-win.msi");
        var appPath = Path.Combine(customDir, "current", "TestApp.exe");

        try {
            await PackTestAppWithMsi(id, "1.0.0", "custom dir test", releaseDir, logger, InstallLocation.PerUser);
            Assert.True(File.Exists(msiPath), $"MSI not found at {msiPath}");

            // install per-user, overriding the install dir via VELOPACK_INSTALLDIR
            logger.Info($"TEST: Installing MSI to custom dir {customDir}...");
            RunMsiExec($"/i \"{msiPath}\" /qn VELOPACK_INSTALLDIR=\"{customDir}\"", logger);

            // app must land in the custom dir, NOT the default LocalAppData location
            Assert.True(File.Exists(appPath), $"TestApp.exe not found at custom dir {appPath}");
            Assert.False(Directory.Exists(defaultDir),
                $"App should NOT have installed to the default dir {defaultDir}");

            var chkVersion = WindowsTestHelper.RunCoveredDotnet(appPath, ["version"], customDir, logger);
            Assert.EndsWith(Environment.NewLine + "1.0.0", chkVersion);
            logger.Info("TEST: app installed to custom dir and verified");
        } finally {
            try {
                if (File.Exists(msiPath)) {
                    RunMsiExec($"/x \"{msiPath}\" /qn", logger, exitCode: null);
                }
            } catch {
                // best effort cleanup
            }

            try {
                if (Directory.Exists(customDir)) {
                    IoUtil.Retry(() => IoUtil.DeleteFileOrDirectoryHard(customDir), 10, 1000);
                }
            } catch {
                // best effort cleanup
            }

            try {
                if (Directory.Exists(defaultDir)) {
                    IoUtil.Retry(() => IoUtil.DeleteFileOrDirectoryHard(defaultDir), 10, 1000);
                }
            } catch {
                // best effort cleanup
            }
        }
    }

    // Requires elevation and UAC approval. Explicit tests are skipped unless asked for; to run from CLI:
    //  dotnet test --project test/Velopack.Packaging.Tests -- --filter-method "*TestMsiPerMachineInstallAndUpdate*" --explicit only
    [Fact(Explicit = true)]
    public async Task TestMsiPerMachineInstallAndUpdate()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<MsiTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);

        string id = "MsiPerMachineTest";
        var installDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), id);
        var fallbackDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), id);
        var msiPath = Path.Combine(releaseDir, $"{id}-win.msi");
        // save v1 MSI path separately — packing v2 overwrites msiPath, and uninstalling requires
        // an MSI whose ProductCode matches the installed product (in-app updates don't change it).
        var v1MsiPath = Path.Combine(releaseDir, $"{id}-v1.msi");
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");

        try {
            // pack v1
            await PackTestAppWithMsi(id, "1.0.0", "version 1 test", releaseDir, logger, InstallLocation.Either);
            Assert.True(File.Exists(msiPath), $"MSI not found at {msiPath}");
            File.Copy(msiPath, v1MsiPath, true);

            // install via msiexec with ALLUSERS=1 (per-machine, requires admin). no INSTALLFOLDER
            // is passed on purpose: silent per-machine installs must default to Program Files (#945)
            logger.Info("TEST: Installing MSI per-machine...");
            RunMsiExec($"/i \"{msiPath}\" /qn ALLUSERS=1", logger);

            // verify install
            Assert.True(File.Exists(appPath), $"TestApp.exe not found at {appPath}");
            var chk1version = WindowsTestHelper.RunCoveredDotnet(appPath, ["version"], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "1.0.0", chk1version);
            logger.Info("TEST: v1 installed and verified");

            // verify uninstall registry is in HKLM for per-machine install
            var (hklmFound, hklmVersion) = FindUninstallEntry(Registry.LocalMachine, id);
            Assert.True(hklmFound, "Uninstall entry should exist in HKLM for per-machine MSI install");
            Assert.Equal("1.0.0", hklmVersion);
            logger.Info("TEST: registry entry verified in HKLM with version 1.0.0");

            // Run packagesdir check as a de-elevated (standard) user.
            // Program Files is not writable for standard users, so the locator should
            // fallback to %LOCALAPPDATA%/{id}/packages.
            string fallbackPackagesPath = Path.Combine(fallbackDir, "packages");
            var chk1pkgdir = RunCoveredDotnetDeelevated(appPath, ["packagesdir"], installDir, logger);
            Assert.EndsWith(Environment.NewLine + fallbackPackagesPath, chk1pkgdir);
            logger.Info("TEST: packages dir correctly fell back to " + fallbackPackagesPath);

            // pack v2
            await PackTestAppWithMsi(id, "2.0.0", "version 2 test", releaseDir, logger, InstallLocation.Either);

            // check for updates (de-elevated)
            var chk2check = RunCoveredDotnetDeelevated(appPath, ["check", releaseDir], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "update: 2.0.0", chk2check);
            logger.Info("TEST: found v2 update (de-elevated)");

            // download update as de-elevated user (nupkg should go to fallback packages dir,
            // and Update.exe should be extracted to the fallback dir)
            RunCoveredDotnetDeelevated(appPath, ["download", releaseDir], installDir, logger);

            // verify nupkg ended up in the fallback packages dir (NOT Program Files)
            var nupkgFileName = $"{id}-2.0.0-full.nupkg";
            Assert.True(File.Exists(Path.Combine(fallbackPackagesPath, nupkgFileName)),
                $"Downloaded nupkg not found in fallback packages dir: {fallbackPackagesPath}");
            Assert.False(File.Exists(Path.Combine(installDir, "packages", nupkgFileName)),
                "Nupkg should NOT be in Program Files packages dir when running as standard user");
            logger.Info("TEST: nupkg downloaded to correct fallback packages dir");

            // verify Update.exe was extracted to the fallback dir (not Program Files)
            var fallbackUpdateExe = Path.Combine(fallbackDir, "Update.exe");
            Assert.True(File.Exists(fallbackUpdateExe),
                $"Update.exe not found in fallback dir: {fallbackUpdateExe}");
            logger.Info("TEST: Update.exe extracted to fallback dir");

            // apply update as de-elevated user (Update.exe should self-elevate via UAC); the UAC
            // prompt + elevated apply + app restart happen in separate processes, so poll until the
            // new version is observable (de-elevated to confirm the update was applied)
            RunCoveredDotnetDeelevated(appPath, ["apply", releaseDir], installDir, logger, exitCode: null);
            TestHelper.WaitUntil(() => {
                var chk2version = RunCoveredDotnetDeelevated(appPath, ["version"], installDir, logger);
                Assert.EndsWith(Environment.NewLine + "2.0.0", chk2version);
            }, timeoutMs: 90_000, pollDelayMs: 2000);
            logger.Info("TEST: v2 update verified");

            // verify registry was updated to v2
            var (hklmFound2, hklmVersion2) = FindUninstallEntry(Registry.LocalMachine, id);
            Assert.True(hklmFound2, "Uninstall entry should still exist in HKLM after update");
            Assert.Equal("2.0.0", hklmVersion2);
            logger.Info("TEST: registry entry verified in HKLM with version 2.0.0");

            // uninstall: must remove the Program Files install dir, the HKLM ARP entry, AND the
            // per-user fallback dir in %LocalAppData% which holds packages + Update.exe (#989)
            WaitUntilInstallDirUnlocked(installDir);
            logger.Info("TEST: Uninstalling MSI...");
            RunMsiExec($"/x \"{v1MsiPath}\" /qn", logger);

            Assert.False(Directory.Exists(installDir), $"Install directory should have been removed: {installDir}");
            Assert.False(Directory.Exists(fallbackDir),
                $"LocalAppData fallback directory should have been removed: {fallbackDir}");
            var (hklmFound3, _) = FindUninstallEntry(Registry.LocalMachine, id);
            Assert.False(hklmFound3, "Uninstall entry should be removed from HKLM after uninstall");
            logger.Info("TEST: uninstall cleanup verified");
        } finally {
            // cleanup: uninstall MSI (best effort, may already be uninstalled)
            try {
                if (File.Exists(v1MsiPath)) {
                    RunMsiExec($"/x \"{v1MsiPath}\" /qn", logger, exitCode: null);
                }
            } catch {
                // best effort cleanup
            }

            // cleanup fallback dir
            try {
                if (Directory.Exists(fallbackDir)) {
                    IoUtil.Retry(() => IoUtil.DeleteFileOrDirectoryHard(fallbackDir), 10, 1000);
                }
            } catch {
                // best effort cleanup
            }

            // cleanup install dir if MSI uninstall left remnants
            try {
                if (Directory.Exists(installDir)) {
                    IoUtil.Retry(() => IoUtil.DeleteFileOrDirectoryHard(installDir), 10, 1000);
                }
            } catch {
                // best effort cleanup
            }
        }
    }
}
