
using System.Xml.Linq;
using NuGet.Packaging;
using Squirrel.Compression;
using Squirrel.Packaging;
using Squirrel.Packaging.Windows.Commands;

namespace Squirrel.Packaging.Tests;

public class WindowsPackTests
{
    private readonly ITestOutputHelper _output;

    public WindowsPackTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public void PackCommandBuildsValidPackageMostOptions()
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
        Assert.Equal("net6.0-x64-desktop", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("runtimeDependencies").Single().Value);
        Assert.Equal("win", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("os").Single().Value);
        Assert.Equal("10.0.19043", xml.Root.ElementsNoNamespace("metadata").Single().ElementsNoNamespace("osMinVersion").Single().Value);

        // check for other files
        Assert.True(File.Exists(Path.Combine(unzipDir, "lib", "squirrel", "testapp.exe")));
        Assert.True(File.Exists(Path.Combine(unzipDir, "lib", "squirrel", "testapp.pdb")));
    }
}
