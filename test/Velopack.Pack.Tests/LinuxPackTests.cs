using System.Runtime.Versioning;
using Velopack.Core;
using Velopack.Util;

using Velopack.TestCommon;

namespace Velopack.Pack.Tests;

[SupportedOSPlatform("linux")]
public class LinuxPackTests
{
    private readonly ITestOutputHelper _output;

    public LinuxPackTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("rust")]
    public async Task TestPackedLinuxAppCanUpdateToLatest(string variant)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsLinux, "Linux only");
        using var logger = _output.BuildLoggerFor<LinuxPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        string id = $"LinuxIntTest-{variant}";
        var appImagePath = Path.Combine(installDir, $"{id}.AppImage");

        // pack v1
        await PackTestAppVariant(variant, id, "1.0.0", "version 1 test", releaseDir, logger);

        // "install" by copying AppImage
        var srcAppImage = Path.Combine(releaseDir, $"{id}.AppImage");
        Assert.True(File.Exists(srcAppImage), $"Expected {srcAppImage} to exist");
        File.Copy(srcAppImage, appImagePath);
        Chmod.ChmodFileAsExecutable(appImagePath);
        logger.Info($"TEST ({variant}): v1 installed");

        // check app output
        var chk1test = TestHelper.RunNoCoverage(appImagePath, ["test"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 1 test", chk1test);
        var chk1version = TestHelper.RunNoCoverage(appImagePath, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "1.0.0", chk1version);
        var chk1check = TestHelper.RunNoCoverage(appImagePath, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "no updates", chk1check);
        logger.Info($"TEST ({variant}): v1 output verified");

        // pack v2
        await PackTestAppVariant(variant, id, "2.0.0", "version 2 test", releaseDir, logger);

        // check can find v2 update
        var chk2check = TestHelper.RunNoCoverage(appImagePath, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "update: 2.0.0", chk2check);
        logger.Info($"TEST ({variant}): found v2 update");

        // download and apply (apply before download should fail; exit code -1 wraps to 255 on unix)
        TestHelper.RunNoCoverage(appImagePath, ["apply", releaseDir], installDir, logger, exitCode: null);
        TestHelper.RunNoCoverage(appImagePath, ["download", releaseDir], installDir, logger);
        TestHelper.RunNoCoverage(appImagePath, ["apply", releaseDir], installDir, logger, exitCode: null);
        logger.Info($"TEST ({variant}): v2 applied");

        Thread.Sleep(5000); // UpdateNix runs in separate process

        // check app output after update
        var chk2version = TestHelper.RunNoCoverage(appImagePath, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "2.0.0", chk2version);
        var chk2test = TestHelper.RunNoCoverage(appImagePath, ["test"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 2 test", chk2test);
        var chk2check2 = TestHelper.RunNoCoverage(appImagePath, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "no updates", chk2check2);
        logger.Info($"TEST ({variant}): v2 output verified / complete");

        // cleanup packages dir
        try {
            var packagesDir = Path.Combine("/var/tmp/velopack", id);
            if (Directory.Exists(packagesDir))
                Directory.Delete(packagesDir, true);
        } catch { }
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("rust")]
    public async Task TestLinuxAppAutoUpdatesWhenLocalIsAvailable(string variant)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsLinux, "Linux only");
        using var logger = _output.BuildLoggerFor<LinuxPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        string id = $"LinuxAutoUpdate-{variant}";
        var appImagePath = Path.Combine(installDir, $"{id}.AppImage");

        // pack v1
        await PackTestAppVariant(variant, id, "1.0.0", "version 1 test", releaseDir, logger);

        // "install" by copying AppImage
        File.Copy(Path.Combine(releaseDir, $"{id}.AppImage"), appImagePath);
        Chmod.ChmodFileAsExecutable(appImagePath);

        // pack v2
        await PackTestAppVariant(variant, id, "2.0.0", "version 2 test", releaseDir, logger);

        // copy v2 nupkg into local packages dir
        var fileName = $"{id}-2.0.0-linux-full.nupkg";
        var packagesDir = Path.Combine("/var/tmp/velopack", id, "packages");
        Directory.CreateDirectory(packagesDir);
        File.Copy(Path.Combine(releaseDir, fileName), Path.Combine(packagesDir, fileName), true);

        // run with --autoupdate
        TestHelper.RunNoCoverage(appImagePath, ["--autoupdate"], installDir, logger, exitCode: null);

        Thread.Sleep(5000); // UpdateNix runs in separate process

        // check version after auto-update
        var chk1version = TestHelper.RunNoCoverage(appImagePath, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "2.0.0", chk1version);
        logger.Info($"TEST ({variant}): auto-update verified / complete");

        // cleanup packages dir
        try {
            var pkgRoot = Path.Combine("/var/tmp/velopack", id);
            if (Directory.Exists(pkgRoot))
                Directory.Delete(pkgRoot, true);
        } catch { }
    }

    private async Task PackTestAppVariant(string variant, string id, string version, string testString, string releaseDir, ILogger logger)
    {
        if (variant == "csharp") {
            await PackCSharpTestApp(id, version, testString, releaseDir, logger);
        } else if (variant == "rust") {
            await PackRustTestApp(id, version, testString, releaseDir, logger);
        } else {
            throw new ArgumentException($"Unknown variant: {variant}");
        }
    }

    private static async Task PackRustTestApp(string id, string version, string testString, string releaseDir, ILogger logger)
    {
        using var _ = TempUtil.GetTempDirectory(out var packDir);

        // copy pre-built Rust testapp binary
        var rustBinary = PathHelper.GetRustAsset("testapp");
        if (!File.Exists(rustBinary))
            throw new FileNotFoundException($"Rust testapp not found at: {rustBinary}. Run 'cargo build -p velopack_bins' first.");
        File.Copy(rustBinary, Path.Combine(packDir, "testapp"));
        Chmod.ChmodFileAsExecutable(Path.Combine(packDir, "testapp"));

        // write test_string.txt (read by testapp at runtime)
        File.WriteAllText(Path.Combine(packDir, "test_string.txt"), testString);

        logger.Info($"TEST: Packing Rust testapp v{version} with test string '{testString}'");

        var console = new Velopack.Vpk.Logging.BasicConsole(logger, new Velopack.Vpk.VelopackDefaults(false));
        var options = new Velopack.Packaging.Unix.Commands.LinuxPackOptions {
            EntryExecutableName = "testapp",
            ReleaseDir = new DirectoryInfo(releaseDir),
            PackId = id,
            PackVersion = version,
            TargetRuntime = RID.Parse("linux-x64"),
            PackDirectory = packDir,
        };

        var runner = new Velopack.Packaging.Unix.Commands.LinuxPackCommandRunner(logger, console);
        await runner.Run(options);
    }

    private static async Task PackCSharpTestApp(string id, string version, string testString, string releaseDir, ILogger logger)
    {
        var projDir = PathHelper.GetTestRootPath("TestApp");
        var testStringFile = Path.Combine(projDir, "Const.cs");
        var oldText = File.ReadAllText(testStringFile);

        try {
            File.WriteAllText(testStringFile, $"class Const {{ public const string TEST_STRING = \"{testString}\"; }}");
            var args = new List<string> {
                "publish", "--no-self-contained", "-c", "Release", "-r", "linux-x64", "-o", "publish", "--tl:off"
            };

            var psi = new System.Diagnostics.ProcessStartInfo("dotnet");
            psi.WorkingDirectory = projDir;
            psi.AppendArgumentListSafe(args, out var debug);

            logger.Info($"TEST: Running {psi.FileName} {debug}");

            using var p = System.Diagnostics.Process.Start(psi);
            p!.WaitForExit();

            if (p.ExitCode != 0)
                throw new Exception($"dotnet publish failed with exit code {p.ExitCode}");

            var console = new Velopack.Vpk.Logging.BasicConsole(logger, new Velopack.Vpk.VelopackDefaults(false));
            var options = new Velopack.Packaging.Unix.Commands.LinuxPackOptions {
                EntryExecutableName = "TestApp",
                ReleaseDir = new DirectoryInfo(releaseDir),
                PackId = id,
                PackVersion = version,
                TargetRuntime = RID.Parse("linux-x64"),
                PackDirectory = Path.Combine(projDir, "publish"),
            };

            var runner = new Velopack.Packaging.Unix.Commands.LinuxPackCommandRunner(logger, console);
            await runner.Run(options);
        } finally {
            File.WriteAllText(testStringFile, oldText);
        }
    }
}
