using Velopack.Core;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

public class BuildAssetsTests
{
    [Fact]
    public void ReadDropsLegacyDeltaAssets()
    {
        // assets files written by older versions of vpk can list delta packages, which are no longer deployed
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var written = new BuildAssets(dir, "win");
        written.MakeAssetPath("MyApp-1.0.0-full.nupkg", VelopackAssetType.Full);
        written.MakeAssetPath("MyApp-1.0.0-delta.nupkg", VelopackAssetType.Delta);
        written.MakeAssetPath("MyApp-win-Setup.exe", VelopackAssetType.Installer);
        written.Write();

        var read = BuildAssets.Read(dir, "win");

        Assert.Equal(2, read.Count);
        Assert.DoesNotContain(read.GetAssets(), a => a.Type == VelopackAssetType.Delta);
        Assert.DoesNotContain(read.GetFilePaths(), p => p.EndsWith("-delta.nupkg"));
    }
}
