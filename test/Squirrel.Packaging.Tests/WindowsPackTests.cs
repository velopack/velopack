
using System;
using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Runtime.Versioning;
using System.Text;
using System.Xml.Linq;
using Microsoft.Win32;
using NuGet.Packaging;
using Squirrel.Compression;
using Squirrel.Packaging;
using Squirrel.Packaging.Windows.Commands;

namespace Squirrel.Packaging.Tests;

[SupportedOSPlatform("windows")]
public class WindowsPackTests
{
    private readonly ITestOutputHelper _output;

    public WindowsPackTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public void PackBuildValidPackageMostOptions()
    {
        Skip.IfNot(SquirrelRuntimeInfo.IsWindows);

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = Utility.GetTempDirectory(out var tmpOutput);
        using var _2 = Utility.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = Utility.GetTempDirectory(out var unzipDir);

        var exe = "testapp.exe";
        var id = "Test.Squirrel-App";
        var version = "1.0.0";

        File.Copy(HelperFile.FindTestFile(exe), Path.Combine(tmpOutput, exe));
        File.Copy(HelperFile.FindTestFile("testapp.pdb"), Path.Combine(tmpOutput, "testapp.pdb"));

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
            IncludePdb = true,
        };

        var runner = new WindowsPackCommandRunner(logger);
        runner.Pack(options);

        var nupkgPath = Path.Combine(tmpReleaseDir, $"{id}-{version}-win-x64-full.nupkg");
        Assert.True(File.Exists(nupkgPath));

        var setupPath = Path.Combine(tmpReleaseDir, $"{id}-Setup-[win-x64].exe");
        Assert.True(File.Exists(setupPath));

        var releasesPath = Path.Combine(tmpReleaseDir, $"RELEASES");
        Assert.True(File.Exists(releasesPath));

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
        Assert.True(File.Exists(Path.Combine(unzipDir, "lib", "squirrel", "testapp.exe")));
        Assert.True(File.Exists(Path.Combine(unzipDir, "lib", "squirrel", "testapp.pdb")));
    }

    [SkippableFact]
    public void PackBuildMultipleChannels()
    {
        Skip.IfNot(SquirrelRuntimeInfo.IsWindows);

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = Utility.GetTempDirectory(out var tmpOutput);
        using var _2 = Utility.GetTempDirectory(out var tmpReleaseDir);

        var exe = "testapp.exe";
        var id = "Test.Squirrel-App";
        var version = "1.0.0";

        File.Copy(HelperFile.FindTestFile(exe), Path.Combine(tmpOutput, exe));
        File.Copy(HelperFile.FindTestFile("testapp.pdb"), Path.Combine(tmpOutput, "testapp.pdb"));

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
            IncludePdb = true,
        };

        var runner = new WindowsPackCommandRunner(logger);
        runner.Pack(options);

        options.TargetRuntime = RID.Parse("win10.0.19043-x86");
        options.Channel = "hello2";
        runner.Pack(options);

        var nupkgPath1 = Path.Combine(tmpReleaseDir, $"{id}-{version}-win-x64-full.nupkg");
        Assert.True(File.Exists(nupkgPath1));

        var setupPath1 = Path.Combine(tmpReleaseDir, $"{id}-Setup-[win-x64].exe");
        Assert.True(File.Exists(setupPath1));

        var releasesPath1 = Path.Combine(tmpReleaseDir, $"RELEASES-hello");
        Assert.True(File.Exists(releasesPath1));

        var nupkgPath2 = Path.Combine(tmpReleaseDir, $"{id}-{version}-win-x86-full.nupkg");
        Assert.True(File.Exists(nupkgPath2));

        var setupPath2 = Path.Combine(tmpReleaseDir, $"{id}-Setup-[win-x86].exe");
        Assert.True(File.Exists(setupPath2));

        var releasesPath2 = Path.Combine(tmpReleaseDir, $"RELEASES-hello2");
        Assert.True(File.Exists(releasesPath2));

        var rel1 = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasesPath1, Encoding.UTF8));
        Assert.Equal(1, rel1.Count());
        Assert.True(rel1.Single().Rid == RID.Parse("win-x64"));

        var rel2 = ReleaseEntry.ParseReleaseFile(File.ReadAllText(releasesPath2, Encoding.UTF8));
        Assert.Equal(1, rel2.Count());
        Assert.True(rel2.Single().Rid == RID.Parse("win-x86"));
    }

    [SkippableFact]
    public void PackBuildRefuseChannelMultipleRids()
    {
        Skip.IfNot(SquirrelRuntimeInfo.IsWindows);

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = Utility.GetTempDirectory(out var tmpOutput);
        using var _2 = Utility.GetTempDirectory(out var tmpReleaseDir);

        var exe = "testapp.exe";
        var id = "Test.Squirrel-App";
        var version = "1.0.0";

        File.Copy(HelperFile.FindTestFile(exe), Path.Combine(tmpOutput, exe));
        File.Copy(HelperFile.FindTestFile("testapp.pdb"), Path.Combine(tmpOutput, "testapp.pdb"));

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
            IncludePdb = true,
        };

        var runner = new WindowsPackCommandRunner(logger);
        runner.Pack(options);

        options.TargetRuntime = RID.Parse("win10.0.19043-x86");
        Assert.Throws<ArgumentException>(() => runner.Pack(options));
    }

    [SkippableFact]
    public void PackBuildsPackageWhichIsInstallable()
    {
        Skip.IfNot(SquirrelRuntimeInfo.IsWindows);

        using var logger = _output.BuildLoggerFor<WindowsPackTests>();

        using var _1 = Utility.GetTempDirectory(out var tmpOutput);
        using var _2 = Utility.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = Utility.GetTempDirectory(out var tmpInstallDir);

        var exe = "testapp.exe";
        var id = "Test.Squirrel-App";
        var version = "1.0.0";

        File.Copy(HelperFile.FindTestFile(exe), Path.Combine(tmpOutput, exe));
        File.Copy(HelperFile.FindTestFile("testapp.pdb"), Path.Combine(tmpOutput, "testapp.pdb"));

        var options = new WindowsPackOptions {
            EntryExecutableName = exe,
            ReleaseDir = new DirectoryInfo(tmpReleaseDir),
            PackId = id,
            PackVersion = version,
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = tmpOutput,
        };

        var runner = new WindowsPackCommandRunner(logger);
        runner.Pack(options);

        var setupPath1 = Path.Combine(tmpReleaseDir, $"{id}-Setup-[win-x64].exe");
        Assert.True(File.Exists(setupPath1));

        RunNoCoverage(setupPath1, new[] { "--nocolor", "--silent", "--installto", tmpInstallDir }, Environment.CurrentDirectory, logger);

        var updatePath = Path.Combine(tmpInstallDir, "Update.exe");
        Assert.True(File.Exists(updatePath));

        var appPath = Path.Combine(tmpInstallDir, "current", "testapp.exe");
        Assert.True(File.Exists(appPath));

        var argsPath = Path.Combine(tmpInstallDir, "current", "args.txt");
        Assert.True(File.Exists(argsPath));
        var argsContent = File.ReadAllText(argsPath).Trim();
        Assert.Equal("--squirrel-install 1.0.0", argsContent);

        var shortcutPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.StartMenu), "Programs", id + ".lnk");
        Assert.True(File.Exists(shortcutPath));

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

        Assert.False(File.Exists(shortcutPath));
        Assert.False(File.Exists(appPath));

        using (var key2 = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
            .OpenSubKey(uninstallRegSubKey + "\\" + id, RegistryKeyPermissionCheck.ReadSubTree)) {
            Assert.Null(key2);
        }
    }

    [SkippableFact]
    public void TestPackedAppCanDeltaUpdateToLatest()
    {
        Skip.IfNot(SquirrelRuntimeInfo.IsWindows);
        using var logger = _output.BuildLoggerFor<WindowsPackTests>();
        using var _1 = Utility.GetTempDirectory(out var releaseDir);
        using var _2 = Utility.GetTempDirectory(out var installDir);

        string id = "SquirrelIntegrationTest";

        // pack v1
        PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger);

        // install app
        var setupPath1 = Path.Combine(releaseDir, $"{id}-Setup-[win-x64].exe");
        RunNoCoverage(setupPath1, new string[] { "--nocolor", "--silent", "--installto", installDir }, Environment.GetFolderPath(Environment.SpecialFolder.Desktop), logger);

        // check app installed correctly
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");
        Assert.True(File.Exists(appPath));
        var argsPath = Path.Combine(installDir, "args.txt");
        Assert.True(File.Exists(argsPath));
        var argsContent = File.ReadAllText(argsPath).Trim();
        Assert.Equal("--squirrel-install 1.0.0", argsContent);
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
        var logPath = Path.Combine(installDir, "Clowd.Squirrel.log");
        logger.Info("TEST: log output - " + Environment.NewLine + File.ReadAllText(logPath));


        // check new obsoleted/updated hooks have run
        var argsContentv3 = File.ReadAllText(argsPath).Trim();
        Assert.Contains("--squirrel-install 1.0.0", argsContentv3);
        Assert.Contains("--squirrel-obsoleted 1.0.0", argsContentv3);
        Assert.Contains("--squirrel-updated 3.0.0", argsContentv3);
        logger.Info("TEST: hooks verified");

        // uninstall
        var updatePath = Path.Combine(installDir, "Update.exe");
        RunNoCoverage(updatePath, new string[] { "--nocolor", "--silent", "--uninstall" }, Environment.CurrentDirectory, logger);
        logger.Info("TEST: uninstalled / complete");
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

        var outputfile = GetPath($"run.{RandomString(8)}.log");

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

            if (exitCode != null)
                Assert.Equal(exitCode, p.ExitCode);

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
        var outputfile = GetPath($"coverage.rundotnet.{RandomString(8)}.xml");

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
        var projDir = GetPath("TestApp");
        var testStringFile = Path.Combine(projDir, "Const.cs");

        var oldText = File.ReadAllText(testStringFile);
        File.WriteAllText(testStringFile, $"class Const {{ public const string TEST_STRING = \"{testString}\"; }}");
        var args = new string[] { "publish", "--no-self-contained", "-c", "Release", "-r", "win-x64", "-o", "publish" };
        RunNoCoverage("dotnet", args, projDir, logger);
        File.WriteAllText(testStringFile, oldText);

        var options = new WindowsPackOptions {
            EntryExecutableName = "TestApp.exe",
            ReleaseDir = new DirectoryInfo(releaseDir),
            PackId = id,
            PackVersion = version,
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = Path.Combine(projDir, "publish"),
        };

        var runner = new WindowsPackCommandRunner(logger);
        runner.Pack(options);
    }

    private static string GetPath(params string[] paths)
    {
        var ret = GetIntegrationTestRootDirectory();
        return (new FileInfo(paths.Aggregate(ret, Path.Combine))).FullName;
    }

    private static string GetIntegrationTestRootDirectory()
    {
        var st = new StackFrame(true);
        var di = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(st.GetFileName()), ".."));
        return di.FullName;
    }
}
