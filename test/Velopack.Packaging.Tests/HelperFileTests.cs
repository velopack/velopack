using Neovolve.Logging.Xunit;

namespace Velopack.Packaging.Tests;

public class HelperFileTests(ITestOutputHelper output)
{
#if DEBUG
    [Fact]
    public void DebugMacUpdatePathIsMachOOrExplainsWhyNot()
    {
        using var logger = output.BuildLoggerFor<HelperFileTests>();

        // A Debug build only finds a Mach-O update binary on macOS or with a prebuilt UpdateMac in vendor/. Either outcome is
        // fine here, but it must never hand back the host's own (ELF or PE) build to be packed as UpdateMac.
        string path;
        try {
            path = HelperFile.GetUpdatePath(RID.Parse("osx-arm64"), logger);
        } catch (Exception ex) {
            Assert.Contains("Mach-O", ex.Message);
            return;
        }

        Assert.True(BinDetect.IsMachOImage(path), $"'{path}' is not a Mach-O image");
    }
#endif
}
