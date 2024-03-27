using Divergic.Logging.Xunit;
using Velopack.Packaging.Exceptions;
using Velopack.Packaging.Windows;
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
        compat = new CompatUtil(logger, new BasicConsole(logger, new DefaultPromptValueFactory(true)));
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
        using var _1 = Utility.GetTempDirectory(out var dir);
        var sample = PathHelper.GetAvaloniaSample();
        Exe.InvokeAndThrowIfNonZero(
            "dotnet",
            new string[] { "publish", "--no-self-contained", "-r", "win-x64", "-o", dir,
                "-p:UseLocalVelopack=true", "-p:PublishSingleFile=true" },
            sample);

        var path = Path.Combine(dir, "AvaloniaCrossPlat.exe");
        Assert.Equal(VelopackRuntimeInfo.VelopackProductVersion, compat.Verify(path));

        var newPath = Path.Combine(dir, "AvaloniaCrossPlat-asd2.exe");
        File.Move(path, newPath);
        Assert.Equal(VelopackRuntimeInfo.VelopackProductVersion, compat.Verify(newPath));
    }

    [SkippableFact]
    public void PublishDotnet6Passes()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = GetCompat(out var compat);
        using var _1 = Utility.GetTempDirectory(out var dir);
        var sample = PathHelper.GetAvaloniaSample();
        Exe.InvokeAndThrowIfNonZero(
            "dotnet",
            new string[] { "publish", "--no-self-contained", "-r", "win-x64", "-o", dir,
                "-p:UseLocalVelopack=true" },
            sample);

        var path = Path.Combine(dir, "AvaloniaCrossPlat.exe");
        Assert.Equal(VelopackRuntimeInfo.VelopackProductVersion, compat.Verify(path));

        var newPath = Path.Combine(dir, "AvaloniaCrossPlat-asd2.exe");
        File.Move(path, newPath);
        Assert.Equal(VelopackRuntimeInfo.VelopackProductVersion, compat.Verify(newPath));
    }

    [SkippableFact]
    public void PublishNet48Passes()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = GetCompat(out var compat);
        using var _1 = Utility.GetTempDirectory(out var dir);
        var sample = PathHelper.GetWpfSample();
        Exe.InvokeAndThrowIfNonZero(
            "dotnet",
            new string[] { "publish", "-o", dir },
            sample);

        var path = Path.Combine(dir, "VeloWpfSample.exe");
        Assert.NotNull(compat.Verify(path));

        var newPath = Path.Combine(dir, "VeloWpfSample-asd2.exe");
        File.Move(path, newPath);
        Assert.NotNull(compat.Verify(newPath));
    }

    [SkippableFact]
    public void UnawareDotnetAppFails()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows);
        using var logger = GetCompat(out var compat);
        using var _1 = Utility.GetTempDirectory(out var dir);
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
        using var _1 = Utility.GetTempDirectory(out var dir);
        var sample = PathHelper.GetTestRootPath("TestApp");
        Exe.InvokeAndThrowIfNonZero(
            "dotnet",
            new string[] { "publish", "--no-self-contained", "-r", "win-x64", "-o", dir,
                "-p:UseAsyncMain=true" },
            sample);

        var path = Path.Combine(dir, "TestApp.exe");
        Assert.NotNull(compat.Verify(path));

        var newPath = Path.Combine(dir, "VeloWpfSample-asd2.exe");
        File.Move(path, newPath);
        Assert.NotNull(compat.Verify(newPath));
    }
}
