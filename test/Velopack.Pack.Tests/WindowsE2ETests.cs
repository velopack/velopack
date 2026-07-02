using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Microsoft.Win32;
using NuGet.Packaging;
using Velopack.Core;
using Velopack.Packaging.Commands;
using Velopack.Packaging.Compression;
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
        var argsContent = File.ReadAllText(argsPath).Trim();
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

        var uninstOutput = WindowsTestHelper.RunNoCoverage(updatePath, ["--silent", "--uninstall"], Environment.CurrentDirectory, logger);
        Assert.EndsWith(Environment.NewLine + "Y", uninstOutput); // this checks that the self-delete succeeded

        Assert.False(File.Exists(startLnk));
        Assert.False(File.Exists(desktopLnk));
        Assert.False(File.Exists(appPath));

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
        string contents = File.ReadAllText(argsPath).Trim();
        Assert.Equal("OnAfterInstallFastCallback: --veloapp-install 1.0.0", contents);

        var firstRun = Path.Combine(installDir, "firstrun");
        Assert.True(File.Exists(argsPath));
        Assert.Equal("OnFirstRun: 1.0.0", File.ReadAllText(firstRun).Trim());

        // pack v2
        await WindowsPackTests.PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger);

        // install v2
        WindowsTestHelper.RunCoveredDotnet(appPath, ["download", releaseDir], installDir, logger);
        WindowsTestHelper.RunCoveredDotnet(appPath, ["apply", releaseDir], installDir, logger, exitCode: null);

        Thread.Sleep(2000);

        var logFile = WindowsTestHelper.GetLogFilePath(id);
        logger.Info("TEST: update log output - " + Environment.NewLine + File.ReadAllText(logFile));

        Assert.Contains("--veloapp-obsolete 1.0.0", File.ReadAllText(argsPath).Trim());
        Assert.Contains("--veloapp-updated 2.0.0", File.ReadAllText(argsPath).Trim());

        var restartedPath = Path.Combine(installDir, "restarted");
        Assert.True(File.Exists(restartedPath));
        Assert.Equal("OnRestarted: 2.0.0,test,args !!", File.ReadAllText(restartedPath).Trim());

        var updatePath = Path.Combine(installDir, "Update.exe");
        WindowsTestHelper.RunNoCoverage(updatePath, ["--silent", "--uninstall"], Environment.CurrentDirectory, logger);
    }
}

// Auto-update and delta-update end-to-end tests.
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

        Thread.Sleep(3000); // update.exe runs in separate process

        var chk1version = WindowsTestHelper.RunNoCoverage(appPath, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "2.0.0", chk1version);
    }

    [Fact]
    public async Task TestPackedAppCanDeltaUpdateToLatest()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        string id = "SquirrelIntegrationTest";
        string packagesPath = Path.Combine(installDir, "packages");
        if (Directory.Exists(packagesPath)) {
            Directory.Delete(packagesPath, true);
        }

        // pack v1
        await WindowsPackTests.PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger);

        // install app
        var setupPath1 = Path.Combine(releaseDir, $"{id}-win-Setup.exe");
        WindowsTestHelper.RunNoCoverage(
            setupPath1,
            ["--silent", "--installto", installDir],
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            logger);

        // check app installed correctly
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");
        Assert.True(File.Exists(appPath));
        var argsPath = Path.Combine(installDir, "args.txt");
        Assert.True(File.Exists(argsPath));
        var argsContent = File.ReadAllText(argsPath).Trim();
        Assert.Equal("OnAfterInstallFastCallback: --veloapp-install 1.0.0", argsContent);
        logger.Info("TEST: v1 installed");

        // check app output
        var chk1test = WindowsTestHelper.RunCoveredDotnet(appPath, ["test"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 1 test", chk1test);
        var chk1version = WindowsTestHelper.RunCoveredDotnet(appPath, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "1.0.0", chk1version);
        var chk1check = WindowsTestHelper.RunCoveredDotnet(appPath, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "no updates", chk1check);
        logger.Info("TEST: v1 output verified");

        // pack v2
        await WindowsPackTests.PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger);

        // check can find v2 update
        var chk2check = WindowsTestHelper.RunCoveredDotnet(appPath, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "update: 2.0.0", chk2check);
        logger.Info("TEST: found v2 update");

        // pack v3
        await WindowsPackTests.PackTestApp(id, "3.0.0", "version 3 test", releaseDir, logger);

        // corrupt v2/v3 full packages as we want to test delta's
        File.WriteAllText(Path.Combine(releaseDir, $"{id}-2.0.0-win-full.nupkg"), "nope");
        File.WriteAllText(Path.Combine(releaseDir, $"{id}-3.0.0-win-full.nupkg"), "nope");

        // perform full update, check that we get v3
        // apply should fail if there's not an update downloaded
        WindowsTestHelper.RunCoveredDotnet(appPath, ["apply", releaseDir], installDir, logger, exitCode: -1);
        WindowsTestHelper.RunCoveredDotnet(appPath, ["download", releaseDir], installDir, logger);
        WindowsTestHelper.RunCoveredDotnet(appPath, ["apply", releaseDir], installDir, logger, exitCode: null);
        logger.Info("TEST: v3 applied");

        // check app output
        var chk3test = WindowsTestHelper.RunCoveredDotnet(appPath, ["test"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 3 test", chk3test);
        var chk3version = WindowsTestHelper.RunCoveredDotnet(appPath, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "3.0.0", chk3version);
        var ch3check2 = WindowsTestHelper.RunCoveredDotnet(appPath, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "no updates", ch3check2);
        logger.Info("TEST: v3 output verified");

        // print log output
        var logPath = WindowsTestHelper.GetLogFilePath(id);
        logger.Info("TEST: log output - " + Environment.NewLine + File.ReadAllText(logPath));


        // check new obsoleted/updated hooks have run
        var argsContentv3 = File.ReadAllText(argsPath).Trim();
        Assert.Contains("--veloapp-install 1.0.0", argsContentv3);
        Assert.Contains("--veloapp-obsolete 1.0.0", argsContentv3);
        Assert.Contains("--veloapp-updated 3.0.0", argsContentv3);
        logger.Info("TEST: hooks verified");

        // uninstall
        var updatePath = Path.Combine(installDir, "Update.exe");
        WindowsTestHelper.RunNoCoverage(updatePath, ["--silent", "--uninstall"], Environment.CurrentDirectory, logger);
        logger.Info("TEST: uninstalled / complete");
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
        Thread.Sleep(8000); // update.exe will do migration here

        string logContents = WindowsTestHelper.ReadFileWithRetry(WindowsTestHelper.GetLogFilePath("LegacyTestApp"), logger);
        logger.Info("Velopack.log:" + Environment.NewLine + logContents);

        if (origDirName != "current") {
            Assert.False(Directory.Exists(currentDir));
            currentDir = Path.Combine(rootDir, "current");
        }

        Assert.True(Directory.Exists(currentDir));
        appExe = Path.Combine(currentDir, "LegacyTestApp.exe");
        Assert.True(File.Exists(appExe));

        Assert.False(Directory.EnumerateDirectories(rootDir, "app-*").Any());
        Assert.False(Directory.Exists(Path.Combine(rootDir, "staging")));

        // this is the file written by TestApp when it's detected the squirrel restart. if this is here, everything went smoothly.
        Assert.True(File.Exists(Path.Combine(rootDir, "restarted")));

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

        Thread.Sleep(10_000); // update.exe runs in a separate process here

        var logPath = WindowsTestHelper.GetLogFilePath("LegacyTestApp");
        string logContents = WindowsTestHelper.ReadFileWithRetry(logPath, logger);
        logger.Info("Velopack.log:" + Environment.NewLine + logContents);
        logger.Info("TEST: " + DateTime.Now.ToLongTimeString());

        if (origDirName != "current") {
            Assert.False(Directory.Exists(currentDir));
            currentDir = Path.Combine(rootDir, "current");
        }

        Assert.True(Directory.Exists(currentDir));
        appExe = Path.Combine(currentDir, "TestApp.exe");
        Assert.True(File.Exists(appExe));

        Assert.False(Directory.EnumerateDirectories(rootDir, "app-*").Any());
        Assert.False(Directory.Exists(Path.Combine(rootDir, "staging")));

        // this is the file written by TestApp when it's detected the squirrel restart. if this is here, everything went smoothly.
        Assert.True(File.Exists(Path.Combine(rootDir, "restarted")));

        var chk3version = WindowsTestHelper.RunNoCoverage(appExe, ["version"], currentDir, logger);
        Assert.EndsWith(Environment.NewLine + "2.0.0", chk3version);
    }
}
