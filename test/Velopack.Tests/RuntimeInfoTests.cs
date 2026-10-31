using System.Diagnostics;
using System.Runtime.InteropServices;
using Velopack.Util;

namespace Velopack.Tests;

public class RuntimeInfoTests
{
    [Fact(Skip = "Sometimes fails in CI")]
    public void NugetVersionAgreesWithNbgv()
    {
        var args = new List<string> { "get-version", "-v", "NuGetPackageVersion" };
        var psi = new ProcessStartInfo("nbgv");
        psi.AppendArgumentListSafe(args, out var _);
        var current = psi.Output(20_000);
        Assert.Equal(current, VelopackRuntimeInfo.VelopackNugetVersion.ToString());
    }

    [Fact]
    public void PlatformIsCorrect()
    {
#if NETFRAMEWORK
        Assert.True(VelopackRuntimeInfo.IsWindows);
        Assert.Equal(RuntimeOs.Windows, VelopackRuntimeInfo.SystemOs);
#else
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            Assert.True(VelopackRuntimeInfo.IsWindows);
            Assert.Equal(RuntimeOs.Windows, VelopackRuntimeInfo.SystemOs);
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux)) {
            Assert.True(VelopackRuntimeInfo.IsLinux);
            Assert.Equal(RuntimeOs.Linux, VelopackRuntimeInfo.SystemOs);
        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
            Assert.True(VelopackRuntimeInfo.IsOSX);
            Assert.Equal(RuntimeOs.OSX, VelopackRuntimeInfo.SystemOs);
        } else {
            throw new PlatformNotSupportedException();
        }
#endif
    }

    [Theory]
    [InlineData("linux-x64", RuntimeOs.Linux, RuntimeCpu.x64)]
    [InlineData("linux-arm64", RuntimeOs.Linux, RuntimeCpu.arm64)]
    [InlineData("linux-arm", RuntimeOs.Linux, RuntimeCpu.arm)]
    [InlineData("win-x86", RuntimeOs.Windows, RuntimeCpu.x86)]
    [InlineData("osx-arm64", RuntimeOs.OSX, RuntimeCpu.arm64)]
    public void RidParsesArchitecture(string input, RuntimeOs os, RuntimeCpu cpu)
    {
        var rid = RID.Parse(input);
        Assert.Equal(os, rid.BaseRID);
        Assert.Equal(cpu, rid.Architecture);
        Assert.Equal(input, rid.ToString());
    }
}
