using Neovolve.Logging.Xunit;
using Velopack.Packaging.Exceptions;
using Velopack.Packaging.Windows;
using Velopack.Util;
using Velopack.Vpk;
using Velopack.Vpk.Logging;

namespace Velopack.Packaging.Tests;

public class CompatUtilTests
{
    private readonly ITestOutputHelper _output;

    public CompatUtilTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private ICacheLogger<CompatUtilTests> GetCompat(out CompatUtil compat)
    {
        var logger = _output.BuildLoggerFor<CompatUtilTests>();
        compat = new CompatUtil(logger, new BasicConsole(logger, new VelopackDefaults(true)));
        return logger;
    }

    [SkippableFact]
    public void NonDotnetBinaryPasses()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = GetCompat(out var compat);
        Assert.Null(compat.Verify(PathHelper.GetRustAsset("testapp.exe")));
    }

    [SkippableFact]
    public void PublishSingleFilePasses()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = GetCompat(out var compat);
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var sample = PathHelper.GetAvaloniaSample();
        Exe.InvokeAndThrowIfNonZero(
            "dotnet",
            new string[] { "publish", "--no-self-contained", "-r", "win-x64", "-o", dir,
                "-p:UseLocalVelopack=true", "-p:PublishSingleFile=true" },
            sample);

        var path = Path.Combine(dir, "VelopackCSharpAvalonia.exe");
        Assert.Equal(VelopackRuntimeInfo.VelopackProductVersion, compat.Verify(path));

        var newPath = Path.Combine(dir, "VelopackCSharpAvalonia-asd2.exe");
        File.Move(path, newPath);
        Assert.Equal(VelopackRuntimeInfo.VelopackProductVersion, compat.Verify(newPath));
    }

    [SkippableFact]
    public void PublishDotnet6Passes()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = GetCompat(out var compat);
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var sample = PathHelper.GetAvaloniaSample();
        Exe.InvokeAndThrowIfNonZero(
            "dotnet",
            new string[] { "publish", "--no-self-contained", "-r", "win-x64", "-o", dir,
                "-p:UseLocalVelopack=true" },
            sample);

        var path = Path.Combine(dir, "VelopackCSharpAvalonia.exe");
        Assert.Equal(VelopackRuntimeInfo.VelopackProductVersion, compat.Verify(path));

        var newPath = Path.Combine(dir, "VelopackCSharpAvalonia-asd2.exe");
        File.Move(path, newPath);
        Assert.Equal(VelopackRuntimeInfo.VelopackProductVersion, compat.Verify(newPath));
    }

    [SkippableFact]
    public void PublishNet48Passes()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = GetCompat(out var compat);
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var sample = PathHelper.GetWpfSample();
        string stdOut = Exe.InvokeAndThrowIfNonZero(
            "dotnet",
            new string[] { "publish", "-o", dir },
            sample);

        var path = Path.Combine(dir, "VelopackCSharpWpf.exe");
        Assert.NotNull(compat.Verify(path));
        //We do not expect to see the warning about VelopackApp.Run() not being at the start of the main method
        Assert.DoesNotContain(logger.Entries, logEntry =>
            logEntry.LogLevel == LogLevel.Warning &&
            logEntry.Message.Contains("VelopackApp.Run()")
        );
        
        var newPath = Path.Combine(dir, "VelopackCSharpWpf-asd2.exe");
        File.Move(path, newPath);
        Assert.NotNull(compat.Verify(newPath));
    }

    [SkippableFact]
    public void UnawareDotnetAppFails()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = GetCompat(out var compat);
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var sample = PathHelper.GetTestRootPath("TestApp");
        Exe.InvokeAndThrowIfNonZero(
            "dotnet",
            new string[] { "publish", "--no-self-contained", "-r", "win-x64", "-o", dir,
                "-p:NoVelopackApp=true" },
            sample);

        var path = Path.Combine(dir, "TestApp.exe");
        Assert.Throws<UserInfoException>(() => compat.Verify(path));
    }

    [SkippableFact]
    public void PublishAsyncMainPasses()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = GetCompat(out var compat);
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var sample = PathHelper.GetTestRootPath("TestApp");
        Exe.InvokeAndThrowIfNonZero(
            "dotnet",
            new string[] { "publish", "--no-self-contained", "-r", "win-x64", "-o", dir,
                "-p:UseAsyncMain=true" },
            sample);

        var path = Path.Combine(dir, "TestApp.exe");
        Assert.NotNull(compat.Verify(path));

        var newPath = Path.Combine(dir, "CSharpWpf-asd2.exe");
        File.Move(path, newPath);
        Assert.NotNull(compat.Verify(newPath));
    }
}
