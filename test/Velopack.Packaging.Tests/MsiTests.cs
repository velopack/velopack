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
        string releaseDir, ILogger logger, InstallLocation instLocation)
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
                TargetRuntime = RID.Parse("win-x64"),
                PackDirectory = publishDir,
                BuildMsi = true,
                InstLocation = instLocation,
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

    // Requires elevation and UAC approval. To run from CLI:
    //   dotnet test test/Velopack.Packaging.Tests --filter TestMsiPerMachineInstallAndUpdate -- xUnit.Explicit=only
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
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");

        try {
            // pack v1
            await PackTestAppWithMsi(id, "1.0.0", "version 1 test", releaseDir, logger, InstallLocation.Either);
            Assert.True(File.Exists(msiPath), $"MSI not found at {msiPath}");

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
        } finally {
            // cleanup: uninstall MSI
            try {
                RunMsiExec($"/x \"{msiPath}\" /qn", logger, exitCode: null);
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
