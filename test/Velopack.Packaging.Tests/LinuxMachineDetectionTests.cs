using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Packaging.Unix.Commands;
using Velopack.TestCommon;
using Velopack.Vpk;
using Velopack.Vpk.Logging;

namespace Velopack.Packaging.Tests;

public class LinuxMachineDetectionTests
{
    private readonly ITestOutputHelper _output;

    public LinuxMachineDetectionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private class MachineProbe : LinuxPackCommandRunner
    {
        public MachineProbe(ILogger logger, IFancyConsole console) : base(logger, console)
        {
        }

        public RuntimeCpu Detect(string path) => GetMachineForBinary(path);
    }

    // the vendored AppImage runtimes are real static ELF executables, one per architecture
    [Theory]
    [InlineData("appimagekit-runtime-x86_64", RuntimeCpu.x64)]
    [InlineData("appimagekit-runtime-aarch64", RuntimeCpu.arm64)]
    [InlineData("appimagekit-runtime-armhf", RuntimeCpu.arm)]
    [InlineData("appimagekit-runtime-i686", RuntimeCpu.x86)]
    public void DetectsElfMachine(string fileName, RuntimeCpu expected)
    {
        using var logger = _output.BuildLoggerFor<LinuxMachineDetectionTests>();
        var probe = new MachineProbe(logger, new BasicConsole(logger, new VelopackDefaults(false)));
        Assert.Equal(expected, probe.Detect(Path.Combine(PathHelper.GetVendorLibDir(), fileName)));
    }
}
