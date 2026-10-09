using System.IO.Compression;
using Velopack.Core;
using Velopack.TestCommon;
using Velopack.Util;
using static Velopack.TestCommon.OsxTestUtil;

namespace Velopack.Packaging.Tests;

/// <summary>
/// vpk [osx] pack end to end off macOS: the real OsxPackCommandRunner, from a published TestApp to a release feed, on the
/// host the tests run on. macOS packs with Apple's own tools, which Velopack.Pack.Tests covers; a Mac runs the bundles
/// packed on Linux and Windows in Velopack.CrossCompile.Tests, signed with rcodesign as well as unsigned.
/// </summary>
public class OsxFullPackTests(ITestOutputHelper output)
{
    private const int UserExecute = 0x40; // S_IXUSR

    [Fact]
    public void PacksReleasesDeltaAndPortableBundle()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsOSX, "macOS packs with ditto and pkgbuild, covered by Velopack.Pack.Tests");
        using var logger = output.BuildLoggerFor<OsxFullPackTests>();
        TestHelper.SkipLocallyFailInCI(TestApp.GetOsxPackBlocker(logger));

        using var _ = TempUtil.GetTempDirectory(out var releaseDir);
        const string id = "OsxFullPack";
        var rid = RID.Parse("osx-arm64");

        TestApp.PackTestApp(id, "1.0.0", "version 1", releaseDir, logger, targetRid: rid);
        TestApp.PackTestApp(id, "2.0.0", "version 2", releaseDir, logger, targetRid: rid);

        // the release feed: full packages, the delta between them, and no .pkg installer (pkgbuild is macOS only)
        Assert.True(File.Exists(Path.Combine(releaseDir, $"{id}-1.0.0-osx-full.nupkg")));
        Assert.True(File.Exists(Path.Combine(releaseDir, $"{id}-2.0.0-osx-full.nupkg")));
        Assert.True(File.Exists(Path.Combine(releaseDir, $"{id}-2.0.0-osx-delta.nupkg")));
        Assert.Empty(Directory.GetFiles(releaseDir, "*.pkg"));
        var feed = File.ReadAllText(Path.Combine(releaseDir, "releases.osx.json"));
        Assert.Contains($"{id}-2.0.0-osx-full.nupkg", feed);
        Assert.Contains($"{id}-2.0.0-osx-delta.nupkg", feed);

        using (var release = ZipFile.OpenRead(Path.Combine(releaseDir, $"{id}-2.0.0-osx-full.nupkg"))) {
            var nupkgEntries = release.Entries.ToDictionary(e => e.FullName.Replace('\\', '/'));
            Assert.Contains(nupkgEntries.Keys, n => n.EndsWith("Contents/MacOS/TestApp", StringComparison.Ordinal));
            Assert.Contains(nupkgEntries.Keys, n => n.EndsWith("Contents/MacOS/UpdateMac", StringComparison.Ordinal));

            // the manifest link is kept as a symlink entry, which the update binary restores as a link when it applies
            var manifestLinkEntry = Assert.Single(
                nupkgEntries, e => e.Key.EndsWith("Contents/MacOS/sq.version.__symlink", StringComparison.Ordinal));
            Assert.Equal("../Resources/sq.version", ReadText(manifestLinkEntry.Value));
            Assert.DoesNotContain(nupkgEntries.Keys, n => n.EndsWith("Contents/MacOS/sq.version", StringComparison.Ordinal));
        }

        // the portable zip: the bundle as its one top-level entry, with what it needs to launch on a Mac
        using var portable = ZipFile.OpenRead(Path.Combine(releaseDir, $"{id}-osx-Portable.zip"));
        var entries = portable.Entries.ToDictionary(e => e.FullName);
        Assert.All(entries.Keys, name => Assert.StartsWith($"{id}.app/", name));
        Assert.True(entries.ContainsKey($"{id}.app/Contents/Info.plist"), "Expected the bundle to have an Info.plist");

        foreach (var exe in new[] { "TestApp", "UpdateMac" }) {
            var mode = UnixModeOf(entries[$"{id}.app/Contents/MacOS/{exe}"]);
            Assert.Equal(S_IFREG, mode & S_IFMT);
            Assert.True((mode & UserExecute) != 0, $"Expected {exe} to be executable, mode is {Convert.ToString(mode, 8)}");
        }

        var manifestLink = entries[$"{id}.app/Contents/MacOS/sq.version"];
        Assert.Equal(S_IFLNK, UnixModeOf(manifestLink) & S_IFMT);
        Assert.Equal("../Resources/sq.version", ReadText(manifestLink));

        var manifest = ReadText(entries[$"{id}.app/Contents/Resources/sq.version"]);
        Assert.Contains($"<id>{id}</id>", manifest);
        Assert.Contains("<version>2.0.0</version>", manifest);
    }
}
