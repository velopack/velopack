using System.Runtime.Versioning;
using Velopack.Compression;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

[SupportedOSPlatform("osx")]
public class OsxPackTests
{
    private readonly ITestOutputHelper _output;

    public OsxPackTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public void PackBuildUsesAppTitleAsBundleName()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsOSX);

        using var logger = _output.BuildLoggerFor<OsxPackTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = TempUtil.GetTempDirectory(out var unzipDir);

        const string id = "MyAppId";
        const string title = "MyAppTitle";
        const string channel = "asd123";

        TestApp.PackTestApp(id, "0.0.1", string.Empty, tmpReleaseDir, logger, channel: channel, packTitle: title);

        var portablePath = Path.Combine(tmpReleaseDir, $"{id}-{channel}-Portable.zip");
        EasyZip.ExtractZipToDirectory(logger, portablePath, unzipDir);

        var bundlePath = Path.Combine(unzipDir, $"{title}.app");
        Assert.True(Directory.Exists(bundlePath));
    }
}