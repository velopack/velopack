using System.Runtime.Versioning;
using Velopack.Core;
using Velopack.Packaging;
using Velopack.Util;

using Velopack.TestCommon;

namespace Velopack.Pack.Tests;

[SupportedOSPlatform("osx")]
public class OsxPackTests
{
    private readonly ITestOutputHelper _output;

    public OsxPackTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void PackBuildUsesAppTitleAsBundleName()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");

        using var logger = _output.BuildLoggerFor<OsxPackTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = TempUtil.GetTempDirectory(out var unzipDir);

        const string id = "MyAppId";
        const string title = "MyAppTitle";
        const string channel = "asd123";

        TestApp.PackTestApp(id, "0.0.1", string.Empty, tmpReleaseDir, logger, channel: channel, packTitle: title);

        var portablePath = Path.Combine(tmpReleaseDir, $"{id}-{channel}-Portable.zip");
        ExtractDittoZip(portablePath, unzipDir);

        var bundlePath = Path.Combine(unzipDir, $"{title}.app");
        Assert.True(Directory.Exists(bundlePath));
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("rust")]
    public async Task TestPackedOsxAppCanUpdateToLatest(string variant)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");
        using var logger = _output.BuildLoggerFor<OsxPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        string id = $"OsxIntTest-{variant}";
        var exeName = variant == "rust" ? "testapp" : "TestApp";
        var bundlePath = Path.Combine(installDir, $"{id}.app");
        var appExe = Path.Combine(bundlePath, "Contents", "MacOS", exeName);

        // pack v1
        await PackTestAppVariant(variant, id, "1.0.0", "version 1 test", releaseDir, logger);

        // "install" by extracting portable ditto zip (preserves symlinks and permissions)
        var portablePath = Path.Combine(releaseDir, $"{id}-osx-Portable.zip");
        Assert.True(File.Exists(portablePath), $"Expected {portablePath} to exist");
        ExtractDittoZip(portablePath, installDir);
        Assert.True(Directory.Exists(bundlePath), $"Expected {bundlePath} to exist");
        logger.Info($"TEST ({variant}): v1 installed");

        // check app output
        var chk1test = TestHelper.RunNoCoverage(appExe, ["test"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 1 test", chk1test);
        var chk1version = TestHelper.RunNoCoverage(appExe, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "1.0.0", chk1version);
        var chk1check = TestHelper.RunNoCoverage(appExe, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "no updates", chk1check);
        logger.Info($"TEST ({variant}): v1 output verified");

        // pack v2
        await PackTestAppVariant(variant, id, "2.0.0", "version 2 test", releaseDir, logger);

        // check can find v2 update
        var chk2check = TestHelper.RunNoCoverage(appExe, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "update: 2.0.0", chk2check);
        logger.Info($"TEST ({variant}): found v2 update");

        // download and apply (apply before download should fail; exit code -1 wraps to 255 on unix)
        TestHelper.RunNoCoverage(appExe, ["apply", releaseDir], installDir, logger, exitCode: null);
        TestHelper.RunNoCoverage(appExe, ["download", releaseDir], installDir, logger);
        TestHelper.RunNoCoverage(appExe, ["apply", releaseDir], installDir, logger, exitCode: null);
        logger.Info($"TEST ({variant}): v2 applied");

        Thread.Sleep(5000); // UpdateMac runs in separate process

        // check app output after update
        var chk2version = TestHelper.RunNoCoverage(appExe, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "2.0.0", chk2version);
        var chk2test = TestHelper.RunNoCoverage(appExe, ["test"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 2 test", chk2test);
        var chk2check2 = TestHelper.RunNoCoverage(appExe, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "no updates", chk2check2);
        logger.Info($"TEST ({variant}): v2 output verified / complete");

        // cleanup packages dir
        try {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "velopack", id);
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, true);
        } catch { }
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("rust")]
    public async Task TestOsxAppAutoUpdatesWhenLocalIsAvailable(string variant)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");
        using var logger = _output.BuildLoggerFor<OsxPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        string id = $"OsxAutoUpdate-{variant}";
        var exeName = variant == "rust" ? "testapp" : "TestApp";
        var bundlePath = Path.Combine(installDir, $"{id}.app");
        var appExe = Path.Combine(bundlePath, "Contents", "MacOS", exeName);

        // pack v1
        await PackTestAppVariant(variant, id, "1.0.0", "version 1 test", releaseDir, logger);

        // "install" by extracting portable ditto zip (preserves symlinks and permissions)
        var portablePath = Path.Combine(releaseDir, $"{id}-osx-Portable.zip");
        ExtractDittoZip(portablePath, installDir);

        // pack v2
        await PackTestAppVariant(variant, id, "2.0.0", "version 2 test", releaseDir, logger);

        // copy v2 nupkg into local packages dir
        var fileName = $"{id}-2.0.0-osx-full.nupkg";
        var packagesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Caches", "velopack", id, "packages");
        Directory.CreateDirectory(packagesDir);
        File.Copy(Path.Combine(releaseDir, fileName), Path.Combine(packagesDir, fileName), true);

        // run with --autoupdate
        TestHelper.RunNoCoverage(appExe, ["--autoupdate"], installDir, logger, exitCode: null);

        Thread.Sleep(5000); // UpdateMac runs in separate process

        // check version after auto-update
        var chk1version = TestHelper.RunNoCoverage(appExe, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "2.0.0", chk1version);
        logger.Info($"TEST ({variant}): auto-update verified / complete");

        // cleanup packages dir
        try {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "velopack", id);
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, true);
        } catch { }
    }

    [Theory]
    [InlineData("LegacyTestApp-Velopack0359-osx-Portable.zip")]
    [InlineData("LegacyTestApp-Velopack1298-osx-Portable.zip")]
    public async Task LegacyOsxAppCanMigrate(string fixture)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");
        using var logger = _output.BuildLoggerFor<OsxPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);

        string id = "LegacyTestApp";
        var bundlePath = Path.Combine(installDir, $"{id}.app");
        var appExe = Path.Combine(bundlePath, "Contents", "MacOS", "TestApp");

        var fixturePath = PathHelper.GetFixture(fixture);
        Assert.True(File.Exists(fixturePath), $"Expected fixture {fixturePath} to exist");
        ExtractDittoZip(fixturePath, installDir);
        Assert.True(Directory.Exists(bundlePath), $"Expected {bundlePath} to exist");
        logger.Info("TEST: Legacy v1 installed from fixture");

        var chk1version = TestHelper.RunNoCoverage(appExe, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "1.0.0", chk1version);
        logger.Info("TEST: Legacy v1 version verified");

        await PackCSharpTestApp(id, "2.0.0", "version 2 test", releaseDir, logger);

        TestHelper.RunNoCoverage(appExe, ["download", releaseDir], installDir, logger, exitCode: 0);
        TestHelper.RunNoCoverage(appExe, ["apply", releaseDir], installDir, logger, exitCode: null);
        logger.Info("TEST: v2 applied");

        Thread.Sleep(5000);

        var chk2version = TestHelper.RunNoCoverage(appExe, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "2.0.0", chk2version);
        var chk2test = TestHelper.RunNoCoverage(appExe, ["test"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 2 test", chk2test);
        logger.Info("TEST: v2 output verified / complete");

        try {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "velopack", id);
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, true);
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

        var rustBinary = PathHelper.GetRustAsset("testapp");
        if (!File.Exists(rustBinary))
            throw new FileNotFoundException($"Rust testapp not found at: {rustBinary}. Run 'cargo build -p velopack_bins' first.");
        File.Copy(rustBinary, Path.Combine(packDir, "testapp"));
        Chmod.ChmodFileAsExecutable(Path.Combine(packDir, "testapp"));

        File.WriteAllText(Path.Combine(packDir, "test_string.txt"), testString);

        logger.Info($"TEST: Packing Rust testapp v{version} with test string '{testString}'");

        var rid = RID.Parse(VelopackRuntimeInfo.SystemRid);
        var console = new Velopack.Vpk.Logging.BasicConsole(logger, new Velopack.Vpk.VelopackDefaults(false));
        var options = new Velopack.Packaging.Unix.Commands.OsxPackOptions {
            EntryExecutableName = "testapp",
            ReleaseDir = new DirectoryInfo(releaseDir),
            PackId = id,
            PackVersion = version,
            TargetRuntime = rid,
            PackDirectory = packDir,
        };

        var runner = new Velopack.Packaging.Unix.Commands.OsxPackCommandRunner(logger, console);
        await runner.Run(options);
    }

    private static async Task PackCSharpTestApp(string id, string version, string testString, string releaseDir, ILogger logger)
    {
        var projDir = PathHelper.GetTestRootPath("TestApp");
        var testStringFile = Path.Combine(projDir, "Const.cs");
        var oldText = File.ReadAllText(testStringFile);

        try {
            File.WriteAllText(testStringFile, $"class Const {{ public const string TEST_STRING = \"{testString}\"; }}");

            var rid = RID.Parse(VelopackRuntimeInfo.SystemRid);
            var args = new List<string> {
                "publish", "--no-self-contained", "-c", "Release", "-r", rid.ToString(), "-o", "publish", "--tl:off"
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
            var options = new Velopack.Packaging.Unix.Commands.OsxPackOptions {
                EntryExecutableName = "TestApp",
                ReleaseDir = new DirectoryInfo(releaseDir),
                PackId = id,
                PackVersion = version,
                TargetRuntime = rid,
                PackDirectory = Path.Combine(projDir, "publish"),
            };

            var runner = new Velopack.Packaging.Unix.Commands.OsxPackCommandRunner(logger, console);
            await runner.Run(options);
        } finally {
            File.WriteAllText(testStringFile, oldText);
        }
    }

    private static void ExtractDittoZip(string zipPath, string destDir)
    {
        Exe.InvokeAndThrowIfNonZero("ditto", ["-xk", zipPath, destDir], null);
    }
}
