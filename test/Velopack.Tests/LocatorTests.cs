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
}
