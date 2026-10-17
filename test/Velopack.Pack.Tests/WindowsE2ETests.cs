using System.Diagnostics;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.Versioning;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using Microsoft.Win32;
using NuGet.Packaging;
using Velopack.Chunks;
using Velopack.Core;
using Velopack.Packaging.Windows.Commands;
using Velopack.Util;
using Velopack.Vpk;
using Velopack.Vpk.Logging;
using Velopack.Windows;

using Velopack.TestCommon;

namespace Velopack.Pack.Tests;

// These end-to-end install/update/migration tests were split out of WindowsPackTests so that the
// groups below run in parallel (xunit runs each class as its own collection). Tests within a class
// still run serially; the legacy migration tests share %LocalAppData%\LegacyTestApp so they must
// stay in the same class.

// Setup.exe install / hooks / uninstall end-to-end tests.
[SupportedOSPlatform("windows")]
public class WindowsInstallTests
{
    private readonly ITestOutputHelper _output;

    public WindowsInstallTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task PackBuildMainExeInSubfolder()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        using var _3 = TempUtil.GetTempDirectory(out var unzipDir);
        string id = "SquirrelSubfolderTest";
        string subfolder = "Folder1";

        // pack v1 with mainExe in a subfolder
        await WindowsPackTests.PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger, mainExeSubfolder: subfolder);

        // verify the nupkg has correct nuspec mainExe and file layout
        var nupkgPath = Path.Combine(releaseDir, $"{id}-1.0.0-full.nupkg");
        Assert.True(File.Exists(nupkgPath));
        EasyZip.ExtractZipToDirectory(logger.ToVelopackLogger(), nupkgPath, unzipDir);

        var nuspecPath = Path.Combine(unzipDir, $"{id}.nuspec");
        var xml = XDocument.Load(nuspecPath);
        var mainExeValue = xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("mainExe").Single().Value;
        Assert.Equal($"{subfolder}/TestApp.exe", mainExeValue);
        Assert.True(File.Exists(Path.Combine(unzipDir, "lib", "app", subfolder, "TestApp.exe")));
        logger.Info("TEST: v1 nupkg verified");

        // install app
        var setupPath = Path.Combine(releaseDir, $"{id}-win-Setup.exe");
        WindowsTestHelper.RunNoCoverage(
            setupPath,
            ["--silent", "--installto", installDir],
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            logger);

        // check app installed correctly â€” exe should be in current/subfolder/
        var appPath = Path.Combine(installDir, "current", subfolder, "TestApp.exe");
        Assert.True(File.Exists(appPath));
        logger.Info("TEST: v1 installed");

        // check app runs correctly
        var chk1version = WindowsTestHelper.RunCoveredDotnet(appPath, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "1.0.0", chk1version);
        var chk1test = WindowsTestHelper.RunCoveredDotnet(appPath, ["test"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 1 test", chk1test);
        logger.Info("TEST: v1 output verified");

        // pack v2 with mainExe in same subfolder
        await WindowsPackTests.PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger, mainExeSubfolder: subfolder);

        // check can find v2 update
        var chk2check = WindowsTestHelper.RunCoveredDotnet(appPath, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "update: 2.0.0", chk2check);
        logger.Info("TEST: found v2 update");

        // download and apply update
        WindowsTestHelper.RunCoveredDotnet(appPath, ["download", releaseDir], installDir, logger);
        WindowsTestHelper.RunCoveredDotnet(appPath, ["apply", releaseDir], installDir, logger, exitCode: null);
        logger.Info("TEST: v2 applied");

        // check v2 is running
        var chk2version = WindowsTestHelper.RunCoveredDotnet(appPath, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "2.0.0", chk2version);
        var chk2test = WindowsTestHelper.RunCoveredDotnet(appPath, ["test"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 2 test", chk2test);
        logger.Info("TEST: v2 output verified");

        // uninstall
        var updatePath = Path.Combine(installDir, "Update.exe");
        WindowsTestHelper.RunNoCoverage(updatePath, ["--silent", "--uninstall"], Environment.CurrentDirectory, logger);
        logger.Info("TEST: uninstalled / complete");
    }

    [Fact]
    public void PackBuildsPackageWhichIsInstallable()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = TempUtil.GetTempDirectory(out var tmpInstallDir);

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
            NoPortable = true
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        runner.Run(options).GetAwaiterResult();

        var setupPath1 = Path.Combine(tmpReleaseDir, $"{id}-win-Setup.exe");
        Assert.True(File.Exists(setupPath1));

        WindowsTestHelper.RunNoCoverage(setupPath1, ["--silent", "--installto", tmpInstallDir], Environment.CurrentDirectory, logger);

        var updatePath = Path.Combine(tmpInstallDir, "Update.exe");
        Assert.True(File.Exists(updatePath));

        var appPath = Path.Combine(tmpInstallDir, "current", "testapp.exe");
        Assert.True(File.Exists(appPath));

        var argsPath = Path.Combine(tmpInstallDir, "current", "args.txt");
        Assert.True(File.Exists(argsPath));
        var argsContent = TestHelper.ReadAllTextShared(argsPath).Trim();
        Assert.Equal("--veloapp-install 1.0.0", argsContent);

        void CheckShortcut(string path)
        {
            Assert.True(File.Exists(path));
            var lnk = new ShellLink(path);
            Assert.Equal(Path.Combine(tmpInstallDir, "current"), lnk.WorkingDirectory);
            Assert.Equal(appPath, lnk.Target);
        }

        var startLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", id + ".lnk");
        var desktopLnk = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), id + ".lnk");
        CheckShortcut(startLnk);
        CheckShortcut(desktopLnk);

        // check registry exists
        string installDate = null;
        string uninstallRegSubKey = @"Software\Microsoft\Windows\CurrentVersion\Uninstall";
        using (var key = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
                   .CreateSubKey(uninstallRegSubKey + "\\" + id, RegistryKeyPermissionCheck.ReadWriteSubTree)) {
            installDate = key.GetValue("InstallDate") as string;
        }

        var date = DateTime.Now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        Assert.Equal(date, installDate.Trim('\0'));

        WindowsTestHelper.RunNoCoverage(updatePath, ["--silent", "--uninstall"], Environment.CurrentDirectory, logger);

        Assert.False(File.Exists(startLnk));
        Assert.False(File.Exists(desktopLnk));
        Assert.False(File.Exists(appPath));

        // the uninstaller schedules the rmdir of its own directory ~3s after it exits; poll for it
        TestHelper.WaitUntil(() => Assert.False(Directory.Exists(tmpInstallDir)), timeoutMs: 15_000);

        using var key2 = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
            .OpenSubKey(uninstallRegSubKey + "\\" + id, RegistryKeyPermissionCheck.ReadSubTree);
        Assert.Null(key2);
    }

    [Fact]
    public async Task TestAppHooks()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        string id = "SquirrelHookTest";
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");

        // pack v1
        await WindowsPackTests.PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger);

        // install app
        var setupPath1 = Path.Combine(releaseDir, $"{id}-win-Setup.exe");
        WindowsTestHelper.RunNoCoverage(
            setupPath1,
            ["--installto", installDir],
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            logger);

        var argsPath = Path.Combine(installDir, "args.txt");
        Assert.True(File.Exists(argsPath));
        string contents = TestHelper.ReadAllTextShared(argsPath).Trim();
        Assert.Equal("OnAfterInstallFastCallback: --veloapp-install 1.0.0", contents);

        var firstRun = Path.Combine(installDir, "firstrun");
        Assert.True(File.Exists(argsPath));
        Assert.Equal("OnFirstRun: 1.0.0", TestHelper.ReadAllTextShared(firstRun).Trim());

        // pack v2
        await WindowsPackTests.PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger);

        // install v2
        WindowsTestHelper.RunCoveredDotnet(appPath, ["download", releaseDir], installDir, logger);
        WindowsTestHelper.RunCoveredDotnet(appPath, ["apply", releaseDir], installDir, logger, exitCode: null);

        // update.exe runs the hooks and relaunches the app in a separate process; the restarted
        // file is written last, so poll until the full end state is observable
        var restartedPath = Path.Combine(installDir, "restarted");
        TestHelper.WaitUntil(() => {
            Assert.Contains("--veloapp-obsolete 1.0.0", TestHelper.ReadAllTextShared(argsPath).Trim());
            Assert.Contains("--veloapp-updated 2.0.0", TestHelper.ReadAllTextShared(argsPath).Trim());
            Assert.True(File.Exists(restartedPath));
            Assert.Equal("OnRestarted: 2.0.0,test,args !!", TestHelper.ReadAllTextShared(restartedPath).Trim());
        });

        var logFile = WindowsTestHelper.GetLogFilePath(id);
        logger.Info("TEST: update log output - " + Environment.NewLine + WindowsTestHelper.ReadFileWithRetry(logFile, logger));

        var updatePath = Path.Combine(installDir, "Update.exe");
        WindowsTestHelper.RunNoCoverage(updatePath, ["--silent", "--uninstall"], Environment.CurrentDirectory, logger);
    }
}

// Auto-update and manual update end-to-end tests.
[SupportedOSPlatform("windows")]
public class WindowsUpdateTests
{
    private readonly ITestOutputHelper _output;

    public WindowsUpdateTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("rust")]
    public async Task TestAppAutoUpdatesWhenLocalIsAvailable(string variant)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        string id = $"WinAutoUpdate-{variant}";
        var exeName = variant == "rust" ? "testapp.exe" : "TestApp.exe";
        var appPath = Path.Combine(installDir, "current", exeName);

        // pack v1
        await WindowsPackTests.PackTestAppVariant(variant, id, "1.0.0", "version 1 test", releaseDir, logger);

        // install app
        var setupPath1 = Path.Combine(releaseDir, $"{id}-win-Setup.exe");
        WindowsTestHelper.RunNoCoverage(
            setupPath1,
            ["--silent", "--installto", installDir],
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            logger);

        // pack v2
        await WindowsPackTests.PackTestAppVariant(variant, id, "2.0.0", "version 2 test", releaseDir, logger);

        // move package into local packages dir (installDir is writable, so packages dir is installDir/packages)
        var fileName = $"{id}-2.0.0-full.nupkg";
        var mvFrom = Path.Combine(releaseDir, fileName);
        string packagesPath = Path.Combine(installDir, "packages");
        Directory.CreateDirectory(packagesPath);
        var mvTo = Path.Combine(packagesPath, fileName);
        File.Copy(mvFrom, mvTo, true);

        WindowsTestHelper.RunNoCoverage(appPath, ["--autoupdate"], installDir, logger, exitCode: null);

        // update.exe swaps the app in a separate process; poll until the new version is live
        TestHelper.WaitUntil(() => {
            var chk1version = WindowsTestHelper.RunNoCoverage(appPath, ["version"], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "2.0.0", chk1version);
        }, pollDelayMs: 1000);
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("rust")]
    public async Task TestPackedAppCanUpdateToLatest(string variant)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        string id = $"WinUpdateTest-{variant}";
        var exeName = variant == "rust" ? "testapp.exe" : "TestApp.exe";
        var appPath = Path.Combine(installDir, "current", exeName);
        string packagesPath = Path.Combine(installDir, "packages");
        if (Directory.Exists(packagesPath)) {
            Directory.Delete(packagesPath, true);
        }

        // the rust testapp is not coverage-instrumented
        string run(string[] args, int? exitCode = 0) => variant == "csharp"
            ? WindowsTestHelper.RunCoveredDotnet(appPath, args, installDir, logger, exitCode)
            : WindowsTestHelper.RunNoCoverage(appPath, args, installDir, logger, exitCode);

        // pack v1
        await WindowsPackTests.PackTestAppVariant(variant, id, "1.0.0", "version 1 test", releaseDir, logger);

        // install app
        var setupPath1 = Path.Combine(releaseDir, $"{id}-win-Setup.exe");
        WindowsTestHelper.RunNoCoverage(
            setupPath1,
            ["--silent", "--installto", installDir],
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            logger);

        // check app installed correctly
        Assert.True(File.Exists(appPath));
        if (variant == "csharp") {
            var argsPath = Path.Combine(installDir, "args.txt");
            Assert.True(File.Exists(argsPath));
            var argsContent = TestHelper.ReadAllTextShared(argsPath).Trim();
            Assert.Equal("OnAfterInstallFastCallback: --veloapp-install 1.0.0", argsContent);
        }

        logger.Info($"TEST ({variant}): v1 installed");

        // check app output
        var chk1test = run(["test"]);
        Assert.EndsWith(Environment.NewLine + "version 1 test", chk1test);
        var chk1version = run(["version"]);
        Assert.EndsWith(Environment.NewLine + "1.0.0", chk1version);
        var chk1check = run(["check", releaseDir]);
        Assert.EndsWith(Environment.NewLine + "no updates", chk1check);
        logger.Info($"TEST ({variant}): v1 output verified");

        // pack v2
        await WindowsPackTests.PackTestAppVariant(variant, id, "2.0.0", "version 2 test", releaseDir, logger);

        // check can find v2 update
        var chk2check = run(["check", releaseDir]);
        Assert.EndsWith(Environment.NewLine + "update: 2.0.0", chk2check);
        logger.Info($"TEST ({variant}): found v2 update");

        // pack v3
        await WindowsPackTests.PackTestAppVariant(variant, id, "3.0.0", "version 3 test", releaseDir, logger);

        // perform update, check that we get v3
        // apply should fail if there's not an update downloaded
        run(["apply", releaseDir], exitCode: -1);
        run(["download", releaseDir]);
        run(["apply", releaseDir], exitCode: null);
        logger.Info($"TEST ({variant}): v3 applied");

        // update.exe swaps the app in a separate process; poll until the new version is live
        TestHelper.WaitUntil(() => {
            var chk3version = run(["version"]);
            Assert.EndsWith(Environment.NewLine + "3.0.0", chk3version);
        }, pollDelayMs: 1000);
        var chk3test = run(["test"]);
        Assert.EndsWith(Environment.NewLine + "version 3 test", chk3test);
        var ch3check2 = run(["check", releaseDir]);
        Assert.EndsWith(Environment.NewLine + "no updates", ch3check2);
        logger.Info($"TEST ({variant}): v3 output verified");

        // print log output
        var logPath = WindowsTestHelper.GetLogFilePath(id);
        logger.Info($"TEST ({variant}): log output - " + Environment.NewLine + WindowsTestHelper.ReadFileWithRetry(logPath, logger));

        // check new obsoleted/updated hooks have run
        if (variant == "csharp") {
            var argsContentv3 = TestHelper.ReadAllTextShared(Path.Combine(installDir, "args.txt")).Trim();
            Assert.Contains("--veloapp-install 1.0.0", argsContentv3);
            Assert.Contains("--veloapp-obsolete 1.0.0", argsContentv3);
            Assert.Contains("--veloapp-updated 3.0.0", argsContentv3);
            logger.Info($"TEST ({variant}): hooks verified");
        }

        // uninstall
        var updatePath = Path.Combine(installDir, "Update.exe");
        WindowsTestHelper.RunNoCoverage(updatePath, ["--silent", "--uninstall"], Environment.CurrentDirectory, logger);
        logger.Info($"TEST ({variant}): uninstalled / complete");
    }
}

// Migration from legacy Squirrel/Clowd/old-Velopack installs.
[SupportedOSPlatform("windows")]
public class WindowsLegacyMigrationTests
{
    private readonly ITestOutputHelper _output;

    public WindowsLegacyMigrationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("LegacyTestApp-ClowdV2-Setup.exe", "app-1.0.0")]
    [InlineData("LegacyTestApp-SquirrelWinV2-Setup.exe", "app-1.0.0")]
    public async Task LegacyAppCanMigrateUsingCli(string fixture, string origDirName)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        var rootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegacyTestApp");
        if (Directory.Exists(rootDir)) {
            IoUtil.Retry(() => IoUtil.DeleteFileOrDirectoryHard(rootDir), 10, 1000);
        }

        var setup = PathHelper.GetFixture(fixture);
        // The 0.0.84-era setup.exe predates app.manifest declaring asInvoker, so Win11 24H2+ UAC
        // installer detection would demand elevation for it; RunAsInvoker disables that heuristic
        // (a no-op for the fixtures that already declare asInvoker).
        var setupPsi = new ProcessStartInfo(setup) { UseShellExecute = false };
        setupPsi.Environment["__COMPAT_LAYER"] = "RunAsInvoker";
        var p = Process.Start(setupPsi);
        p!.WaitForExit();

        var currentDir = Path.Combine(rootDir, origDirName);
        var appExe = Path.Combine(currentDir, "LegacyTestApp.exe");
        var stubExe = Path.Combine(rootDir, "LegacyTestApp.exe");
        var updateExe = Path.Combine(rootDir, "Update.exe");

        var assertAppExe = appExe;
        IoUtil.Retry(
            () => {
                Assert.True(File.Exists(assertAppExe));
                Assert.True(File.Exists(updateExe));
            },
            retries: 10,
            retryDelay: 1000);

        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        await WindowsPackTests.PackTestApp("LegacyTestApp", "2.0.0", "hello!", releaseDir, logger, assemblyNameOverride: "LegacyTestApp");

        WindowsTestHelper.RunNoCoverage(updateExe, ["--update", releaseDir], currentDir, logger, exitCode: 0);
        Thread.Sleep(2000); // update.exe does a self update after

        WindowsTestHelper.RunNoCoverage(stubExe, [], currentDir, logger, exitCode: 0);

        // update.exe performs the migration in a separate process; the "restarted" file (written by
        // the app when it detects the squirrel restart) is the last observable step, so poll for
        // the full end state instead of sleeping a fixed amount
        TestHelper.WaitUntil(
            () => {
                if (origDirName != "current") {
                    Assert.False(Directory.Exists(Path.Combine(rootDir, origDirName)));
                }

                Assert.True(Directory.Exists(Path.Combine(rootDir, "current")));
                Assert.True(File.Exists(Path.Combine(rootDir, "current", "LegacyTestApp.exe")));
                Assert.False(Directory.EnumerateDirectories(rootDir, "app-*").Any());
                Assert.False(Directory.Exists(Path.Combine(rootDir, "staging")));
                Assert.True(File.Exists(Path.Combine(rootDir, "restarted")));
            },
            timeoutMs: 60_000);
        currentDir = Path.Combine(rootDir, "current");
        appExe = Path.Combine(currentDir, "LegacyTestApp.exe");

        string logContents = WindowsTestHelper.ReadFileWithRetry(WindowsTestHelper.GetLogFilePath("LegacyTestApp"), logger);
        logger.Info("Velopack.log:" + Environment.NewLine + logContents);

        var chk3version = WindowsTestHelper.RunNoCoverage(appExe, ["version"], currentDir, logger);
        Assert.EndsWith(Environment.NewLine + "2.0.0", chk3version);
    }

    [Theory]
    [InlineData("LegacyTestApp-ClowdV2-Setup.exe", "app-1.0.0", "LegacyTestApp.exe")]
    [InlineData("LegacyTestApp-ClowdV3-Setup.exe", "current", "LegacyTestApp.exe")]
    [InlineData("LegacyTestApp-SquirrelWinV2-Setup.exe", "app-1.0.0", "LegacyTestApp.exe")]
    [InlineData("LegacyTestApp-Velopack0084-Setup.exe", "current", "LegacyTestApp.exe")]
    [InlineData("LegacyTestApp-Velopack1298-Setup.exe", "current", "TestApp.exe")]
    public async Task LegacyAppCanMigrate(string fixture, string origDirName, string initialExeName)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        var rootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegacyTestApp");
        if (Directory.Exists(rootDir)) {
            IoUtil.Retry(() => IoUtil.DeleteFileOrDirectoryHard(rootDir), 10, 1000);
        }

        var setup = PathHelper.GetFixture(fixture);
        // The 0.0.84-era setup.exe predates app.manifest declaring asInvoker, so Win11 24H2+ UAC
        // installer detection would demand elevation for it; RunAsInvoker disables that heuristic
        // (a no-op for the fixtures that already declare asInvoker).
        var setupPsi = new ProcessStartInfo(setup) { UseShellExecute = false };
        setupPsi.Environment["__COMPAT_LAYER"] = "RunAsInvoker";
        var p = Process.Start(setupPsi);
        p!.WaitForExit();

        var currentDir = Path.Combine(rootDir, origDirName);
        var appExe = Path.Combine(currentDir, initialExeName);
        var updateExe = Path.Combine(rootDir, "Update.exe");

        var assertAppExe = appExe;
        IoUtil.Retry(
            () => {
                Assert.True(File.Exists(assertAppExe));
                Assert.True(File.Exists(updateExe));
            },
            retries: 10,
            retryDelay: 1000);

        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        await WindowsPackTests.PackTestApp("LegacyTestApp", "2.0.0", "hello!", releaseDir, logger);

        WindowsTestHelper.RunNoCoverage(appExe, ["download", releaseDir], currentDir, logger, exitCode: 0);
        WindowsTestHelper.RunNoCoverage(appExe, ["apply", releaseDir], currentDir, logger, exitCode: null);

        logger.Info("TEST: " + DateTime.Now.ToLongTimeString());

        // update.exe performs the migration in a separate process; the "restarted" file (written by
        // the app when it detects the squirrel restart) is the last observable step, so poll for
        // the full end state instead of sleeping a fixed amount
        TestHelper.WaitUntil(
            () => {
                if (origDirName != "current") {
                    Assert.False(Directory.Exists(Path.Combine(rootDir, origDirName)));
                }

                Assert.True(Directory.Exists(Path.Combine(rootDir, "current")));
                Assert.True(File.Exists(Path.Combine(rootDir, "current", "TestApp.exe")));
                Assert.False(Directory.EnumerateDirectories(rootDir, "app-*").Any());
                Assert.False(Directory.Exists(Path.Combine(rootDir, "staging")));
                Assert.True(File.Exists(Path.Combine(rootDir, "restarted")));
            },
            timeoutMs: 60_000);
        currentDir = Path.Combine(rootDir, "current");
        appExe = Path.Combine(currentDir, "TestApp.exe");

        var logPath = WindowsTestHelper.GetLogFilePath("LegacyTestApp");
        string logContents = WindowsTestHelper.ReadFileWithRetry(logPath, logger);
        logger.Info("Velopack.log:" + Environment.NewLine + logContents);
        logger.Info("TEST: " + DateTime.Now.ToLongTimeString());

        var chk3version = WindowsTestHelper.RunNoCoverage(appExe, ["version"], currentDir, logger);
        Assert.EndsWith(Environment.NewLine + "2.0.0", chk3version);
    }
}

// Shared helpers for the delta v2 (chunk update) end-to-end tests below (spec §12.5).
[SupportedOSPlatform("windows")]
internal static class ChunkE2E
{
    public const string CSharp = "csharp";
    public const string CSharpNet48 = "csharp-net48";
    public const string Rust = "rust";

    public static string ExeName(string variant) => variant == Rust ? "testapp.exe" : "TestApp.exe";

    public static string AppPath(string variant, string installDir) => Path.Combine(installDir, "current", ExeName(variant));

    public static void Pack(string variant, string id, string version, string testString, string releaseDir, ILogger logger,
        Action<string> preparePublishDir = null)
    {
        switch (variant) {
        case CSharp:
            TestApp.PackTestApp(id, version, testString, releaseDir, logger, targetRid: RID.Parse("win-x64"), preparePublishDir: preparePublishDir);
            break;
        case CSharpNet48:
            TestApp.PackTestApp(
                id, version, testString, releaseDir, logger, targetRid: RID.Parse("win-x64"), framework: TestApp.FrameworkNet48,
                preparePublishDir: preparePublishDir);
            break;
        case Rust:
            Assert.Null(preparePublishDir);
            TestApp.PackRustTestAppWindows(id, version, testString, releaseDir, logger);
            break;
        default:
            throw new ArgumentException($"Unknown variant: {variant}");
        }
    }

    /// <summary> Runs the installed app. Only the net8 TestApp is coverage-instrumented. </summary>
    public static string Run(string variant, string installDir, string[] args, ILogger logger, int? exitCode = 0)
    {
        var appPath = AppPath(variant, installDir);
        return variant == CSharp
            ? WindowsTestHelper.RunCoveredDotnet(appPath, args, installDir, logger, exitCode)
            : WindowsTestHelper.RunNoCoverage(appPath, args, installDir, logger, exitCode);
    }

    public static void Install(string id, string releaseDir, string installDir, ILogger logger)
    {
        var setupPath = Path.Combine(releaseDir, $"{id}-win-Setup.exe");
        WindowsTestHelper.RunNoCoverage(
            setupPath,
            ["--silent", "--installto", installDir],
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            logger);
    }

    public static void Uninstall(string installDir, ILogger logger)
    {
        var updatePath = Path.Combine(installDir, "Update.exe");
        WindowsTestHelper.RunNoCoverage(updatePath, ["--silent", "--uninstall"], Environment.CurrentDirectory, logger);
    }

    public static void WaitForVersion(string variant, string installDir, string version, ILogger logger)
    {
        // update.exe swaps the app in a separate process; poll until the new version is live
        TestHelper.WaitUntil(
            () => {
                var output = Run(variant, installDir, ["version"], logger);
                Assert.EndsWith(Environment.NewLine + version, output);
            },
            timeoutMs: 60_000,
            pollDelayMs: 1000);
    }

    /// <summary>
    /// Replaces the SHA1/SHA256 of every Full asset in <c>releases.{channel}.json</c> with zeros: a classic full download then fails
    /// its checksum, so a successful download proves the chunk path (verified by the index digest and per-chunk hashes) ran.
    /// </summary>
    public static void CorruptFullAssetChecksums(string releaseDir, string channel = "win")
    {
        var path = Path.Combine(releaseDir, $"releases.{channel}.json");
        var root = JsonNode.Parse(File.ReadAllText(path))!;
        int corrupted = 0;
        foreach (var asset in root["Assets"]!.AsArray()) {
            if (asset!["Type"]!.ToString() != "Full") continue;
            asset["SHA1"] = new string('0', 40);
            asset["SHA256"] = new string('0', 64);
            corrupted++;
        }
        Assert.True(corrupted > 0, "no Full assets found in " + path);
        File.WriteAllText(path, root.ToJsonString());
    }

    public static VelopackAsset GetFeedAsset(string releaseDir, string version, string channel = "win")
    {
        var feed = VelopackAssetFeed.FromJson(File.ReadAllText(Path.Combine(releaseDir, $"releases.{channel}.json")));
        var asset = feed.Assets.Single(a => a.Type == VelopackAssetType.Full && a.Version.ToString() == version);
        Assert.True(asset.HasChunkIndex, $"the {version} feed asset carries no chunk index pointer");
        return asset;
    }

    public static string[] PackagesInDir(string packagesDir) =>
        Directory.Exists(packagesDir) ? Directory.GetFiles(packagesDir, "*.nupkg") : [];

    public record ReadyStaged(string Dir, string StagedId, string IndexSHA256, string Version, string FileName);

    /// <summary> Every <c>packages/staged/*/ready.json</c>, parsed. </summary>
    public static ReadyStaged[] FindReadyStaged(string packagesDir)
    {
        var stagedRoot = Path.Combine(packagesDir, "staged");
        if (!Directory.Exists(stagedRoot)) return [];
        var result = new List<ReadyStaged>();
        foreach (var dir in Directory.GetDirectories(stagedRoot)) {
            var readyPath = Path.Combine(dir, "ready.json");
            if (!File.Exists(readyPath)) continue;
            var json = JsonNode.Parse(File.ReadAllText(readyPath))!;
            result.Add(
                new ReadyStaged(
                    dir,
                    json["StagedId"]!.ToString(),
                    json["IndexSHA256"]!.ToString(),
                    json["Asset"]!["Version"]!.ToString(),
                    json["Asset"]!["FileName"]!.ToString()));
        }
        return result.ToArray();
    }

    /// <summary>
    /// Asserts the download left exactly one ready staged tree for <paramref name="target"/> (matched by its index digest) and no
    /// package.
    /// </summary>
    public static ReadyStaged AssertStagedFor(string packagesDir, VelopackAsset target)
    {
        var packages = PackagesInDir(packagesDir);
        Assert.True(packages.Length == 0, "a chunk download must not leave any .nupkg behind: " + string.Join(", ", packages));
        var ready = Assert.Single(FindReadyStaged(packagesDir));
        Assert.Equal(target.Version.ToString(), ready.Version);
        Assert.Equal(target.FileName, ready.FileName);
        Assert.Equal(target.ChunkIndexSHA256, ready.IndexSHA256, ignoreCase: true);
        Assert.Equal(ChunkIndex.StagedIdOf(target.ChunkIndexSHA256), ready.StagedId);
        Assert.True(Directory.Exists(Path.Combine(ready.Dir, "current")), "staged tree has no current/ dir");
        return ready;
    }

    /// <summary> After apply-staged: the staged dirs, the swap backup and every package are gone. </summary>
    public static void AssertAppliedAndCleanedUp(string installDir)
    {
        var packagesDir = Path.Combine(installDir, "packages");
        TestHelper.WaitUntil(
            () => {
                Assert.Empty(PackagesInDir(packagesDir));
                var stagedRoot = Path.Combine(packagesDir, "staged");
                Assert.True(
                    !Directory.Exists(stagedRoot) || !Directory.EnumerateFileSystemEntries(stagedRoot).Any(),
                    "staged dirs left behind after apply");
                Assert.Empty(Directory.GetDirectories(installDir, "current.old-*"));
                Assert.Empty(Directory.GetDirectories(installDir, "current.new-*"));
            });
    }

    /// <summary> The installed content map equals the target package's <c>lib/app/sq.chunks</c> and parses. </summary>
    public static void AssertInstalledSqChunksMatchPackage(string installDir, string nupkgPath)
    {
        var installed = Path.Combine(installDir, "current", ChunkIndex.LocalFileName);
        Assert.True(File.Exists(installed), "current/sq.chunks is missing");
        using var zip = ZipFile.OpenRead(nupkgPath);
        using var s = zip.GetEntry("lib/app/" + ChunkIndex.LocalFileName)!.Open();
        var ms = new MemoryStream();
        s.CopyTo(ms);
        var installedBytes = File.ReadAllBytes(installed);
        Assert.Equal(ms.ToArray(), installedBytes);
        Assert.NotNull(ChunkIndex.ParseLocal(installedBytes));
    }

    /// <summary> Sum of the byte ranges requested for <c>.nupkg</c> files; asserts every such request was ranged. </summary>
    public static long AssertNupkgRequestsRangedOnly(StaticFileServer server)
    {
        var nupkgRequests = server.Requests.Where(r => r.Path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)).ToArray();
        Assert.NotEmpty(nupkgRequests);
        long total = 0;
        foreach (var r in nupkgRequests) {
            Assert.True(r.IsRanged, $"package request without a Range header: {r.PathAndQuery}");
            total += RangeLength(r.Range);
        }
        return total;
    }

    public static long RangeLength(string range)
    {
        Assert.StartsWith("bytes=", range);
        var parts = range.Substring(6).Split('-');
        Assert.Equal(2, parts.Length);
        return long.Parse(parts[1], CultureInfo.InvariantCulture) - long.Parse(parts[0], CultureInfo.InvariantCulture) + 1;
    }

    /// <summary> The "progress: N" lines printed by TestApp's download command. </summary>
    public static int[] ParseProgress(string output) =>
        output.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("progress: ", StringComparison.Ordinal))
            .Select(l => int.Parse(l.Substring("progress: ".Length), CultureInfo.InvariantCulture))
            .ToArray();
}

// Delta v2 chunk updates through a local file source, for every client flavour.
[SupportedOSPlatform("windows")]
public class WindowsChunkUpdateTests
{
    private readonly ITestOutputHelper _output;

    public WindowsChunkUpdateTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData(ChunkE2E.CSharp)]
    [InlineData(ChunkE2E.CSharpNet48)]
    [InlineData(ChunkE2E.Rust)]
    public void TestPackedAppCanChunkUpdateToLatest(string variant)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsChunkUpdateTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        string id = $"WinChunkUpdate-{variant}";
        string packagesDir = Path.Combine(installDir, "packages");
        string run(string[] args, int? exitCode = 0) => ChunkE2E.Run(variant, installDir, args, logger, exitCode);

        // pack + install v1: a v2-packed install carries current/sq.chunks, which makes it eligible for the chunk path
        ChunkE2E.Pack(variant, id, "1.0.0", "version 1 test", releaseDir, logger);
        ChunkE2E.Install(id, releaseDir, installDir, logger);
        Assert.True(File.Exists(Path.Combine(installDir, "current", ChunkIndex.LocalFileName)), "v1 install has no current/sq.chunks");
        Assert.EndsWith(Environment.NewLine + "1.0.0", run(["version"]));
        logger.Info($"TEST ({variant}): v1 installed");

        // pack v2 and v3; the update must skip v2 and land on v3. A classic fallback would fail the corrupted checksums.
        ChunkE2E.Pack(variant, id, "2.0.0", "version 2 test", releaseDir, logger);
        ChunkE2E.Pack(variant, id, "3.0.0", "version 3 test", releaseDir, logger);
        ChunkE2E.CorruptFullAssetChecksums(releaseDir);
        var v3 = ChunkE2E.GetFeedAsset(releaseDir, "3.0.0");

        Assert.EndsWith(Environment.NewLine + "update: 3.0.0", run(["check", releaseDir]));
        run(["download", releaseDir]);
        ChunkE2E.AssertStagedFor(packagesDir, v3);
        logger.Info($"TEST ({variant}): v3 staged by the chunk path");

        // UpdatePendingRestart returns the staged asset, pointer included (the rust testapp has no such command; the
        // ready marker it reads was checked above)
        if (variant != ChunkE2E.Rust) {
            var pending = run(["pending", releaseDir]);
            Assert.EndsWith(Environment.NewLine + $"pending: 3.0.0 {v3.FileName} {v3.ChunkIndexSHA256}", pending);
        }

        run(["apply", releaseDir], exitCode: null);
        ChunkE2E.WaitForVersion(variant, installDir, "3.0.0", logger);
        Assert.EndsWith(Environment.NewLine + "version 3 test", run(["test"]));
        logger.Info($"TEST ({variant}): v3 applied");

        // hooks: the C# TestApp logs them to {root}/args.txt; the rust testapp appends its args to args.txt in its working
        // dir, which for hooks is current/ (so only hooks run by the new tree survive the swap)
        TestHelper.WaitUntil(() => {
            if (variant == ChunkE2E.Rust) {
                var args = TestHelper.ReadAllTextShared(Path.Combine(installDir, "current", "args.txt"));
                Assert.Contains("--veloapp-updated 3.0.0", args);
            } else {
                var args = TestHelper.ReadAllTextShared(Path.Combine(installDir, "args.txt"));
                Assert.Contains("--veloapp-install 1.0.0", args);
                Assert.Contains("--veloapp-obsolete 1.0.0", args);
                Assert.Contains("--veloapp-updated 3.0.0", args);
                Assert.DoesNotContain("--veloapp-updated 2.0.0", args);
                var restarted = TestHelper.ReadAllTextShared(Path.Combine(installDir, "restarted")).Trim();
                Assert.Equal("OnRestarted: 3.0.0,test,args !!", restarted);
            }
        });

        ChunkE2E.AssertInstalledSqChunksMatchPackage(installDir, Path.Combine(releaseDir, $"{id}-3.0.0-full.nupkg"));
        ChunkE2E.AssertAppliedAndCleanedUp(installDir);
        Assert.EndsWith(Environment.NewLine + "no updates", run(["check", releaseDir]));

        logger.Info(
            $"TEST ({variant}): update log - " + Environment.NewLine +
            WindowsTestHelper.ReadFileWithRetry(WindowsTestHelper.GetLogFilePath(id), logger));
        ChunkE2E.Uninstall(installDir, logger);
    }

    [Theory]
    [InlineData(ChunkE2E.CSharp)]
    [InlineData(ChunkE2E.Rust)]
    public void TestAutoUpdateAppliesStagedTree(string variant)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsChunkUpdateTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        string id = $"WinChunkAutoUpdate-{variant}";
        string packagesDir = Path.Combine(installDir, "packages");

        ChunkE2E.Pack(variant, id, "1.0.0", "version 1 test", releaseDir, logger);
        ChunkE2E.Install(id, releaseDir, installDir, logger);
        ChunkE2E.Pack(variant, id, "2.0.0", "version 2 test", releaseDir, logger);
        ChunkE2E.CorruptFullAssetChecksums(releaseDir);
        var v2 = ChunkE2E.GetFeedAsset(releaseDir, "2.0.0");

        // stage via download, then a plain relaunch with auto-apply picks the staged tree up (no package exists)
        ChunkE2E.Run(variant, installDir, ["download", releaseDir], logger);
        ChunkE2E.AssertStagedFor(packagesDir, v2);

        WindowsTestHelper.RunNoCoverage(ChunkE2E.AppPath(variant, installDir), ["--autoupdate"], installDir, logger, exitCode: null);
        ChunkE2E.WaitForVersion(variant, installDir, "2.0.0", logger);
        Assert.EndsWith(Environment.NewLine + "version 2 test", ChunkE2E.Run(variant, installDir, ["test"], logger));
        ChunkE2E.AssertAppliedAndCleanedUp(installDir);
        ChunkE2E.Uninstall(installDir, logger);
    }
}

// Delta v2 chunk updates over HTTP (StaticFileServer): ranged transport, fault tolerance, large payloads, healing, fallback.
[SupportedOSPlatform("windows")]
public class WindowsChunkUpdateHttpTests
{
    private readonly ITestOutputHelper _output;

    public WindowsChunkUpdateHttpTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void TestChunkUpdateOverHttpUsesRangesOnly()
    {
        RunHttpChunkUpdate("WinChunkHttp", withFaults: false);
    }

    [Fact]
    public void TestChunkUpdateOverHttpUsesRangesOnlyWithFaultPlanRetries()
    {
        RunHttpChunkUpdate("WinChunkHttpFaults", withFaults: true);
    }

    private void RunHttpChunkUpdate(string id, bool withFaults)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsChunkUpdateHttpTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        const string variant = ChunkE2E.CSharp;
        string packagesDir = Path.Combine(installDir, "packages");

        ChunkE2E.Pack(variant, id, "1.0.0", "version 1 test", releaseDir, logger);
        ChunkE2E.Install(id, releaseDir, installDir, logger);
        ChunkE2E.Pack(variant, id, "2.0.0", "version 2 test", releaseDir, logger);
        ChunkE2E.CorruptFullAssetChecksums(releaseDir);
        var v2 = ChunkE2E.GetFeedAsset(releaseDir, "2.0.0");

        using var server = new StaticFileServer(releaseDir, logger);
        if (withFaults) {
            // two transient faults in front of each of the first ranged requests (index and chunk ranges alike), all of them
            // survivable: 5xx / 429 statuses, connection resets and short bodies. Retry-After 0 keeps the run fast.
            Fault[] transient = [
                Fault.Status(503, "0"), Fault.ResetAfter(32), Fault.Status(500), Fault.ShortBody(8),
                Fault.Status(429, "0"), Fault.Status(502), Fault.Status(504), Fault.ResetAfter(0),
            ];
            server.Faults.AddRule((_, n) => n <= 12 && n % 3 != 0 ? transient[(n - 1 - (n - 1) / 3) % transient.Length] : null);
        }

        var output = ChunkE2E.Run(variant, installDir, ["download", server.BaseUrl], logger);
        ChunkE2E.AssertStagedFor(packagesDir, v2);
        Assert.DoesNotContain("falling back to a full download", output);

        // the TestApp binaries are identical across versions, so only a sliver of the package goes over the wire
        long requested = ChunkE2E.AssertNupkgRequestsRangedOnly(server);
        logger.Info(
            $"TEST: {requested} package bytes requested of {v2.Size}; {server.BodyBytesSent} body bytes sent; " +
            $"{server.Faults.Applied} faults applied");
        if (withFaults) {
            Assert.True(server.Faults.Applied >= 4, $"expected at least 4 injected faults, got {server.Faults.Applied}");
        }
        Assert.True(server.BodyBytesSent < v2.Size / 10, $"{server.BodyBytesSent} bytes sent for a {v2.Size} byte package");

        ChunkE2E.Run(variant, installDir, ["apply", server.BaseUrl], logger, exitCode: null);
        ChunkE2E.WaitForVersion(variant, installDir, "2.0.0", logger);
        Assert.EndsWith(Environment.NewLine + "version 2 test", ChunkE2E.Run(variant, installDir, ["test"], logger));
        ChunkE2E.AssertAppliedAndCleanedUp(installDir);
        ChunkE2E.Uninstall(installDir, logger);
    }

    [Fact]
    public void TestChunkUpdateLargeModifiedPayload()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsChunkUpdateHttpTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        using var _3 = TempUtil.GetTempDirectory(out var scratchDir);
        const string variant = ChunkE2E.CSharp;
        const string id = "WinChunkLargePayload";
        const string payloadName = "payload.bin";
        const int payloadLength = 20 * 1024 * 1024;
        const int insertAt = 5 * 1024 * 1024;
        const int seed = 20261017;
        string packagesDir = Path.Combine(installDir, "packages");

        // v1: 20 MiB of deterministic random bytes; v2: the same with 1 KiB inserted at 5 MiB (shifts the remaining 15 MiB)
        ChunkE2E.Pack(
            variant, id, "1.0.0", "version 1 test", releaseDir, logger,
            dir => TestApp.WriteLargePayload(Path.Combine(dir, payloadName), payloadLength, seed));
        ChunkE2E.Install(id, releaseDir, installDir, logger);
        ChunkE2E.Pack(
            variant, id, "2.0.0", "version 2 test", releaseDir, logger,
            dir => TestApp.WriteLargePayload(Path.Combine(dir, payloadName), payloadLength, seed, insertAt, 1024));
        ChunkE2E.CorruptFullAssetChecksums(releaseDir);
        var v2 = ChunkE2E.GetFeedAsset(releaseDir, "2.0.0");

        using var server = new StaticFileServer(releaseDir, logger);
        ChunkE2E.Run(variant, installDir, ["download", server.BaseUrl], logger);
        ChunkE2E.AssertStagedFor(packagesDir, v2);

        long requested = ChunkE2E.AssertNupkgRequestsRangedOnly(server);
        logger.Info($"TEST: {requested} package bytes requested of {v2.Size}; {server.BodyBytesSent} body bytes sent");
        Assert.True(requested < 1024 * 1024, $"{requested} package bytes were fetched for a 1 KiB insertion (expected < 1 MiB)");

        ChunkE2E.Run(variant, installDir, ["apply", server.BaseUrl], logger, exitCode: null);
        ChunkE2E.WaitForVersion(variant, installDir, "2.0.0", logger);
        var expectedPayload = Path.Combine(scratchDir, payloadName);
        TestApp.WriteLargePayload(expectedPayload, payloadLength, seed, insertAt, 1024);
        Assert.Equal(
            ChunkInflate.Sha256File(expectedPayload),
            ChunkInflate.Sha256File(Path.Combine(installDir, "current", payloadName)));
        ChunkE2E.AssertAppliedAndCleanedUp(installDir);
        ChunkE2E.Uninstall(installDir, logger);
    }

    [Fact]
    public void TestChunkUpdateHealsLocallyModifiedFiles()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsChunkUpdateHttpTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        const string variant = ChunkE2E.CSharp;
        const string id = "WinChunkHeal";
        string packagesDir = Path.Combine(installDir, "packages");

        ChunkE2E.Pack(variant, id, "1.0.0", "version 1 test", releaseDir, logger);
        ChunkE2E.Install(id, releaseDir, installDir, logger);
        ChunkE2E.Pack(variant, id, "2.0.0", "version 2 test", releaseDir, logger);
        ChunkE2E.CorruptFullAssetChecksums(releaseDir);
        var v2 = ChunkE2E.GetFeedAsset(releaseDir, "2.0.0");

        // retag the install's channel the way the installer channel tag does (patch_installed_channel), and serve that feed too
        var sqVersion = Path.Combine(installDir, "current", "sq.version");
        var manifest = File.ReadAllText(sqVersion);
        Assert.Contains("<channel>win</channel>", manifest);
        File.WriteAllText(sqVersion, manifest.Replace("<channel>win</channel>", "<channel>beta</channel>"));
        File.Copy(Path.Combine(releaseDir, "releases.win.json"), Path.Combine(releaseDir, "releases.beta.json"));

        // modify a large (> 64 KiB, so not hashed while planning) DLL in place with the same length: only assembly can notice.
        // The last byte is section alignment padding, so the app still loads it.
        var dll = Path.Combine(installDir, "current", "Velopack.dll");
        var dllBytes = File.ReadAllBytes(dll);
        Assert.True(dllBytes.Length > 64 * 1024);
        Assert.Equal(0, dllBytes[^1]);
        var originalDll = (byte[]) dllBytes.Clone();
        dllBytes[^1] = 0xFF;
        File.WriteAllBytes(dll, dllBytes);

        using var server = new StaticFileServer(releaseDir, logger);
        var output = ChunkE2E.Run(variant, installDir, ["download", server.BaseUrl], logger);
        ChunkE2E.AssertStagedFor(packagesDir, v2);
        Assert.Contains("missing chunks; fetching them", output);
        Assert.DoesNotContain("falling back to a full download", output);
        ChunkE2E.AssertNupkgRequestsRangedOnly(server);
        Assert.Contains(server.Requests, r => r.Path.EndsWith("releases.beta.json", StringComparison.Ordinal));

        ChunkE2E.Run(variant, installDir, ["apply", server.BaseUrl], logger, exitCode: null);
        ChunkE2E.WaitForVersion(variant, installDir, "2.0.0", logger);
        Assert.Equal(originalDll, File.ReadAllBytes(dll));
        Assert.Contains("<channel>win</channel>", File.ReadAllText(sqVersion));
        ChunkE2E.AssertAppliedAndCleanedUp(installDir);
        ChunkE2E.Uninstall(installDir, logger);
    }

    [Fact]
    public void TestChunkUpdateFallsBackWhenServerIgnoresRange()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsChunkUpdateHttpTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        const string variant = ChunkE2E.CSharp;
        const string id = "WinChunkIgnoreRange";
        string packagesDir = Path.Combine(installDir, "packages");

        ChunkE2E.Pack(variant, id, "1.0.0", "version 1 test", releaseDir, logger);
        ChunkE2E.Install(id, releaseDir, installDir, logger);
        ChunkE2E.Pack(variant, id, "2.0.0", "version 2 test", releaseDir, logger);
        var v2 = ChunkE2E.GetFeedAsset(releaseDir, "2.0.0");

        // every ranged request is answered 200 + the whole file: fatal for the chunk path, the classic download must finish
        using var server = new StaticFileServer(releaseDir, logger);
        server.Faults.AddRule((_, _) => Fault.IgnoreRange());

        var output = ChunkE2E.Run(variant, installDir, ["download", server.BaseUrl], logger);
        var progress = ChunkE2E.ParseProgress(output);
        logger.Info("TEST: progress " + string.Join(",", progress));
        Assert.NotEmpty(progress);
        for (int i = 1; i < progress.Length; i++) {
            Assert.True(progress[i] >= progress[i - 1], $"progress went backwards: {string.Join(",", progress)}");
        }
        Assert.Equal(100, progress[^1]);

        Assert.Contains(server.Requests, r => r.Path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && r.IsRanged);
        Assert.Contains(server.Requests, r => r.Path.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase) && !r.IsRanged);
        Assert.Equal([Path.Combine(packagesDir, v2.FileName)], ChunkE2E.PackagesInDir(packagesDir));
        Assert.Empty(ChunkE2E.FindReadyStaged(packagesDir));

        ChunkE2E.Run(variant, installDir, ["apply", server.BaseUrl], logger, exitCode: null);
        ChunkE2E.WaitForVersion(variant, installDir, "2.0.0", logger);
        Assert.EndsWith(Environment.NewLine + "version 2 test", ChunkE2E.Run(variant, installDir, ["test"], logger));
        ChunkE2E.Uninstall(installDir, logger);
    }
}
