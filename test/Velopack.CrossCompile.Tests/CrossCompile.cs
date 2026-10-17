using Velopack.Core;
using Velopack.Packaging;
using Velopack.Packaging.Unix;
using Velopack.Util;

using Velopack.TestCommon;

namespace Velopack.CrossCompile.Tests;

public class CrossCompile
{
    private readonly ITestOutputHelper _output;

    public CrossCompile(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Packs v1 of an app for the target OS and publishes its installer as {id}.exe / {id}.AppImage, plus an
    /// update feed directory {id}-feed holding v2, to the artifacts dir. The "rust" variant packs the rust
    /// testapp, which (unlike the .NET 8 TestApp) also runs on Windows 7; its win-x86 build gets a "-x86" id suffix.
    /// </summary>
    [Theory]
    [InlineData("win-x64", "csharp")]
    [InlineData("win-x64", "rust")]
    [InlineData("win-x86", "rust")]
    [InlineData("linux-x64", "csharp")]
    public void PackCrossApp(string target, string variant)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        var rid = RID.Parse(target);

        string id = $"from-{VelopackRuntimeInfo.SystemOs.GetOsShortName()}-targets-{rid.BaseRID.GetOsShortName()}";
        if (variant == "rust") id += "-rust";
        if (variant == "rust" && rid.Architecture == RuntimeCpu.x86) {
            id += "-x86";
            // only CI downloads the x86 testapp (testapp_x86.exe); a missing binary there must fail, not skip
            Assert.SkipWhen(
                !PathHelper.IsCI && TestApp.FindRustTestAppWindows(RuntimeCpu.x86) == null,
                "Rust testapp_x86.exe not built locally");
        }

        using var _1 = TempUtil.GetTempDirectory(out var tempDir);
        void pack(string version, string testString)
        {
            if (variant == "rust") {
                TestApp.PackRustTestAppWindows(id, version, testString, tempDir, logger, rid.Architecture);
            } else {
                TestApp.PackTestApp(id, version, testString, tempDir, logger, targetRid: rid);
            }
        }

        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        Directory.CreateDirectory(artifactsDir);

        pack("1.0.0", id);

        string src, dest;
        if (rid.BaseRID == RuntimeOs.Windows) {
            src = Path.Combine(tempDir, id + "-win-Setup.exe");
            dest = Path.Combine(artifactsDir, id + ".exe");
        } else {
            src = Path.Combine(tempDir, id + ".AppImage");
            dest = Path.Combine(artifactsDir, id + ".AppImage");
        }

        Assert.True(File.Exists(src), $"Expected {src} to exist");
        File.Copy(src, dest, overwrite: true);

        // v2 lands in the same release dir, so the feed lists both v1 and v2
        pack("2.0.0", id + "-v2");

        var feedDir = Path.Combine(artifactsDir, id + "-feed");
        IoUtil.DeleteFileOrDirectoryHard(feedDir);
        Directory.CreateDirectory(feedDir);
        var feedFiles = Directory.GetFiles(tempDir, "releases.*.json").Concat(Directory.GetFiles(tempDir, "*.nupkg")).ToArray();
        // non-Windows channels are part of the file name, e.g. {id}-2.0.0-linux-full.nupkg
        Assert.Contains(feedFiles, f => Path.GetFileName(f).StartsWith($"{id}-2.0.0-") && f.EndsWith("-full.nupkg"));
        foreach (var file in feedFiles) {
            File.Copy(file, Path.Combine(feedDir, Path.GetFileName(file)));
        }
    }

    [Theory]
    [InlineData("from-win-targets-linux")]
    [InlineData("from-linux-targets-linux")]
    [InlineData("from-osx-targets-linux")]
    public void RunCrossAppLinux(string artifactId)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        Assert.SkipWhen(
            String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOPACK_CROSS_ARTIFACTS")),
            "VELOPACK_CROSS_ARTIFACTS not set");
        Assert.SkipUnless(VelopackRuntimeInfo.IsLinux, "AppImage's can only run on Linux");

        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        var artifactPath = Path.Combine(artifactsDir, artifactId + ".AppImage");
        var feedDir = Path.Combine(artifactsDir, artifactId + "-feed");
        Assert.True(File.Exists(artifactPath), $"Expected {artifactPath} to exist");

        // applying an update replaces the AppImage file, so work on a copy
        using var _1 = TempUtil.GetTempDirectory(out var tempDir);
        var appImage = Path.Combine(tempDir, artifactId + ".AppImage");
        File.Copy(artifactPath, appImage);
        Chmod.ChmodFileAsExecutable(appImage);
        IoUtil.DeleteFileOrDirectoryHard($"/var/tmp/velopack/{artifactId}");

        string run(params string[] args)
        {
            var output = Exe.InvokeAndThrowIfNonZero(appImage, args, tempDir);
            logger.LogInformation(output);
            return output.Trim();
        }

        Assert.EndsWith(artifactId, run("test"));
        Assert.EndsWith("update: 2.0.0", run("check", feedDir));
        run("download", feedDir);
        Exe.InvokeProcess(appImage, ["apply", feedDir], tempDir);

        // the update binary swaps the AppImage in a separate process; poll until the new version is live
        TestHelper.WaitUntil(() => Assert.EndsWith("2.0.0", run("version")), timeoutMs: 60_000, pollDelayMs: 1000);
        Assert.EndsWith(artifactId + "-v2", run("test"));
    }

    [Theory]
    [InlineData("from-win-targets-win")]
    [InlineData("from-linux-targets-win")]
    [InlineData("from-osx-targets-win")]
    [InlineData("from-win-targets-win-rust")]
    [InlineData("from-linux-targets-win-rust")]
    [InlineData("from-osx-targets-win-rust")]
    [InlineData("from-win-targets-win-rust-x86")]
    [InlineData("from-linux-targets-win-rust-x86")]
    [InlineData("from-osx-targets-win-rust-x86")]
    public void RunCrossAppWindows(string artifactId)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        Assert.SkipWhen(
            String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOPACK_CROSS_ARTIFACTS")),
            "VELOPACK_CROSS_ARTIFACTS not set");
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "PE files can only run on Windows");

        InstallUpdateUninstall(new LocalWindowsHost(), artifactId, logger);
    }

    /// <summary>
    /// Runs the rust-testapp installers on a Windows 7 VM over SSH (see the win7 job in build-tests.yml);
    /// the .NET 8 TestApp variants are excluded since .NET 8 does not support Windows 7. The VM is x64, so the
    /// x86 packages (x86 update.exe/setup.exe/testapp.exe) run under WOW64.
    /// </summary>
    [Theory]
    [InlineData("from-win-targets-win-rust")]
    [InlineData("from-linux-targets-win-rust")]
    [InlineData("from-osx-targets-win-rust")]
    [InlineData("from-win-targets-win-rust-x86")]
    [InlineData("from-linux-targets-win-rust-x86")]
    [InlineData("from-osx-targets-win-rust-x86")]
    public void RunCrossAppWindows7(string artifactId)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        var connection = Environment.GetEnvironmentVariable("VELOPACK_WIN7_SSH");
        Assert.SkipWhen(String.IsNullOrWhiteSpace(connection), "VELOPACK_WIN7_SSH not set");

        InstallUpdateUninstall(new SshWindowsHost(connection!, logger), artifactId, logger);
    }

    private static void InstallUpdateUninstall(IWindowsHost host, string artifactId, ILogger logger)
    {
        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        var artifactPath = Path.Combine(artifactsDir, artifactId + ".exe");
        var feedPath = Path.Combine(artifactsDir, artifactId + "-feed");
        Assert.True(File.Exists(artifactPath), $"Expected {artifactPath} to exist");
        Assert.True(Directory.Exists(feedPath), $"Expected {feedPath} to exist");

        var appRoot = host.Combine(host.LocalAppData, artifactId);
        var appExe = host.Combine(appRoot, "current", artifactId.Contains("-rust") ? "testapp.exe" : "TestApp.exe");
        var appUpdate = host.Combine(appRoot, "Update.exe");

        host.Delete(appRoot);
        Assert.False(host.Exists(appExe));

        // a feed whose Full assets fail their checksum: the classic full download can not succeed, so a successful
        // `download` proves the chunk path (§12.5) ran on this host for a package packed on another OS
        using var _ = TempUtil.GetTempDirectory(out var chunkOnlyFeed);
        FileUtil.CopyDirectoryContents(feedPath, chunkOnlyFeed);
        ReleaseFeedHelper.CorruptFullAssetChecksums(chunkOnlyFeed, requireChunkIndex: true);

        // install v1
        var setup = host.Stage(artifactPath);
        var feed = host.Stage(chunkOnlyFeed);
        host.RunAndThrowIfNonZero(setup, "--silent");
        Assert.True(host.Exists(appExe), $"Expected {appExe} to exist after install");
        Assert.EndsWith(artifactId, host.RunAndThrowIfNonZero(appExe, "test").Trim());
        logger.Info($"TEST: {artifactId} v1 installed");

        // update to v2
        Assert.EndsWith("update: 2.0.0", host.RunAndThrowIfNonZero(appExe, "check", feed).Trim());
        host.RunAndThrowIfNonZero(appExe, "download", feed);
        // apply hands off to Update.exe and exits; its exit code is not meaningful
        host.Run(appExe, "apply", feed);

        // Update.exe swaps the app in a separate process; poll until the new version is live
        TestHelper.WaitUntil(
            () => Assert.EndsWith("2.0.0", host.RunAndThrowIfNonZero(appExe, "version").Trim()),
            timeoutMs: 60_000,
            pollDelayMs: 1000);
        Assert.EndsWith(artifactId + "-v2", host.RunAndThrowIfNonZero(appExe, "test").Trim());
        logger.Info($"TEST: {artifactId} updated to v2");

        // uninstall
        host.Run(appUpdate, "--uninstall", "--silent");

        // the uninstaller schedules the rmdir of its own directory ~3s after it exits; poll for it
        TestHelper.WaitUntil(() => Assert.False(host.Exists(appRoot)), timeoutMs: 30_000, pollDelayMs: 1000);
    }
}
