
using System.Diagnostics;
using System.Globalization;
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

        var result = PlatformUtil.InvokeProcess(setupPath1, new string[] { "--nocolor", "--silent", "--installto", tmpInstallDir }, Environment.CurrentDirectory, CancellationToken.None);
        logger.Info(result.StdOutput);
        Assert.Equal(0, result.ExitCode);

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

        var result2 = PlatformUtil.InvokeProcess(updatePath, new string[] { "--nocolor", "--silent", "--uninstall" }, Environment.CurrentDirectory, CancellationToken.None);
        logger.Info(result2.StdOutput);
        Assert.Equal(0, result2.ExitCode);

        Assert.False(File.Exists(shortcutPath));
        Assert.False(File.Exists(appPath));

        using (var key2 = RegistryKey.OpenBaseKey(RegistryHive.CurrentUser, RegistryView.Default)
            .OpenSubKey(uninstallRegSubKey + "\\" + id, RegistryKeyPermissionCheck.ReadSubTree)) {
            Assert.Null(key2);
        }
    }
}
