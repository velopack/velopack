using System.Xml.Linq;
using NuGet.Versioning;
using Velopack.Core;
using Velopack.NuGet;
using Velopack.Packaging.Abstractions;
using Velopack.Packaging.Unix;
using Velopack.Packaging.Windows.Commands;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

/// <summary>
/// The nuspec must carry a &lt;velopackVersion&gt; element recording the SDK release that
/// built the package (CONTRACTS §1.6). The velopack.api promotion planner compares it to
/// AppImageChannelTrailer.TRAILER_MIN_SDK_VERSION to detect packages built by SDKs that
/// predate the AppImage channel-override trailer reader.
/// </summary>
public class NuspecVelopackVersionTests
{
    private readonly ITestOutputHelper _output;

    public NuspecVelopackVersionTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void GeneratedNuspecContainsParseableVelopackVersion()
    {
        using var logger = _output.BuildLoggerFor<NuspecVelopackVersionTests>();
        using var _1 = TempUtil.GetTempDirectory(out var tmpDir);

        var builder = new TestPackageBuilder(logger);
        var options = new WindowsPackOptions {
            ReleaseDir = new DirectoryInfo(tmpDir),
            PackId = "TestApp",
            PackVersion = "1.0.0",
            EntryExecutableName = "test.exe",
            TargetRuntime = RID.Parse("win-x64"),
        };

        typeof(PackageBuilder<IPackOptions, PackOptionsValidator<IPackOptions>>)
            .GetProperty("Options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(builder, options);

        var xml = XDocument.Parse(builder.GenerateNuspecContentPublic());
        var metadata = xml.Root?.ElementsNoNamespace("metadata").FirstOrDefault();
        Assert.NotNull(metadata);

        var velopackVersion = metadata.ElementsNoNamespace("velopackVersion").FirstOrDefault()?.Value;
        Assert.NotNull(velopackVersion);
        Assert.True(SemanticVersion.TryParse(velopackVersion, out var parsed), $"velopackVersion '{velopackVersion}' is not valid semver");
        Assert.Equal(VelopackRuntimeInfo.VelopackNugetVersion.ToNormalizedString(), velopackVersion);
    }

    [Fact]
    public void TrailerMinSdkVersionConstantIsValidSemver()
    {
        // CONTRACTS §1.6: the SDK release shipping the trailer reader defines
        // TRAILER_MIN_SDK_VERSION; the velopack.api repo copies it into worker config.
        Assert.True(SemanticVersion.TryParse(AppImageChannelTrailer.TRAILER_MIN_SDK_VERSION, out _));
    }
}
