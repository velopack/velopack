using System.Diagnostics;
using System.Runtime.InteropServices;

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
}
