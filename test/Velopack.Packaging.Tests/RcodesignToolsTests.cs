using System.Text.RegularExpressions;
using Velopack.Core;
using Velopack.Packaging.Unix;
using Velopack.TestCommon;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

/// <summary>
/// The rcodesign command lines vpk builds and how it reads rcodesign's output. Mistakes here only show up on a real Mac
/// (an app that crashes at launch, is reported as damaged, or is rejected by the notary), so the arguments are pinned
/// exactly.
/// </summary>
public class RcodesignToolsTests
{
    private const string P12 = "/certs/developer-id.p12";
    private const string Password = "/certs/password.txt";
    private const string AppEntitlements = "/build/App.entitlements";
    private const string VelopackEntitlements = "/vendor/Velopack.entitlements";
    private const string Bundle = "/tmp/pack/MyApp.app";

    private static readonly string UpdateMacScope = VelopackRuntimeInfo.IsWindows ? @"Contents\MacOS\UpdateMac" : "Contents/MacOS/UpdateMac";

    [Fact]
    public void DeepSignScopesAppEntitlementsToMainAndRuntimeWithVelopackEntitlementsToUpdateMac()
    {
        var args = RcodesignTools.BuildSignArgs(Bundle, P12, Password, AppEntitlements, shallow: false, forNotarization: false,
            [("Contents/MacOS/UpdateMac", VelopackEntitlements)]);

        Assert.Equal(
            new[] {
                "sign",
                "--code-signature-flags", "runtime",
                "--p12-file", P12,
                "--p12-password-file", Password,
                "--entitlements-xml-file", "@main:" + AppEntitlements,
                "--code-signature-flags", UpdateMacScope + ":runtime",
                "--entitlements-xml-file", UpdateMacScope + ":" + VelopackEntitlements,
                Bundle,
            },
            args);
    }

    [Fact]
    public void NestedCodeWithoutEntitlementsOnlyGetsTheHardenedRuntime()
    {
        // e.g. createdump beside the main executable: hardened like `codesign --deep --options runtime` would, but
        // keeping its own entitlements.
        var args = RcodesignTools.BuildSignArgs(Bundle, P12, Password, AppEntitlements, shallow: false, forNotarization: false,
            [("Contents/MacOS/UpdateMac", VelopackEntitlements), ("Contents/MacOS/createdump", null)]);

        var createdumpScope = VelopackRuntimeInfo.IsWindows ? @"Contents\MacOS\createdump" : "Contents/MacOS/createdump";
        Assert.Equal(
            new[] {
                "sign",
                "--code-signature-flags", "runtime",
                "--p12-file", P12,
                "--p12-password-file", Password,
                "--entitlements-xml-file", "@main:" + AppEntitlements,
                "--code-signature-flags", UpdateMacScope + ":runtime",
                "--entitlements-xml-file", UpdateMacScope + ":" + VelopackEntitlements,
                "--code-signature-flags", createdumpScope + ":runtime",
                Bundle,
            },
            args);
    }

    [Fact]
    public void LooseMachOFilesAreFoundOutsideNestedBundles()
    {
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var bundle = Path.Combine(dir, "MyApp.app");
        WriteMachO(bundle, "Contents/MacOS/MyApp");
        WriteMachO(bundle, "Contents/MacOS/createdump");
        WriteMachO(bundle, "Contents/MacOS/runtimes/libnative.dylib");
        WriteMachO(bundle, "Contents/Frameworks/libfoo.dylib");
        WriteFile(bundle, "Contents/MacOS/MyApp.dll", new byte[512]);
        WriteFile(bundle, "Contents/Info.plist", new byte[512]);
        // Code in nested bundles is signed with the bundle it belongs to, so it is never scoped from the outer bundle.
        WriteMachO(bundle, "Contents/Frameworks/Foo.framework/Versions/A/Foo");
        WriteMachO(bundle, "Contents/Library/LoginItems/Helper.app/Contents/MacOS/Helper");
        WriteMachO(bundle, "Contents/PlugIns/Ext.appex/Contents/MacOS/Ext");

        Assert.Equal(
            new[] {
                "Contents/Frameworks/libfoo.dylib",
                "Contents/MacOS/MyApp",
                "Contents/MacOS/createdump",
                "Contents/MacOS/runtimes/libnative.dylib",
            },
            RcodesignTools.FindLooseMachOFiles(bundle));
        Assert.Empty(RcodesignTools.FindLooseMachOFiles(Path.Combine(dir, "Missing.app")));
    }

    [Fact]
    public void LooseMachOFilesLeaveOutSymlinks()
    {
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var bundle = Path.Combine(dir, "MyApp.app");
        WriteMachO(bundle, "Contents/MacOS/libfoo.1.dylib");
        File.CreateSymbolicLink(Path.Combine(bundle, "Contents", "MacOS", "libfoo.dylib"), "libfoo.1.dylib");

        Assert.Equal(new[] { "Contents/MacOS/libfoo.1.dylib" }, RcodesignTools.FindLooseMachOFiles(bundle));
    }

    [Fact]
    public void ShallowNotarizedSignAddsBothFlagsAndKeepsPathLast()
    {
        var args = RcodesignTools.BuildSignArgs(Bundle, P12, Password, AppEntitlements, shallow: true, forNotarization: true);

        Assert.Equal(
            new[] {
                "sign",
                "--shallow",
                "--for-notarization",
                "--code-signature-flags", "runtime",
                "--p12-file", P12,
                "--p12-password-file", Password,
                "--entitlements-xml-file", "@main:" + AppEntitlements,
                Bundle,
            },
            args);
    }

    [Fact]
    public void ForNotarizationIsOnlyPassedWhenNotarizing()
    {
        // --for-notarization gives every nested Mach-O the hardened runtime, but also refuses certificates that are not
        // an Apple Developer ID, so plain signing (e.g. with a test certificate) must not pass it.
        Assert.DoesNotContain("--for-notarization", RcodesignTools.BuildSignArgs(Bundle, P12, Password, AppEntitlements, false, false));
        Assert.DoesNotContain("--for-notarization", RcodesignTools.BuildSignArgs(Bundle, P12, Password, AppEntitlements, true, false));
        Assert.Contains("--for-notarization", RcodesignTools.BuildSignArgs(Bundle, P12, Password, AppEntitlements, false, true));
        Assert.DoesNotContain("--shallow", RcodesignTools.BuildSignArgs(Bundle, P12, Password, AppEntitlements, false, true));
    }

    [Fact]
    public void WindowsDriveLetterEntitlementsAreScopedToMain()
    {
        // rcodesign splits scoped values at the first ':', so an unscoped "C:\..." would become the scope "C".
        const string windowsEntitlements = @"C:\build\App.entitlements";
        var args = RcodesignTools.BuildSignArgs(@"C:\pack\MyApp.app", P12, Password, windowsEntitlements, false, true,
            [("Contents/MacOS/UpdateMac", @"C:\vendor\Velopack.entitlements")]);

        var entitlementValues = args.Where((_, i) => i > 0 && args[i - 1] == "--entitlements-xml-file").ToArray();
        Assert.Equal(2, entitlementValues.Length);
        Assert.Equal("@main:" + windowsEntitlements, entitlementValues[0]);
        Assert.Equal(UpdateMacScope + @":C:\vendor\Velopack.entitlements", entitlementValues[1]);
        Assert.All(entitlementValues, v => Assert.False(v.StartsWith("C:", StringComparison.Ordinal), v));
    }

    [Fact]
    public void NotarizeWaitsLongerThanRcodesignDefaultAndStaples()
    {
        var args = RcodesignTools.BuildNotarizeArgs(Bundle, "/certs/api-key.json");

        Assert.Equal("notary-submit", args[0]);
        Assert.Equal("/certs/api-key.json", args[args.IndexOf("--api-key-file") + 1]);
        Assert.Contains("--staple", args);
        Assert.Equal(Bundle, args[^1]);

        var maxWait = int.Parse(args[args.IndexOf("--max-wait-seconds") + 1]);
        Assert.Equal(RcodesignTools.NotaryMaxWaitSeconds, maxWait);
        Assert.True(maxWait >= 2 * 60 * 60, $"--max-wait-seconds {maxWait} is shorter than Apple's queue can take");
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void UnsealedFilesAreFoundInRcodesignOutput(string newLine)
    {
        var stdErr = String.Join(
            newLine,
            "signing Mach-O file Contents/MacOS/MyApp",
            "encountered a non Mach-O file with a nested rule: Contents/MacOS/MyApp.dll",
            "we do not know how to handle this scenario; either your bundle layout is invalid or you found a bug in this program",
            "encountered a non Mach-O file with a nested rule: Contents/MacOS/MyApp.deps.json",
            "encountered a non Mach-O file with a nested rule: Contents/MacOS/MyApp.dll",
            "sealing Contents/Info.plist");

        Assert.Equal(new[] { "Contents/MacOS/MyApp.dll", "Contents/MacOS/MyApp.deps.json" }, RcodesignTools.FindUnsealedFiles(stdErr));
        Assert.Empty(RcodesignTools.FindUnsealedFiles("signing Mach-O file Contents/MacOS/MyApp"));
        Assert.Empty(RcodesignTools.FindUnsealedFiles(""));
        Assert.Empty(RcodesignTools.FindUnsealedFiles(null));
    }

    [Fact]
    public void UnsealedFilesFailTheSignature()
    {
        // rcodesign exits 0 here, but macOS would report the app as damaged, so vpk must not go on to release it.
        var stdErr = "signing Mach-O file Contents/MacOS/MyApp\n" +
                     "encountered a non Mach-O file with a nested rule: Contents/MacOS/MyApp.dll\n" +
                     "encountered a non Mach-O file with a nested rule: Contents/MacOS/MyApp.deps.json\n";

        var ex = Assert.Throws<UserInfoException>(() => RcodesignTools.ThrowIfUnsealed(Bundle, stdErr));
        Assert.Contains("2 non-Mach-O file(s)", ex.Message);
        Assert.Contains("Contents/MacOS/MyApp.dll, Contents/MacOS/MyApp.deps.json", ex.Message);
        // vpk moves such files out of the way before signing, so this can only be a bug, not something the user did.
        Assert.Contains("bug in vpk", ex.Message);
        Assert.DoesNotContain("PublishSingleFile", ex.Message);

        RcodesignTools.ThrowIfUnsealed(Bundle, "signing Mach-O file Contents/MacOS/MyApp\nsealing Contents/Info.plist");
        RcodesignTools.ThrowIfUnsealed(Bundle, null);
    }

    [Fact]
    public void NonMachOFilesAreMovedToResourcesAndLinked()
    {
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var bundle = Path.Combine(dir, "MyApp.app");
        WriteMachO(bundle, "Contents/MacOS/MyApp");
        WriteMachO(bundle, "Contents/MacOS/UpdateMac");
        WriteFile(bundle, "Contents/MacOS/MyApp.dll", "assembly");
        WriteFile(bundle, "Contents/MacOS/MyApp.deps.json", "deps");
        // a subdirectory with a Mach-O in it keeps its layout, each other file linked on its own
        WriteMachO(bundle, "Contents/MacOS/runtimes/osx-arm64/native/libnative.dylib");
        WriteFile(bundle, "Contents/MacOS/runtimes/osx-arm64/native/libnative.json", "config");
        // one with no Mach-O anywhere is moved whole, including one whose '.' would make rcodesign take it for a bundle
        WriteFile(bundle, "Contents/MacOS/cs/MyApp.resources.dll", "satellite");
        WriteFile(bundle, "Contents/MacOS/Assets.Data/notes.txt", "notes");
        // a nested bundle is signed on its own, with its own resources
        WriteMachO(bundle, "Contents/MacOS/Helper.app/Contents/MacOS/Helper");
        WriteFile(bundle, "Contents/MacOS/Helper.app/Contents/Info.plist", "plist");
        // the rest of Contents is not a code directory
        WriteFile(bundle, "Contents/Info.plist", "plist");
        WriteFile(bundle, "Contents/Resources/MyApp.icns", "icon");

        var moved = RcodesignTools.RelocateNonMachOFiles(bundle);

        Assert.Equal(
            new[] {
                "Contents/MacOS/MyApp.deps.json",
                "Contents/MacOS/MyApp.dll",
                "Contents/MacOS/Assets.Data/",
                "Contents/MacOS/cs/",
                "Contents/MacOS/runtimes/osx-arm64/native/libnative.json",
            },
            moved);

        AssertFileLink(bundle, "Contents/MacOS/MyApp.dll", "../Resources/MacOS/MyApp.dll", "assembly");
        AssertFileLink(bundle, "Contents/MacOS/MyApp.deps.json", "../Resources/MacOS/MyApp.deps.json", "deps");
        AssertFileLink(bundle, "Contents/MacOS/runtimes/osx-arm64/native/libnative.json",
            "../../../../Resources/MacOS/runtimes/osx-arm64/native/libnative.json", "config");

        AssertDirectoryLink(bundle, "Contents/MacOS/cs", "../Resources/MacOS/cs");
        Assert.Equal("satellite", ReadFile(bundle, "Contents/MacOS/cs/MyApp.resources.dll"));
        AssertDirectoryLink(bundle, "Contents/MacOS/Assets.Data", "../Resources/MacOS/Assets.Data");
        Assert.Equal("notes", ReadFile(bundle, "Contents/MacOS/Assets.Data/notes.txt"));

        foreach (var untouched in new[] {
                     "Contents/MacOS/MyApp", "Contents/MacOS/UpdateMac", "Contents/MacOS/runtimes/osx-arm64/native/libnative.dylib",
                     "Contents/MacOS/Helper.app/Contents/MacOS/Helper", "Contents/MacOS/Helper.app/Contents/Info.plist",
                     "Contents/Info.plist", "Contents/Resources/MyApp.icns",
                 }) {
            Assert.Null(InfoOf(bundle, untouched).LinkTarget);
        }

        var relocatedDir = new DirectoryInfo(Path.Combine(bundle, "Contents", "Resources", "MacOS"));
        Assert.Equal(
            new[] { "Assets.Data", "MyApp.deps.json", "MyApp.dll", "cs", "runtimes" },
            relocatedDir.EnumerateFileSystemInfos().Select(i => i.Name).Order(StringComparer.Ordinal));

        // nothing is left to move: the links are left alone, so the Mach-O files beside them are signed where they are
        Assert.Empty(RcodesignTools.RelocateNonMachOFiles(bundle));
        Assert.Equal(new[] { "Contents/MacOS/MyApp", "Contents/MacOS/UpdateMac", "Contents/MacOS/runtimes/osx-arm64/native/libnative.dylib" },
            RcodesignTools.FindLooseMachOFiles(bundle));
    }

    [Fact]
    public void ExistingLinksInMacOSAreLeftAlone()
    {
        TestHelper.SkipUnlessSymlinksCanBeCreated();
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var bundle = Path.Combine(dir, "MyApp.app");
        WriteMachO(bundle, "Contents/MacOS/MyApp");
        WriteFile(bundle, "Contents/Resources/sq.version", "manifest");
        WriteFile(bundle, "Contents/Resources/data/notes.txt", "notes");
        // rcodesign seals a symlink as a link, whatever it points to, and does not follow a linked directory.
        File.CreateSymbolicLink(Path.Combine(bundle, "Contents", "MacOS", "sq.version"), Path.Combine("..", "Resources", "sq.version"));
        Directory.CreateSymbolicLink(Path.Combine(bundle, "Contents", "MacOS", "data"), Path.Combine("..", "Resources", "data"));

        Assert.Empty(RcodesignTools.RelocateNonMachOFiles(bundle));

        AssertFileLink(bundle, "Contents/MacOS/sq.version", "../Resources/sq.version", "manifest");
        AssertDirectoryLink(bundle, "Contents/MacOS/data", "../Resources/data");
        Assert.False(Directory.Exists(Path.Combine(bundle, "Contents", "Resources", "MacOS")));
    }

    [Fact]
    public void RelocationNeverOverwritesExistingResources()
    {
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var bundle = Path.Combine(dir, "MyApp.app");
        WriteMachO(bundle, "Contents/MacOS/MyApp");
        WriteFile(bundle, "Contents/MacOS/MyApp.dll", "assembly");
        WriteFile(bundle, "Contents/Resources/MacOS/MyApp.dll", "something else");

        var ex = Assert.Throws<UserInfoException>(() => RcodesignTools.RelocateNonMachOFiles(bundle));

        Assert.Contains("Contents/MacOS/MyApp.dll", ex.Message);
        Assert.Contains("Contents/Resources/MacOS/", ex.Message);
        Assert.Equal("assembly", ReadFile(bundle, "Contents/MacOS/MyApp.dll"));
        Assert.Equal("something else", ReadFile(bundle, "Contents/Resources/MacOS/MyApp.dll"));

        Assert.Empty(RcodesignTools.RelocateNonMachOFiles(Path.Combine(dir, "Missing.app")));
    }

    [Fact]
    public void MissingMainExecutableFailsTheSignature()
    {
        // rcodesign exits 0 here, but the app's entitlements and hardened runtime were applied to nothing.
        var stdErr = "signing bundle at /tmp/pack/MyApp.app into /tmp/pack/MyApp.app\n" +
                     "signing Mach-O file Contents/MacOS/MyApp\n" +
                     "bundle has no main executable to sign specially\n";

        Assert.True(RcodesignTools.ReportsNoMainExecutable(stdErr));
        Assert.True(RcodesignTools.ReportsNoMainExecutable(stdErr.Replace("\n", "\r\n")));
        var ex = Assert.Throws<UserInfoException>(() => RcodesignTools.ThrowIfNoMainExecutable(Bundle, stdErr));
        Assert.Contains("CFBundleExecutable", ex.Message);

        RcodesignTools.ThrowIfNoMainExecutable(Bundle, "signing main executable Contents/MacOS/MyApp");
        RcodesignTools.ThrowIfNoMainExecutable(Bundle, null);
    }

    [Fact]
    public void NestedBundleWithoutMainExecutableIsAccepted()
    {
        // A nested bundle may hold only resources; only the bundle being signed must have a main executable.
        var stdErr = String.Join(
            "\n",
            "signing 1 nested bundles in the following order:",
            "Contents/Resources/Assets.bundle",
            "entering nested bundle Contents/Resources/Assets.bundle",
            "signing bundle at /tmp/pack/MyApp.app/Contents/Resources/Assets.bundle into /tmp/pack/MyApp.app/Contents/Resources/Assets.bundle",
            "bundle has no main executable to sign specially",
            "leaving nested bundle Contents/Resources/Assets.bundle",
            "signing bundle at /tmp/pack/MyApp.app into /tmp/pack/MyApp.app",
            "signing main executable Contents/MacOS/MyApp");

        Assert.False(RcodesignTools.ReportsNoMainExecutable(stdErr));
        Assert.True(RcodesignTools.ReportsNoMainExecutable(
            stdErr.Replace("signing main executable Contents/MacOS/MyApp", "bundle has no main executable to sign specially")));
    }

    [Theory]
    [InlineData("registering signing key", true)]
    [InlineData("using time-stamp protocol server http://timestamp.apple.com/ts01", true)]
    [InlineData("automatically setting team ID from signing certificate: ABCDE12345", true)]
    [InlineData("adding code signature flag CodeSignatureFlags(RUNTIME) to path Contents/MacOS/UpdateMac", true)]
    [InlineData("setting entitlements XML for main signing target from path /build/App.entitlements", true)]
    [InlineData(@"signing C:\pack\MyApp.app in place", true)]
    [InlineData("signing /tmp/pack/My App.app in place", true)]
    [InlineData("signing bundle at /tmp/pack/MyApp.app into /tmp/pack/MyApp.app", true)]
    [InlineData("signing main executable Contents/MacOS/MyApp", true)]
    [InlineData("creating cryptographic signature with certificate Developer ID Application: Me", true)]
    [InlineData("signing Mach-O file Contents/MacOS/MyApp", true)]
    [InlineData("entering nested bundle Contents/Frameworks/Foo.framework", true)]
    [InlineData("leaving nested bundle Contents/Frameworks/Foo.framework", true)]
    // A standalone Mach-O: UpdateMac with --signDisableDeep.
    [InlineData("signing /tmp/pack/MyApp.app/Contents/MacOS/UpdateMac as a Mach-O binary", true)]
    [InlineData("setting binary identifier to UpdateMac", true)]
    [InlineData("parsing Mach-O", true)]
    [InlineData("writing Mach-O to /tmp/pack/MyApp.app/Contents/MacOS/UpdateMac", true)]
    [InlineData("poll state after 30s: InProgress", true)]
    [InlineData("signing certificate expired as of 2026-01-01T00:00:00+00:00; signatures may not be valid", false)]
    [InlineData("signing without an Apple signed certificate but signing settings contain a team name; signature varies from Apple's tooling",
        false)]
    [InlineData("bundle has no main executable to sign specially", false)]
    [InlineData("hardened runtime version required but unable to derive suitable version; signature will likely fail Apple checks", false)]
    [InlineData("1 nested bundles will be copied instead of signed because shallow signing enabled:", false)]
    [InlineData("could not find main executable of presumed nested bundle: Contents/Resources/Foo.app", false)]
    public void OnlyRoutineProgressIsHiddenFromDefaultOutput(string line, bool isProgress)
    {
        Assert.Equal(isProgress, RcodesignTools.IsProgressLine(line));
    }

    [Theory]
    // What the release binaries print (the crate is apple-codesign).
    [InlineData("apple-codesign 0.29.0", "0.29.0")]
    [InlineData("rcodesign 0.29.0", "0.29.0")]
    [InlineData("rcodesign 0.27.0\n", "0.27.0")]
    [InlineData("rcodesign 1.2.3-pre", "1.2.3")]
    [InlineData("", null)]
    [InlineData("rcodesign", null)]
    [InlineData("something unexpected", null)]
    public void VersionIsReadFromRcodesignOutput(string output, string expected)
    {
        Assert.Equal(expected == null ? null : Version.Parse(expected), RcodesignTools.ParseVersion(output));
    }

    [Fact]
    public void MinimumVersionIsTheOneCITestsWith()
    {
        // The release build-tests.yml installs is the one vpk is known to work with; raise both together.
        var workflow = File.ReadAllText(Path.Combine(PathHelper.GetProjectDir(), ".github", "workflows", "build-tests.yml"));
        var match = Regex.Match(workflow, @"name:\s*Install rcodesign\b[\s\S]*?\$version\s*=\s*'([^']+)'");

        Assert.True(match.Success, "build-tests.yml has no 'Install rcodesign' step setting $version");
        Assert.Equal(Version.Parse(match.Groups[1].Value), RcodesignTools.MinimumVersion);
    }

    [Fact]
    public void SubmissionIdIsFoundInRcodesignOutput()
    {
        var stdErr = "uploading asset to Apple\ncreated submission ID: 3a1f6c2e-8b1d-4c55-9a40-1b2c3d4e5f60\nreached wait limit after 600s";

        Assert.Equal("3a1f6c2e-8b1d-4c55-9a40-1b2c3d4e5f60", RcodesignTools.FindSubmissionId(stdErr));
        Assert.Null(RcodesignTools.FindSubmissionId("error: invalid API key"));
        Assert.Null(RcodesignTools.FindSubmissionId(null));
    }

    [Fact]
    public void RcodesignIsOnlyFoundInAbsolutePathDirectories()
    {
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var binary = WriteFakeRcodesign(dir, executable: true);

        var missing = Path.Combine(dir, "missing");
        Assert.Equal(binary, RcodesignTools.FindOnPath(String.Join(Path.PathSeparator, missing, dir)));
        Assert.Equal(binary, RcodesignTools.FindOnPath($"\"{dir}\""));

        // Relative entries (e.g. ".") would resolve against the working directory, so they are never searched.
        Assert.Null(RcodesignTools.FindOnPath(String.Join(Path.PathSeparator, ".", "relative", missing)));
        Assert.Null(RcodesignTools.FindOnPath(""));
        Assert.Null(RcodesignTools.FindOnPath(null));
    }

    [Fact]
    public void NonExecutableRcodesignIsPassedOver()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "Windows has no execute permission; any rcodesign.exe is a candidate");
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var shadowDir = Directory.CreateDirectory(Path.Combine(dir, "shadow")).FullName;
        var installDir = Directory.CreateDirectory(Path.Combine(dir, "install")).FullName;
        WriteFakeRcodesign(shadowDir, executable: false);
        var binary = WriteFakeRcodesign(installDir, executable: true);

        // As in a shell, the stray non-executable file earlier on the PATH does not shadow the real install.
        Assert.Equal(binary, RcodesignTools.FindOnPath(String.Join(Path.PathSeparator, shadowDir, installDir)));
        Assert.Null(RcodesignTools.FindOnPath(shadowDir));
    }

    private static string WriteFakeRcodesign(string dir, bool executable)
    {
        var binary = Path.Combine(dir, VelopackRuntimeInfo.IsWindows ? "rcodesign.exe" : "rcodesign");
        File.WriteAllText(binary, "");
        if (!VelopackRuntimeInfo.IsWindows) {
            var mode = UnixFileMode.UserRead | UnixFileMode.UserWrite;
            File.SetUnixFileMode(binary, executable ? mode | UnixFileMode.UserExecute : mode);
        }

        return binary;
    }

    private static void WriteMachO(string bundle, string relativePath) =>
        OsxTestUtil.WriteMachO(Path.Combine(bundle, Path.Combine(relativePath.Split('/'))));

    private static void WriteFile(string bundle, string relativePath, byte[] content)
    {
        var path = Path.Combine(bundle, Path.Combine(relativePath.Split('/')));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, content);
    }

    private static void WriteFile(string bundle, string relativePath, string content)
    {
        var path = Path.Combine(bundle, Path.Combine(relativePath.Split('/')));
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, content);
    }

    /// <summary>Reads a file through whatever links are on its path.</summary>
    private static string ReadFile(string bundle, string relativePath) =>
        File.ReadAllText(Path.Combine(bundle, Path.Combine(relativePath.Split('/'))));

    private static FileSystemInfo InfoOf(string bundle, string relativePath)
    {
        var path = Path.Combine(bundle, Path.Combine(relativePath.Split('/')));
        return Directory.Exists(path) ? new DirectoryInfo(path) : new FileInfo(path);
    }

    // Windows stores a relative link target with backslashes; the packers write it with '/'.
    private static string LinkTargetOf(FileSystemInfo info) => info.LinkTarget?.Replace('\\', '/');

    private static void AssertFileLink(string bundle, string relativePath, string expectedTarget, string expectedContent)
    {
        var link = InfoOf(bundle, relativePath);
        Assert.IsType<FileInfo>(link);
        Assert.Equal(expectedTarget, LinkTargetOf(link));
        Assert.Equal(expectedContent, ReadFile(bundle, relativePath));
    }

    private static void AssertDirectoryLink(string bundle, string relativePath, string expectedTarget)
    {
        var link = InfoOf(bundle, relativePath);
        Assert.IsType<DirectoryInfo>(link);
        Assert.Equal(expectedTarget, LinkTargetOf(link));
    }
}
