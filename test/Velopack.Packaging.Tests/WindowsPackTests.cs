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

namespace Velopack.Packaging.Tests;

[SupportedOSPlatform("windows")]
public class WindowsPackTests
{
    private readonly ITestOutputHelper _output;

    public WindowsPackTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void PackBuildValidPackageMostOptions()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = TempUtil.GetTempDirectory(out var unzipDir);

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
            TargetRuntime = RID.Parse("win10.0.19043-x64"),
            Runtimes = "net6",
            PackAuthors = "author",
            PackTitle = "Test Squirrel App",
            PackDirectory = tmpOutput,
            Channel = "asd123",
            Exclude = @".*\.pdb",
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        runner.Run(options).GetAwaiterResult();

        var nupkgPath = Path.Combine(tmpReleaseDir, $"{id}-{version}-asd123-full.nupkg");
        Assert.True(File.Exists(nupkgPath));

        var setupPath = Path.Combine(tmpReleaseDir, $"{id}-asd123-Setup.exe");
        Assert.True(File.Exists(setupPath));

        //var releasesPath = Path.Combine(tmpReleaseDir, $"RELEASES-asd123");
        //Assert.True(File.Exists(releasesPath));
        var releasesPath2 = Path.Combine(tmpReleaseDir, $"releases.asd123.json");
        Assert.True(File.Exists(releasesPath2));

        EasyZip.ExtractZipToDirectory(logger.ToVelopackLogger(), nupkgPath, unzipDir);

        // does nuspec exist and is it valid
        var nuspecPath = Path.Combine(unzipDir, $"{id}.nuspec");
        Assert.True(File.Exists(nuspecPath));
        var xml = XDocument.Load(nuspecPath);

        Assert.Equal(id, xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("id").Single().Value);
        Assert.Equal(version, xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("version").Single().Value);
        Assert.Equal(exe, xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("mainExe").Single().Value);
        Assert.Equal("Test Squirrel App", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("title").Single().Value);
        Assert.Equal("author", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("authors").Single().Value);
        Assert.Equal("x64", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("machineArchitecture").Single().Value);
        Assert.Equal("net6-x64-desktop", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("runtimeDependencies").Single().Value);
        Assert.Equal("win", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("os").Single().Value);
        Assert.Equal("10.0.19043", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("osMinVersion").Single().Value);

        // check for other files
        Assert.True(File.Exists(Path.Combine(unzipDir, "lib", "app", Path.GetFileName(exe))));
        Assert.False(File.Exists(Path.Combine(unzipDir, "lib", "app", Path.GetFileName(pdb))));
    }

    [Fact]
    public void PackBuildRefuseSameVersion()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

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
            PackDirectory = tmpOutput,
            TargetRuntime = RID.Parse("win"),
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        runner.Run(options).GetAwaiterResult();

        Assert.Throws<UserInfoException>(() => runner.Run(options).GetAwaiterResult());
    }

    [Fact]
    public void PackBuildRefuseChannelMultipleRids()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

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
            TargetRuntime = RID.Parse("win10.0.19043-x64"),
            Runtimes = "net6",
            PackAuthors = "author",
            PackTitle = "Test Squirrel App",
            PackDirectory = tmpOutput,
            Channel = "hello",
        };

        var runner = WindowsTestHelper.GetPackRunner(logger);
        runner.Run(options).GetAwaiterResult();

        options.TargetRuntime = RID.Parse("win10.0.19043-x86");
        Assert.Throws<UserInfoException>(() => runner.Run(options).GetAwaiterResult());
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
    public async Task TestAppAutoUpdatesWhenLocalIsAvailable()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        string id = "SquirrelAutoUpdateTest";
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");

        // pack v1
        await PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger);

        // install app
        var setupPath1 = Path.Combine(releaseDir, $"{id}-win-Setup.exe");
        WindowsTestHelper.RunNoCoverage(
            setupPath1,
            ["--silent", "--installto", installDir],
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            logger);

        // pack v2
        await PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger);

        // move package into local packages dir (installDir is writable, so packages dir is installDir/packages)
        var fileName = $"{id}-2.0.0-full.nupkg";
        var mvFrom = Path.Combine(releaseDir, fileName);
        string packagesPath = Path.Combine(installDir, "packages");
        Directory.CreateDirectory(packagesPath);
        var mvTo = Path.Combine(packagesPath, fileName);
        File.Copy(mvFrom, mvTo, true);

        WindowsTestHelper.RunCoveredDotnet(appPath, ["--autoupdate"], installDir, logger, exitCode: null);

        Thread.Sleep(3000); // update.exe runs in separate process

        var chk1version = WindowsTestHelper.RunCoveredDotnet(appPath, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "2.0.0", chk1version);
    }

    [Fact]
    public async Task TestPackGeneratesValidDelta()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        string id = "SquirrelDeltaTest";
        await PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger);
        await PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger, true);
        await PackTestApp(id, "3.0.0", "version 3 test", releaseDir, logger);

        // did a zsdiff get created for our v2 update?
        var deltaPath = Path.Combine(releaseDir, $"{id}-2.0.0-delta.nupkg");
        Assert.True(File.Exists(deltaPath));
        using var _2 = TempUtil.GetTempDirectory(out var extractDir);
        EasyZip.ExtractZipToDirectory(logger.ToVelopackLogger(), deltaPath, extractDir);
        var extractDllDiff = Path.Combine(extractDir, "lib", "app", "testapp.dll.zsdiff");
        var extractDllShasum = Path.Combine(extractDir, "lib", "app", "testapp.dll.shasum");
        Assert.True(File.Exists(extractDllDiff));
        Assert.True(new FileInfo(extractDllDiff).Length > 0);
        Assert.True(File.Exists(extractDllShasum));
        Assert.True(new FileInfo(extractDllShasum).Length > 0);

        var extractAppDiff = Path.Combine(extractDir, "lib", "app", "testapp.exe.diff"); // not changed
        Assert.True(File.Exists(extractAppDiff));
        Assert.True(new FileInfo(extractAppDiff).Length == 0);
        var extractAppShasum = Path.Combine(extractDir, "lib", "app", "testapp.exe.shasum");
        Assert.True(File.Exists(extractAppDiff));
        Assert.True(new FileInfo(extractAppDiff).Length == 0);

        // new file should exist but not have shasum
        var extractNewFile = Path.Combine(extractDir, "lib", "app", "NewFile.txt");
        Assert.True(File.Exists(extractNewFile));
        Assert.True(new FileInfo(extractDllDiff).Length > 0);
        var extractNewFileShasum = Path.Combine(extractDir, "lib", "app", "NewFile.txt.shasum");
        Assert.False(File.Exists(extractNewFileShasum));

        // apply delta and check package
        var output = Path.Combine(releaseDir, "delta.patched");
        new DeltaPatchCommandRunner(logger, new BasicConsole(logger, new VelopackDefaults(false))).Run(
            new DeltaPatchOptions {
                BasePackage = Path.Combine(releaseDir, $"{id}-1.0.0-full.nupkg"),
                OutputFile = output,
                PatchFiles = [new FileInfo(deltaPath)],
            }).GetAwaiterResult();

        // are the packages the same?
        Assert.True(File.Exists(output));
        var v2 = Path.Combine(releaseDir, $"{id}-2.0.0-full.nupkg");
        var f1 = File.ReadAllBytes(output);
        var f2 = File.ReadAllBytes(v2);
        Assert.True(new ReadOnlySpan<byte>(f1).SequenceEqual(new ReadOnlySpan<byte>(f2)));
        Assert.True(DeltaPackageBuilder.AreFilesEqualFast(output, v2));

        // can apply multiple deltas, and handle add/removing files?
        output = Path.Combine(releaseDir, "delta.patched2");
        var deltav3 = Path.Combine(releaseDir, $"{id}-3.0.0-delta.nupkg");
        new DeltaPatchCommandRunner(logger, new BasicConsole(logger, new VelopackDefaults(false))).Run(
            new DeltaPatchOptions {
                BasePackage = Path.Combine(releaseDir, $"{id}-1.0.0-full.nupkg"),
                OutputFile = output,
                PatchFiles = [new FileInfo(deltaPath), new FileInfo(deltav3)],
            }).GetAwaiterResult();

        // are the packages the same?
        Assert.True(File.Exists(output));
        var v3 = Path.Combine(releaseDir, $"{id}-3.0.0-full.nupkg");
        var f4 = File.ReadAllBytes(output);
        var f5 = File.ReadAllBytes(v3);
        Assert.True(new ReadOnlySpan<byte>(f4).SequenceEqual(new ReadOnlySpan<byte>(f5)));
        Assert.True(DeltaPackageBuilder.AreFilesEqualFast(output, v3));
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
        await PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger);

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
        await PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger);

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
        await PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger);

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
        await PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger);

        // check can find v2 update
        var chk2check = WindowsTestHelper.RunCoveredDotnet(appPath, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "update: 2.0.0", chk2check);
        logger.Info("TEST: found v2 update");

        // pack v3
        await PackTestApp(id, "3.0.0", "version 3 test", releaseDir, logger);

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
        var p = Process.Start(setup);
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
        await PackTestApp("LegacyTestApp", "2.0.0", "hello!", releaseDir, logger, assemblyNameOverride: "LegacyTestApp");

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
    [InlineData("LegacyTestApp-ClowdV2-Setup.exe", "app-1.0.0")]
    [InlineData("LegacyTestApp-ClowdV3-Setup.exe", "current")]
    [InlineData("LegacyTestApp-SquirrelWinV2-Setup.exe", "app-1.0.0")]
    [InlineData("LegacyTestApp-Velopack0084-Setup.exe", "current")]
    public async Task LegacyAppCanMigrate(string fixture, string origDirName)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        var rootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegacyTestApp");
        if (Directory.Exists(rootDir)) {
            IoUtil.Retry(() => IoUtil.DeleteFileOrDirectoryHard(rootDir), 10, 1000);
        }

        var setup = PathHelper.GetFixture(fixture);
        var p = Process.Start(setup);
        p!.WaitForExit();

        var currentDir = Path.Combine(rootDir, origDirName);
        var appExe = Path.Combine(currentDir, "LegacyTestApp.exe");
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
        await PackTestApp("LegacyTestApp", "2.0.0", "hello!", releaseDir, logger);

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

    private static async Task PackTestApp(string id, string version, string testString, string releaseDir, ILogger logger,
        bool addNewFile = false, string assemblyNameOverride = null)
    {
        var projDir = PathHelper.GetTestRootPath("TestApp");
        var testStringFile = Path.Combine(projDir, "Const.cs");
        var oldText = File.ReadAllText(testStringFile);

        try {
            File.WriteAllText(testStringFile, $"class Const {{ public const string TEST_STRING = \"{testString}\"; }}");
            var args = new List<string> {
                "publish", "--no-self-contained", "-c", "Release", "-r", "win-x64", "-o", "publish", "--tl:off"
            };

            if (assemblyNameOverride != null) {
                args.Add("-p:PublishSingleFile=true");
            }

            var psi = new ProcessStartInfo("dotnet");
            psi.WorkingDirectory = projDir;
            psi.AppendArgumentListSafe(args, out var debug);

            logger.Info($"TEST: Running {psi.FileName} {debug}");

            using var p = Process.Start(psi);
            p.WaitForExit();

            if (p.ExitCode != 0)
                throw new Exception($"dotnet publish failed with exit code {p.ExitCode}");

            var newFilePath = Path.Combine(projDir, "publish", "NewFile.txt");
            if (addNewFile) {
                File.WriteAllText(newFilePath, "New File Test");
            } else {
                // This is needed as the presence of this file in v1.0.0 can give false positives in the delta test
                if (File.Exists(newFilePath)) {
                    File.Delete(newFilePath);
                }
            }

            var publishDir = Path.Combine(projDir, "publish");

            if (assemblyNameOverride != null) {
                var targetExe = Path.Combine(publishDir, assemblyNameOverride + ".exe");
                if (File.Exists(targetExe)) {
                    File.Delete(targetExe);
                }
                File.Move(Path.Combine(publishDir, "TestApp.exe"), targetExe);
            }

            var options = new WindowsPackOptions {
                EntryExecutableName = (assemblyNameOverride ?? "TestApp") + ".exe",
                ReleaseDir = new DirectoryInfo(releaseDir),
                PackId = id,
                PackVersion = version,
                TargetRuntime = RID.Parse("win-x64"),
                PackDirectory = Path.Combine(projDir, "publish"),
            };

            var runner = WindowsTestHelper.GetPackRunner(logger);
            await runner.Run(options);
        } finally {
            File.WriteAllText(testStringFile, oldText);
        }
    }
}
