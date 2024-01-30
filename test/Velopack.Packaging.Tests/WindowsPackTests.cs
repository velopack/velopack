using System.Diagnostics;
using System.Globalization;
using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;
using NuGet.Packaging;
using Velopack.Compression;
using Velopack.Packaging.Commands;
using Velopack.Packaging.Exceptions;
using Velopack.Packaging.Windows.Commands;
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

    private WindowsPackCommandRunner GetPackRunner(ILogger logger)
    {
        var console = new BasicConsole(logger, new DefaultPromptValueFactory(false));
        return new WindowsPackCommandRunner(logger, console);
    }

    [SkippableFact]
    public void PackBuildValidPackageMostOptions()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = Utility.GetTempDirectory(out var tmpOutput);
        using var _2 = Utility.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = Utility.GetTempDirectory(out var unzipDir);

        var exe = "testawareapp.exe";
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
            Channel = "asd123"
        };

        var runner = GetPackRunner(logger);
        runner.Run(options).GetAwaiterResult();

        var nupkgPath = Path.Combine(tmpReleaseDir, $"{id}-{version}-asd123-full.nupkg");
        Assert.True(File.Exists(nupkgPath));

        var setupPath = Path.Combine(tmpReleaseDir, $"{id}-asd123-Setup.exe");
        Assert.True(File.Exists(setupPath));

        //var releasesPath = Path.Combine(tmpReleaseDir, $"RELEASES-asd123");
        //Assert.True(File.Exists(releasesPath));
        var releasesPath2 = Path.Combine(tmpReleaseDir, $"releases.asd123.json");
        Assert.True(File.Exists(releasesPath2));

        EasyZip.ExtractZipToDirectory(logger, nupkgPath, unzipDir);

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

    [SkippableFact]
    public void PackBuildRefuseSameVersion()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = Utility.GetTempDirectory(out var tmpOutput);
        using var _2 = Utility.GetTempDirectory(out var tmpReleaseDir);

        var exe = "testawareapp.exe";
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

        var runner = GetPackRunner(logger);
        runner.Run(options).GetAwaiterResult();

        Assert.Throws<UserInfoException>(() => runner.Run(options).GetAwaiterResult());
    }

    [SkippableFact]
    public void PackBuildRefuseChannelMultipleRids()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = Utility.GetTempDirectory(out var tmpOutput);
        using var _2 = Utility.GetTempDirectory(out var tmpReleaseDir);

        var exe = "testawareapp.exe";
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

        var runner = GetPackRunner(logger);
        runner.Run(options).GetAwaiterResult();

        options.TargetRuntime = RID.Parse("win10.0.19043-x86");
        Assert.Throws<UserInfoException>(() => runner.Run(options).GetAwaiterResult());
    }

    [SkippableFact]
    public void PackBuildsPackageWhichIsInstallable()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = Utility.GetTempDirectory(out var tmpOutput);
        using var _2 = Utility.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = Utility.GetTempDirectory(out var tmpInstallDir);

        var exe = "testawareapp.exe";
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
        };

        var runner = GetPackRunner(logger);
        runner.Run(options).GetAwaiterResult();

        var setupPath1 = Path.Combine(tmpReleaseDir, $"{id}-win-Setup.exe");
        Assert.True(File.Exists(setupPath1));

        RunNoCoverage(setupPath1, new[] { "--nocolor", "--silent", "--installto", tmpInstallDir }, Environment.CurrentDirectory, logger);

        var updatePath = Path.Combine(tmpInstallDir, "Update.exe");
        Assert.True(File.Exists(updatePath));

        var appPath = Path.Combine(tmpInstallDir, "current", "testawareapp.exe");
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

        var uninstOutput = RunNoCoverage(updatePath, new string[] { "--nocolor", "--silent", "--uninstall" }, Environment.CurrentDirectory, logger);
        Assert.EndsWith(Environment.NewLine + "Y", uninstOutput); // this checks that the self-delete succeeded

        Assert.False(File.Exists(startLnk));
        Assert.False(File.Exists(desktopLnk));
        Assert.False(File.Exists(appPath));

        using (var key2 = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
            .OpenSubKey(uninstallRegSubKey + "\\" + id, RegistryKeyPermissionCheck.ReadSubTree)) {
            Assert.Null(key2);
        }
    }

    [SkippableFact]
    public void TestAppAutoUpdatesWhenLocalIsAvailable()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        using var _1 = Utility.GetTempDirectory(out var releaseDir);
        using var _2 = Utility.GetTempDirectory(out var installDir);
        string id = "SquirrelAutoUpdateTest";
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");

        // pack v1
        PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger);

        // install app
        var setupPath1 = Path.Combine(releaseDir, $"{id}-win-Setup.exe");
        RunNoCoverage(setupPath1, new string[] { "--nocolor", "--silent", "--installto", installDir },
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), logger);

        // pack v2
        PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger);

        // move package into local packages dir
        var fileName = $"{id}-2.0.0-full.nupkg";
        var mvFrom = Path.Combine(releaseDir, fileName);
        var mvTo = Path.Combine(installDir, "packages", fileName);
        File.Copy(mvFrom, mvTo);

        RunCoveredDotnet(appPath, new string[] { "--autoupdate" }, installDir, logger, exitCode: null);

        Thread.Sleep(3000); // update.exe runs in separate process

        var chk1version = RunCoveredDotnet(appPath, new string[] { "version" }, installDir, logger);
        Assert.EndsWith(Environment.NewLine + "2.0.0", chk1version);
    }

    [SkippableFact]
    public void TestPackGeneratesValidDelta()
    {
        using var _1 = Utility.GetTempDirectory(out var releaseDir);
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        string id = "SquirrelDeltaTest";
        PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger);
        PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger);

        // did a zsdiff get created for our v2 update?
        var deltaPath = Path.Combine(releaseDir, $"{id}-2.0.0-delta.nupkg");
        Assert.True(File.Exists(deltaPath));
        using var _2 = Utility.GetTempDirectory(out var extractDir);
        EasyZip.ExtractZipToDirectory(logger, deltaPath, extractDir);
        var extractDllDiff = Path.Combine(extractDir, "lib", "app", "testapp.dll.zsdiff");
        Assert.True(File.Exists(extractDllDiff));
        Assert.True(new FileInfo(extractDllDiff).Length > 0);
        var extractAppDiff = Path.Combine(extractDir, "lib", "app", "testapp.exe.diff"); // not changed
        Assert.True(File.Exists(extractAppDiff));
        Assert.True(new FileInfo(extractAppDiff).Length == 0);

        // apply delta and check package
        var output = Path.Combine(releaseDir, "delta.patched");
        new DeltaPatchCommandRunner(logger, new BasicConsole(logger, new DefaultPromptValueFactory(false))).Run(new DeltaPatchOptions {
            BasePackage = Path.Combine(releaseDir, $"{id}-1.0.0-full.nupkg"),
            OutputFile = output,
            PatchFiles = new[] { new FileInfo(deltaPath) },
        }).GetAwaiterResult();

        // are the packages the same?
        Assert.True(File.Exists(output));
        var v2 = Path.Combine(releaseDir, $"{id}-2.0.0-full.nupkg");
        var f1 = File.ReadAllBytes(output);
        var f2 = File.ReadAllBytes(v2);
        Assert.True(new ReadOnlySpan<byte>(f1).SequenceEqual(new ReadOnlySpan<byte>(f2)));
        Assert.True(DeltaPackageBuilder.AreFilesEqualFast(output, v2));
    }

    [SkippableFact]
    public void TestAppHooks()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        using var _1 = Utility.GetTempDirectory(out var releaseDir);
        using var _2 = Utility.GetTempDirectory(out var installDir);
        string id = "SquirrelHookTest";
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");

        // pack v1
        PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger);

        // install app
        var setupPath1 = Path.Combine(releaseDir, $"{id}-win-Setup.exe");
        RunNoCoverage(setupPath1, new string[] { "--nocolor", "--installto", installDir },
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), logger);

        var argsPath = Path.Combine(installDir, "args.txt");
        Assert.True(File.Exists(argsPath));
        Assert.Equal("--veloapp-install 1.0.0", File.ReadAllText(argsPath).Trim());

        var firstRun = Path.Combine(installDir, "firstrun");
        Assert.True(File.Exists(argsPath));
        Assert.Equal("1.0.0", File.ReadAllText(firstRun).Trim());

        // pack v2
        PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger);

        // install v2
        RunCoveredDotnet(appPath, new string[] { "download", releaseDir }, installDir, logger);
        RunCoveredDotnet(appPath, new string[] { "apply", releaseDir }, installDir, logger, exitCode: null);

        Thread.Sleep(2000);

        var logFile = Path.Combine(installDir, "Velopack.log");
        logger.Info("TEST: update log output - " + Environment.NewLine + File.ReadAllText(logFile));

        Assert.Contains("--veloapp-obsolete 1.0.0", File.ReadAllText(argsPath).Trim());
        Assert.Contains("--veloapp-updated 2.0.0", File.ReadAllText(argsPath).Trim());

        var restartedPath = Path.Combine(installDir, "restarted");
        Assert.True(File.Exists(restartedPath));
        Assert.Equal("2.0.0,test,args !!", File.ReadAllText(restartedPath).Trim());

        var updatePath = Path.Combine(installDir, "Update.exe");
        RunNoCoverage(updatePath, new string[] { "--nocolor", "--silent", "--uninstall" }, Environment.CurrentDirectory, logger);
    }

    [SkippableFact]
    public void TestPackedAppCanDeltaUpdateToLatest()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        using var _1 = Utility.GetTempDirectory(out var releaseDir);
        using var _2 = Utility.GetTempDirectory(out var installDir);

        string id = "SquirrelIntegrationTest";

        // pack v1
        PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger);

        // install app
        var setupPath1 = Path.Combine(releaseDir, $"{id}-win-Setup.exe");
        RunNoCoverage(setupPath1, new string[] { "--nocolor", "--silent", "--installto", installDir },
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop), logger);

        // check app installed correctly
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");
        Assert.True(File.Exists(appPath));
        var argsPath = Path.Combine(installDir, "args.txt");
        Assert.True(File.Exists(argsPath));
        var argsContent = File.ReadAllText(argsPath).Trim();
        Assert.Equal("--veloapp-install 1.0.0", argsContent);
        logger.Info("TEST: v1 installed");

        // check app output
        var chk1test = RunCoveredDotnet(appPath, new string[] { "test" }, installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 1 test", chk1test);
        var chk1version = RunCoveredDotnet(appPath, new string[] { "version" }, installDir, logger);
        Assert.EndsWith(Environment.NewLine + "1.0.0", chk1version);
        var chk1check = RunCoveredDotnet(appPath, new string[] { "check", releaseDir }, installDir, logger);
        Assert.EndsWith(Environment.NewLine + "no updates", chk1check);
        logger.Info("TEST: v1 output verified");

        // pack v2
        PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger);

        // check can find v2 update
        var chk2check = RunCoveredDotnet(appPath, new string[] { "check", releaseDir }, installDir, logger);
        Assert.EndsWith(Environment.NewLine + "update: 2.0.0", chk2check);
        logger.Info("TEST: found v2 update");

        // pack v3
        PackTestApp(id, "3.0.0", "version 3 test", releaseDir, logger);

        // corrupt v2/v3 full packages as we want to test delta's
        File.WriteAllText(Path.Combine(releaseDir, $"{id}-2.0.0-win-full.nupkg"), "nope");
        File.WriteAllText(Path.Combine(releaseDir, $"{id}-3.0.0-win-full.nupkg"), "nope");

        // perform full update, check that we get v3
        // apply should fail if there's not an update downloaded
        RunCoveredDotnet(appPath, new string[] { "apply", releaseDir }, installDir, logger, exitCode: -1);
        RunCoveredDotnet(appPath, new string[] { "download", releaseDir }, installDir, logger);
        RunCoveredDotnet(appPath, new string[] { "apply", releaseDir }, installDir, logger, exitCode: null);
        logger.Info("TEST: v3 applied");

        // check app output
        var chk3test = RunCoveredDotnet(appPath, new string[] { "test" }, installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 3 test", chk3test);
        var chk3version = RunCoveredDotnet(appPath, new string[] { "version" }, installDir, logger);
        Assert.EndsWith(Environment.NewLine + "3.0.0", chk3version);
        var ch3check2 = RunCoveredDotnet(appPath, new string[] { "check", releaseDir }, installDir, logger);
        Assert.EndsWith(Environment.NewLine + "no updates", ch3check2);
        logger.Info("TEST: v3 output verified");

        // print log output
        var logPath = Path.Combine(installDir, "Velopack.log");
        logger.Info("TEST: log output - " + Environment.NewLine + File.ReadAllText(logPath));


        // check new obsoleted/updated hooks have run
        var argsContentv3 = File.ReadAllText(argsPath).Trim();
        Assert.Contains("--veloapp-install 1.0.0", argsContentv3);
        Assert.Contains("--veloapp-obsolete 1.0.0", argsContentv3);
        Assert.Contains("--veloapp-updated 3.0.0", argsContentv3);
        logger.Info("TEST: hooks verified");

        // uninstall
        var updatePath = Path.Combine(installDir, "Update.exe");
        RunNoCoverage(updatePath, new string[] { "--nocolor", "--silent", "--uninstall" }, Environment.CurrentDirectory, logger);
        logger.Info("TEST: uninstalled / complete");
    }

    [SkippableTheory]
    [InlineData("LegacyTestApp-ClowdV2-Setup.exe", "app-1.0.0")]
    [InlineData("LegacyTestApp-ClowdV3-Setup.exe", "current")]
    [InlineData("LegacyTestApp-SquirrelWinV2-Setup.exe", "app-1.0.0")]
    [InlineData("LegacyTestApp-Velopack0084-Setup.exe", "current")]
    public void LegacyAppCanSuccessfullyMigrate(string fixture, string origDirName)
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        var rootDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "LegacyTestApp");
        if (Directory.Exists(rootDir))
            Utility.DeleteFileOrDirectoryHard(rootDir);

        var setup = PathHelper.GetFixture(fixture);
        var p = Process.Start(setup);
        p.WaitForExit();

        var currentDir = Path.Combine(rootDir, origDirName);
        var appExe = Path.Combine(currentDir, "LegacyTestApp.exe");
        var updateExe = Path.Combine(rootDir, "Update.exe");
        Assert.True(File.Exists(appExe));
        Assert.True(File.Exists(updateExe));

        using var _1 = Utility.GetTempDirectory(out var releaseDir);
        PackTestApp("LegacyTestApp", "2.0.0", "hello!", releaseDir, logger);

        RunNoCoverage(appExe, new string[] { "download", releaseDir }, currentDir, logger, exitCode: 0);
        RunNoCoverage(appExe, new string[] { "apply", releaseDir }, currentDir, logger, exitCode: null);

        logger.Info("TEST: " + DateTime.Now.ToLongTimeString());

        Thread.Sleep(5000); // update.exe runs in a separate process here

        logger.Info("Velopack.log:" + Environment.NewLine + File.ReadAllText(Path.Combine(rootDir, "Velopack.log")));
        logger.Info("TEST: " + DateTime.Now.ToLongTimeString());

        if (origDirName != "current") {
            Assert.True(!Directory.Exists(currentDir));
            currentDir = Path.Combine(rootDir, "current");
        }

        Assert.True(Directory.Exists(currentDir));
        appExe = Path.Combine(currentDir, "TestApp.exe");
        Assert.True(File.Exists(appExe));

        Assert.False(Directory.EnumerateDirectories(rootDir, "app-*").Any());
        Assert.False(Directory.Exists(Path.Combine(rootDir, "staging")));

        // this is the file written by TestApp when it's detected the squirrel restart. if this is here, everything went smoothly.
        Assert.True(File.Exists(Path.Combine(rootDir, "restarted")));

        var chk3version = RunNoCoverage(appExe, new string[] { "version" }, currentDir, logger);
        Assert.EndsWith(Environment.NewLine + "2.0.0", chk3version);
    }

    //private string RunCoveredRust(string binName, string[] args, string workingDir, ILogger logger, int? exitCode = 0)
    //{
    //    var outputfile = GetPath($"coverage.runrust.{RandomString(8)}.xml");
    //    var manifestFile = GetPath("..", "src", "Rust", "Cargo.toml");

    //    var psi = new ProcessStartInfo("cargo");
    //    psi.CreateNoWindow = true;
    //    psi.RedirectStandardOutput = true;
    //    psi.RedirectStandardError = true;
    //    psi.WorkingDirectory = workingDir;

    //    psi.ArgumentList.Add("llvm-cov");
    //    psi.ArgumentList.Add("run");
    //    psi.ArgumentList.Add("--cobertura");
    //    psi.ArgumentList.Add("--manifest-path");
    //    psi.ArgumentList.Add(manifestFile);
    //    psi.ArgumentList.Add("--output");
    //    psi.ArgumentList.Add(outputfile);
    //    psi.ArgumentList.Add("--bin");
    //    psi.ArgumentList.Add(binName);
    //    psi.ArgumentList.Add("--");
    //    psi.ArgumentList.AddRange(args);

    //    return RunImpl(psi, logger, exitCode);
    //}

    private string RunImpl(ProcessStartInfo psi, ILogger logger, int? exitCode = 0)
    {
        //logger.Info($"TEST: Running {psi.FileName} {psi.ArgumentList.Aggregate((a, b) => $"{a} {b}")}");
        //using var p = Process.Start(psi);

        var outputfile = PathHelper.GetTestRootPath($"run.{RandomString(8)}.log");

        try {
            // this is a huge hack, but WaitForProcess hangs in the test runner when the output is redirected
            // so for now, we use cmd.exe to redirect output to file.
            var args = new string[psi.ArgumentList.Count];
            psi.ArgumentList.CopyTo(args, 0);
            new ProcessStartInfo().AppendArgumentListSafe(args, out var debug);

            var fix = new ProcessStartInfo("cmd.exe");
            fix.CreateNoWindow = true;
            fix.WorkingDirectory = psi.WorkingDirectory;
            fix.Arguments = $"/c \"{psi.FileName}\" {debug} > {outputfile} 2>&1";

            Stopwatch sw = new Stopwatch();
            sw.Start();

            logger.Info($"TEST: Running {fix.FileName} {fix.Arguments}");
            using var p = Process.Start(fix);

            var timeout = TimeSpan.FromMinutes(1);
            if (!p.WaitForExit(timeout))
                throw new TimeoutException($"Process did not exit within {timeout.TotalSeconds}s.");

            var elapsed = sw.Elapsed;
            sw.Stop();

            logger.Info($"TEST: Process exited with code {p.ExitCode} in {elapsed.TotalSeconds}s");

            using var fs = Utility.Retry(() => {
                return File.Open(outputfile, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            }, 10, 1000, logger);

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

            return String.Join(Environment.NewLine,
                output
                    .Split('\n')
                    .Where(l => !l.Contains("Code coverage results"))
                    .Select(l => l.Trim())
                ).Trim();
        } finally {
            try {
                File.Delete(outputfile);
            } catch { }
        }
    }

    private string RunCoveredDotnet(string exe, string[] args, string workingDir, ILogger logger, int? exitCode = 0)
    {
        var outputfile = PathHelper.GetTestRootPath($"coverage.rundotnet.{RandomString(8)}.xml");

        if (!File.Exists(exe))
            throw new Exception($"File {exe} does not exist.");

        var psi = new ProcessStartInfo("dotnet-coverage");
        psi.WorkingDirectory = workingDir;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;

        psi.ArgumentList.Add("collect");
        psi.ArgumentList.Add("-o");
        psi.ArgumentList.Add(outputfile);
        psi.ArgumentList.Add("-f");
        psi.ArgumentList.Add("cobertura");
        psi.ArgumentList.Add(exe);
        psi.ArgumentList.AddRange(args);

        return RunImpl(psi, logger, exitCode);
    }

    private static Random _random = new Random();
    private static string RandomString(int length)
    {
        string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToLower();
        return new string(Enumerable.Repeat(chars, length)
            .Select(s => s[_random.Next(s.Length)]).ToArray());
    }

    private string RunNoCoverage(string exe, string[] args, string workingDir, ILogger logger, int? exitCode = 0)
    {
        if (!File.Exists(exe))
            throw new Exception($"File {exe} does not exist.");

        var psi = new ProcessStartInfo(exe);
        psi.WorkingDirectory = workingDir;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.ArgumentList.AddRange(args);
        return RunImpl(psi, logger, exitCode);
    }

    private void PackTestApp(string id, string version, string testString, string releaseDir, ILogger logger)
    {
        var projDir = PathHelper.GetTestRootPath("TestApp");
        var testStringFile = Path.Combine(projDir, "Const.cs");
        var oldText = File.ReadAllText(testStringFile);

        try {
            File.WriteAllText(testStringFile, $"class Const {{ public const string TEST_STRING = \"{testString}\"; }}");
            var args = new string[] { "publish", "--no-self-contained", "-c", "Release", "-r", "win-x64", "-o", "publish" };

            var psi = new ProcessStartInfo("dotnet");
            psi.WorkingDirectory = projDir;
            psi.AppendArgumentListSafe(args, out var debug);

            logger.Info($"TEST: Running {psi.FileName} {debug}");

            using var p = Process.Start(psi);
            p.WaitForExit();

            if (p.ExitCode != 0)
                throw new Exception($"dotnet publish failed with exit code {p.ExitCode}");

            //RunNoCoverage("dotnet", args, projDir, logger);

            var options = new WindowsPackOptions {
                EntryExecutableName = "TestApp.exe",
                ReleaseDir = new DirectoryInfo(releaseDir),
                PackId = id,
                PackVersion = version,
                TargetRuntime = RID.Parse("win-x64"),
                PackDirectory = Path.Combine(projDir, "publish"),
            };

            var runner = GetPackRunner(logger);
            runner.Run(options).GetAwaiterResult();
        } finally {
            File.WriteAllText(testStringFile, oldText);
        }
    }
}
