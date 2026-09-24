using System.Diagnostics;
using System.IO.Compression;
using Neovolve.Logging.Xunit;
using Velopack.Packaging.Unix;
using Velopack.Packaging.Unix.Commands;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

/// <summary>
/// vpk [osx] pack off macOS: the portable zip written without ditto, and the option rules that keep Apple-only
/// tools (codesign, notarytool, pkgbuild) off the paths that cannot run them.
/// </summary>
public class OsxCrossPackTests(ITestOutputHelper output)
{
    private const int S_IFMT = 0xF000;
    private const int S_IFREG = 0x8000;
    private const int S_IFLNK = 0xA000;

    private static string MakeBundle(string root)
    {
        var app = Path.Combine(root, "My.app");
        var macos = Directory.CreateDirectory(Path.Combine(app, "Contents", "MacOS")).FullName;
        var resources = Directory.CreateDirectory(Path.Combine(app, "Contents", "Resources")).FullName;

        File.WriteAllText(Path.Combine(app, "Contents", "Info.plist"), "<plist/>");
        var exe = Path.Combine(macos, "MyApp");
        File.WriteAllText(exe, "not really mach-o");
        File.SetUnixFileMode(exe, (UnixFileMode) 0x1ED); // 0755
        File.WriteAllText(Path.Combine(resources, "sq.version"), "<package/>");

        // Velopack's own layout: the manifest lives in Resources and is symlinked into MacOS.
        File.CreateSymbolicLink(Path.Combine(macos, "sq.version"), "../Resources/sq.version");
        return app;
    }

    [Fact]
    public void PortableZipKeepsTheBundleAsTheTopLevelEntry()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "Unix file modes cannot be written on Windows");
        using var logger = output.BuildLoggerFor<OsxCrossPackTests>();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var app = MakeBundle(dir);
        var zipPath = Path.Combine(dir, "portable.zip");

        OsxPortableZip.Create(logger, app, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        Assert.All(zip.Entries, e => Assert.StartsWith("My.app/", e.FullName));
        Assert.Contains(zip.Entries, e => e.FullName == "My.app/Contents/Info.plist");
    }

    [Fact]
    public void PortableZipRecordsExecutableBitAndSymlinks()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "Unix file modes cannot be written on Windows");
        using var logger = output.BuildLoggerFor<OsxCrossPackTests>();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var app = MakeBundle(dir);
        var zipPath = Path.Combine(dir, "portable.zip");

        OsxPortableZip.Create(logger, app, zipPath);

        using var zip = ZipFile.OpenRead(zipPath);
        var exe = zip.GetEntry("My.app/Contents/MacOS/MyApp");
        Assert.NotNull(exe);
        var exeMode = exe.ExternalAttributes >> 16;
        Assert.Equal(S_IFREG, exeMode & S_IFMT);
        Assert.Equal(0x1ED, exeMode & 0x1FF);

        var link = zip.GetEntry("My.app/Contents/MacOS/sq.version");
        Assert.NotNull(link);
        Assert.Equal(S_IFLNK, (link.ExternalAttributes >> 16) & S_IFMT);
        using var reader = new StreamReader(link.Open());
        Assert.Equal("../Resources/sq.version", reader.ReadToEnd());
    }

    [Fact]
    public void PortableZipExpandsWithUnixMetadataIntact()
    {
        // The zip is only as good as what an unzipper does with it. Info-ZIP honours the same Unix attributes Archive
        // Utility does, and is on both Linux and macOS images.
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "Unix file modes cannot be written on Windows");
        using var logger = output.BuildLoggerFor<OsxCrossPackTests>();
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var app = MakeBundle(dir);
        var zipPath = Path.Combine(dir, "portable.zip");
        OsxPortableZip.Create(logger, app, zipPath);

        var extractTo = Directory.CreateDirectory(Path.Combine(dir, "out")).FullName;
        Process unzip;
        try {
            unzip = Process.Start(new ProcessStartInfo("unzip", ["-q", zipPath, "-d", extractTo]) { UseShellExecute = false })!;
        } catch (System.ComponentModel.Win32Exception) {
            Assert.Skip("unzip is not installed");
            return;
        }

        unzip.WaitForExit();
        Assert.Equal(0, unzip.ExitCode);

        var exe = Path.Combine(extractTo, "My.app", "Contents", "MacOS", "MyApp");
        Assert.True(File.GetUnixFileMode(exe).HasFlag(UnixFileMode.UserExecute));
        var link = new FileInfo(Path.Combine(extractTo, "My.app", "Contents", "MacOS", "sq.version"));
        Assert.Equal("../Resources/sq.version", link.LinkTarget);
        Assert.Equal("<package/>", File.ReadAllText(link.FullName));
    }

    private static IEnumerable<string> ErrorsFor(OsxPackOptions options, string property)
    {
        var result = new OsxPackOptionsValidator().Validate(options);
        return result.Errors.Where(e => e.PropertyName == property).Select(e => e.ErrorMessage);
    }

    private static string TempFile(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.WriteAllText(path, "x");
        return path;
    }

    [Fact]
    public void P12WithoutItsPasswordIsRejected()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = new OsxPackOptions { NoInst = true, SignP12File = TempFile(dir, "id.p12") };

        Assert.Contains(ErrorsFor(options, nameof(OsxPackOptions.SignP12PasswordFile)), m => m.Contains("password"));
    }

    [Fact]
    public void KeychainIdentityAndP12AreExclusive()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = new OsxPackOptions {
            NoInst = true,
            SignAppIdentity = "Developer ID Application: Someone",
            SignP12File = TempFile(dir, "id.p12"),
            SignP12PasswordFile = TempFile(dir, "pw.txt"),
        };

        Assert.Contains(ErrorsFor(options, nameof(OsxPackOptions.SignP12File)), m => m.Contains("not both"));
    }

    [Fact]
    public void NotaryApiKeyNeedsTheP12()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        var options = new OsxPackOptions { NoInst = true, NotaryApiKeyFile = TempFile(dir, "key.json") };

        Assert.Contains(ErrorsFor(options, nameof(OsxPackOptions.NotaryApiKeyFile)), m => m.Contains("signP12File"));
    }

    [Fact]
    public void AppleToolOptionsAreRefusedOffMacOS()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsOSX, "these options are valid on macOS");
        var options = new OsxPackOptions { NoInst = true, SignAppIdentity = "Developer ID Application: Someone" };

        Assert.Contains(ErrorsFor(options, nameof(OsxPackOptions.SignAppIdentity)), m => m.Contains("rcodesign"));
    }

    [Fact]
    public void InstallerIsRefusedOffMacOS()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsOSX, "the .pkg installer is built on macOS");
        var options = new OsxPackOptions { NoInst = false };

        Assert.Contains(ErrorsFor(options, nameof(OsxPackOptions.NoInst)), m => m.Contains("pkgbuild"));
    }

    [Fact]
    public void InstallerIsAllowedOnMacOS()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");
        var options = new OsxPackOptions { NoInst = false };

        Assert.Empty(ErrorsFor(options, nameof(OsxPackOptions.NoInst)));
    }
}
