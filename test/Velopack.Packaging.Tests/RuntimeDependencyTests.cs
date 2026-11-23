using Velopack.Core;
using Velopack.Packaging.Windows.Commands;

namespace Velopack.Packaging.Tests;

public class RuntimeDependencyTests
{
    [Theory]
    [InlineData("net8-x64-desktop", "net8-x64-desktop")]
    [InlineData("net10.0-x86-runtime", "net10-x86-runtime")]
    [InlineData("net11", "net11-x64-desktop")]
    [InlineData("net11.0-x64-desktop", "net11-x64-desktop")]
    [InlineData("net11-x64-runtime", "net11-x64-runtime")]
    [InlineData("net11.0-arm64-asp", "net11-arm64-asp")]
    [InlineData("net11-x64-desktop,vcredist143-x64", "net11-x64-desktop", "vcredist143-x64")]
    public void ParseRuntimeDependenciesAcceptsDotnetVersions(string input, params string[] expected)
    {
        Assert.Equal(expected, WindowsPackCommandRunner.ParseRuntimeDependencies(input));
    }

    [Theory]
    [InlineData("netcoreapp3.1")]
    [InlineData("net11-x64-base")]
    public void ParseRuntimeDependenciesRejectsInvalidValues(string input)
    {
        Assert.Throws<UserInfoException>(() => WindowsPackCommandRunner.ParseRuntimeDependencies(input));
    }
}
