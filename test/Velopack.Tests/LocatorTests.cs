using Velopack.Core;
using Velopack.Locators;
using Velopack.Util;

namespace Velopack.Tests;

public class LocatorTests
{
    private readonly ITestOutputHelper _output;

    public LocatorTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private const string ManifestWithNotes = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
        <metadata>
        <id>MyCoolApp</id>
        <title>MyCoolApp</title>
        <description>MyCoolApp</description>
        <authors>me</authors>
        <version>1.2.3</version>
        <channel>win</channel>
        <mainExe>MyCoolApp.exe</mainExe>
        <os>win</os>
        <rid>win-x64</rid>
        <releaseNotes><![CDATA[
        # Release 1.2.3

        - Fixed <everything>
        ]]></releaseNotes>
        <releaseNotesHtml><![CDATA[
        <h1>Release 1.2.3</h1>
        <ul>
        <li>Fixed &lt;everything&gt;</li>
        </ul>
        ]]></releaseNotesHtml>
        </metadata>
        </package>
        """;

    private const string ManifestWithoutNotes = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
        <metadata>
        <id>MyCoolApp</id>
        <version>1.2.3</version>
        <channel>win</channel>
        <mainExe>MyCoolApp.exe</mainExe>
        <os>win</os>
        <rid>win-x64</rid>
        </metadata>
        </package>
        """;

    [Fact]
    public void WindowsLocatorReadsReleaseNotesFromManifest()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<LocatorTests>();
        using var _1 = TempUtil.GetTempDirectory(out var rootDir);
        var exePath = WriteInstall(rootDir, ManifestWithNotes);

        var locator = new WindowsVelopackLocator(new FakeProcess(exePath), logger.ToVelopackLogger());

        Assert.Equal("MyCoolApp", locator.AppId);
        Assert.Equal(new SemanticVersion(1, 2, 3), locator.CurrentlyInstalledVersion);
        Assert.Equal("# Release 1.2.3\n\n- Fixed <everything>", locator.CurrentReleaseNotesMarkdown);
        Assert.Equal("<h1>Release 1.2.3</h1>\n<ul>\n<li>Fixed &lt;everything&gt;</li>\n</ul>", locator.CurrentReleaseNotesHtml);

        var um = new UpdateManager(locator: locator);
        Assert.Equal(locator.CurrentReleaseNotesMarkdown, um.CurrentReleaseNotesMarkdown);
        Assert.Equal(locator.CurrentReleaseNotesHtml, um.CurrentReleaseNotesHtml);
    }

    [Fact]
    public void WindowsLocatorReleaseNotesAreNullWhenManifestHasNone()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<LocatorTests>();
        using var _1 = TempUtil.GetTempDirectory(out var rootDir);
        var exePath = WriteInstall(rootDir, ManifestWithoutNotes);

        var locator = new WindowsVelopackLocator(new FakeProcess(exePath), logger.ToVelopackLogger());

        Assert.Equal("MyCoolApp", locator.AppId);
        Assert.Null(locator.CurrentReleaseNotesMarkdown);
        Assert.Null(locator.CurrentReleaseNotesHtml);
    }

    /// <summary>Lays out {root}/Update.exe and {root}/current/sq.version, returning the path of the (fake) main exe.</summary>
    private static string WriteInstall(string rootDir, string manifest)
    {
        var current = Directory.CreateDirectory(Path.Combine(rootDir, "current"));
        File.WriteAllText(Path.Combine(rootDir, "Update.exe"), "");
        File.WriteAllText(Path.Combine(current.FullName, CoreUtil.SpecVersionFileName), manifest);
        return Path.Combine(current.FullName, "MyCoolApp.exe");
    }

    private sealed class FakeProcess(string exePath) : IProcessImpl
    {
        public string GetCurrentProcessPath() => exePath;
        public uint GetCurrentProcessId() => 1234;
        public void StartProcess(string exePath, IEnumerable<string> args, string workDir, bool showWindow) => throw new NotSupportedException();
        public void Exit(int exitCode) => throw new NotSupportedException();
    }

    [Fact]
    public void FallbackUpdateExeRefreshFollowsRootWhenNothingIsPending()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var root = Path.Combine(dir, "root-Update.exe");
        var fallback = Path.Combine(dir, "fallback-Update.exe");
        File.WriteAllBytes(root, new byte[] { 1, 2, 3, 4 });
        Func<SemanticVersion> nothingPending = () => null;

        // missing: copied
        Assert.True(WindowsVelopackLocator.FallbackUpdateExeNeedsRefresh(root, fallback, nothingPending));
        // a plain copy keeps the last-write time: current
        File.Copy(root, fallback);
        Assert.False(WindowsVelopackLocator.FallbackUpdateExeNeedsRefresh(root, fallback, nothingPending));
        // same length, another last-write time (an MSI upgrade with an updater of the same size): refreshed
        File.SetLastWriteTimeUtc(root, File.GetLastWriteTimeUtc(fallback) + TimeSpan.FromMinutes(5));
        Assert.True(WindowsVelopackLocator.FallbackUpdateExeNeedsRefresh(root, fallback, nothingPending));
        // another length: refreshed
        File.Copy(root, fallback, true);
        Assert.False(WindowsVelopackLocator.FallbackUpdateExeNeedsRefresh(root, fallback, nothingPending));
        File.WriteAllBytes(root, new byte[] { 1, 2, 3, 4, 5 });
        Assert.True(WindowsVelopackLocator.FallbackUpdateExeNeedsRefresh(root, fallback, nothingPending));
    }

    [Fact]
    public void FallbackUpdateExeKeptWhileADownloadedUpdateIsPending()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var root = Path.Combine(dir, "root-Update.exe");
        var fallback = Path.Combine(dir, "fallback-Update.exe");
        File.WriteAllBytes(root, new byte[] { 1, 2, 3 });
        // a download put the target's (newer) updater in the fallback path
        File.WriteAllBytes(fallback, new byte[] { 9, 9, 9, 9, 9 });
        int queries = 0;
        Func<SemanticVersion> pending = () => {
            queries++;
            return SemanticVersion.Parse("2.0.0");
        };

        Assert.False(WindowsVelopackLocator.FallbackUpdateExeNeedsRefresh(root, fallback, pending));
        Assert.Equal(1, queries);
        // a missing fallback is always copied, and identical files never look for a pending update
        File.Delete(fallback);
        Assert.True(WindowsVelopackLocator.FallbackUpdateExeNeedsRefresh(root, fallback, pending));
        File.Copy(root, fallback);
        Assert.False(WindowsVelopackLocator.FallbackUpdateExeNeedsRefresh(root, fallback, pending));
        Assert.Equal(1, queries);
    }
}
