using Velopack.Packaging.Unix;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

public class CrossCompile
{
    private readonly ITestOutputHelper _output;

    public CrossCompile(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("linux-x64")]
    public void PackCrossApp(string target)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        var rid = RID.Parse(target);

        string id = $"from-{VelopackRuntimeInfo.SystemOs.GetOsShortName()}-targets-{rid.BaseRID.GetOsShortName()}";
        using var _1 = TempUtil.GetTempDirectory(out var tempDir);
        TestApp.PackTestApp(id, "1.0.0", id, tempDir, logger, targetRid: rid);

        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        Directory.CreateDirectory(artifactsDir);

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
    }

    [SkippableTheory]
    [InlineData("from-win-targets-linux")]
    [InlineData("from-linux-targets-linux")]
    [InlineData("from-osx-targets-linux")]
    public async Task RunCrossAppLinux(string artifactId)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        Skip.If(
            String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOPACK_CROSS_ARTIFACTS")),
            "VELOPACK_CROSS_ARTIFACTS not set");
        Skip.IfNot(VelopackRuntimeInfo.IsLinux, "AppImage's can only run on Linux");

        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        var artifactPath = Path.Combine(artifactsDir, artifactId + ".AppImage");

        Assert.True(File.Exists(artifactPath), $"Expected {artifactPath} to exist");
        Chmod.ChmodFileAsExecutable(artifactPath);

        var output = Exe.InvokeAndThrowIfNonZero(artifactPath, new[] { "test" }, null);
        logger.LogInformation(output);
        Assert.EndsWith(artifactId, output.Trim());

        var appImageLintPath = PathHelper.GetTestRootPath("appimagelint.AppImage");
        if (!File.Exists(appImageLintPath)) {
            var downloader = HttpUtil.CreateDefaultDownloader();
            await downloader.DownloadFile(
                "https://github.com/TheAssassin/appimagelint/releases/download/continuous/appimagelint-x86_64.AppImage",
                appImageLintPath,
                _ => { });
            Chmod.ChmodFileAsExecutable(appImageLintPath);
        }
        
        var lintOutput = Exe.InvokeAndThrowIfNonZero(appImageLintPath, new[] { artifactPath }, null);
        logger.LogInformation(lintOutput);
    }

    [SkippableTheory]
    [InlineData("from-win-targets-win")]
    [InlineData("from-linux-targets-win")]
    [InlineData("from-osx-targets-win")]
    public void RunCrossAppWindows(string artifactId)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        Skip.If(
            String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOPACK_CROSS_ARTIFACTS")),
            "VELOPACK_CROSS_ARTIFACTS not set");
        Skip.IfNot(VelopackRuntimeInfo.IsWindows, "PE files can only run on Windows");

        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        var artifactPath = Path.Combine(artifactsDir, artifactId + ".exe");

        Assert.True(File.Exists(artifactPath), $"Expected {artifactPath} to exist");

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appRoot = Path.Combine(appData, artifactId);
        var appExe = Path.Combine(appRoot, "current", "TestApp.exe");
        var appUpdate = Path.Combine(appRoot, "Update.exe");

        IoUtil.DeleteFileOrDirectoryHard(appRoot);

        Assert.False(File.Exists(appExe));

        var installOutput = Exe.InvokeAndThrowIfNonZero(artifactPath, new[] { "--silent" }, null);
        logger.LogInformation(installOutput);

        Assert.True(File.Exists(appExe));

        var output = Exe.InvokeAndThrowIfNonZero(appExe, new[] { "test" }, null);
        logger.LogInformation(output);
        Assert.EndsWith(artifactId, output.Trim());

        var uninstallOutput = Exe.RunHostedCommand($"\"{appUpdate}\" --uninstall --silent");
        logger.LogInformation(uninstallOutput);

        Assert.False(File.Exists(appExe));
        Assert.True(File.Exists(Path.Combine(appRoot, ".dead")));
        IoUtil.DeleteFileOrDirectoryHard(appRoot);
    }
}