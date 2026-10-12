using System.Diagnostics;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Velopack.Core;
using Velopack.Packaging;
using Velopack.Packaging.Unix;
using Velopack.Packaging.Unix.Commands;
using Velopack.Util;

using Velopack.TestCommon;

namespace Velopack.Pack.Tests;

[SupportedOSPlatform("osx")]
public class OsxPackTests
{
    private readonly ITestOutputHelper _output;

    public OsxPackTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public void PackBuildUsesAppTitleAsBundleName()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");

        using var logger = _output.BuildLoggerFor<OsxPackTests>();

        using var _1 = TempUtil.GetTempDirectory(out var tmpOutput);
        using var _2 = TempUtil.GetTempDirectory(out var tmpReleaseDir);
        using var _3 = TempUtil.GetTempDirectory(out var unzipDir);

        const string id = "MyAppId";
        const string title = "MyAppTitle";
        const string channel = "asd123";

        TestApp.PackTestApp(id, "0.0.1", string.Empty, tmpReleaseDir, logger, channel: channel, packTitle: title);

        var portablePath = Path.Combine(tmpReleaseDir, $"{id}-{channel}-Portable.zip");
        ExtractDittoZip(portablePath, unzipDir);

        var bundlePath = Path.Combine(unzipDir, $"{title}.app");
        Assert.True(Directory.Exists(bundlePath));
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("rust")]
    public async Task TestPackedOsxAppCanUpdateToLatest(string variant)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");
        using var logger = _output.BuildLoggerFor<OsxPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        string id = $"OsxIntTest-{variant}";
        var exeName = variant == "rust" ? "testapp" : "TestApp";
        var bundlePath = Path.Combine(installDir, $"{id}.app");
        var appExe = Path.Combine(bundlePath, "Contents", "MacOS", exeName);

        // pack v1
        await PackTestAppVariant(variant, id, "1.0.0", "version 1 test", releaseDir, logger);

        // "install" by extracting portable ditto zip (preserves symlinks and permissions)
        var portablePath = Path.Combine(releaseDir, $"{id}-osx-Portable.zip");
        Assert.True(File.Exists(portablePath), $"Expected {portablePath} to exist");
        ExtractDittoZip(portablePath, installDir);
        Assert.True(Directory.Exists(bundlePath), $"Expected {bundlePath} to exist");
        logger.Info($"TEST ({variant}): v1 installed");

        // check app output
        var chk1test = TestHelper.RunNoCoverage(appExe, ["test"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 1 test", chk1test);
        var chk1version = TestHelper.RunNoCoverage(appExe, ["version"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "1.0.0", chk1version);
        var chk1check = TestHelper.RunNoCoverage(appExe, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "no updates", chk1check);
        logger.Info($"TEST ({variant}): v1 output verified");

        // pack v2
        await PackTestAppVariant(variant, id, "2.0.0", "version 2 test", releaseDir, logger);

        // check can find v2 update
        var chk2check = TestHelper.RunNoCoverage(appExe, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "update: 2.0.0", chk2check);
        logger.Info($"TEST ({variant}): found v2 update");

        // download and apply (apply before download should fail; exit code -1 wraps to 255 on unix)
        TestHelper.RunNoCoverage(appExe, ["apply", releaseDir], installDir, logger, exitCode: null);
        TestHelper.RunNoCoverage(appExe, ["download", releaseDir], installDir, logger);
        TestHelper.RunNoCoverage(appExe, ["apply", releaseDir], installDir, logger, exitCode: null);
        logger.Info($"TEST ({variant}): v2 applied");

        // UpdateMac swaps the app in a separate process; poll until the new version is live
        TestHelper.WaitUntil(() => {
            var chk2version = TestHelper.RunNoCoverage(appExe, ["version"], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "2.0.0", chk2version);
        }, pollDelayMs: 1000);
        var chk2test = TestHelper.RunNoCoverage(appExe, ["test"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 2 test", chk2test);
        var chk2check2 = TestHelper.RunNoCoverage(appExe, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "no updates", chk2check2);
        logger.Info($"TEST ({variant}): v2 output verified");

        // pack v3
        await PackTestAppVariant(variant, id, "3.0.0", "version 3 test", releaseDir, logger);

        // corrupt the v3 full package, so the update can only succeed via the v2 -> v3
        // delta (based on the v2 full downloaded above) - a fallback to a full update
        // will fail its checksum
        TestHelper.CorruptFullPackagesToForceDelta(releaseDir, id, ["3.0.0"]);

        // delta update to v3
        var chk3check = TestHelper.RunNoCoverage(appExe, ["check", releaseDir], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "update: 3.0.0", chk3check);
        TestHelper.RunNoCoverage(appExe, ["download", releaseDir], installDir, logger);
        TestHelper.RunNoCoverage(appExe, ["apply", releaseDir], installDir, logger, exitCode: null);
        logger.Info($"TEST ({variant}): v3 applied");

        TestHelper.WaitUntil(() => {
            var chk3version = TestHelper.RunNoCoverage(appExe, ["version"], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "3.0.0", chk3version);
        }, pollDelayMs: 1000);
        var chk3test = TestHelper.RunNoCoverage(appExe, ["test"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 3 test", chk3test);
        logger.Info($"TEST ({variant}): v3 output verified / complete");

        // cleanup packages dir
        try {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "velopack", id);
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, true);
        } catch { }
    }

    [Theory]
    [InlineData("csharp")]
    [InlineData("rust")]
    public async Task TestOsxAppAutoUpdatesWhenLocalIsAvailable(string variant)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");
        using var logger = _output.BuildLoggerFor<OsxPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        string id = $"OsxAutoUpdate-{variant}";
        var exeName = variant == "rust" ? "testapp" : "TestApp";
        var bundlePath = Path.Combine(installDir, $"{id}.app");
        var appExe = Path.Combine(bundlePath, "Contents", "MacOS", exeName);

        // pack v1
        await PackTestAppVariant(variant, id, "1.0.0", "version 1 test", releaseDir, logger);

        // "install" by extracting portable ditto zip (preserves symlinks and permissions)
        var portablePath = Path.Combine(releaseDir, $"{id}-osx-Portable.zip");
        ExtractDittoZip(portablePath, installDir);

        // pack v2
        await PackTestAppVariant(variant, id, "2.0.0", "version 2 test", releaseDir, logger);

        // copy v2 nupkg into local packages dir
        var fileName = $"{id}-2.0.0-osx-full.nupkg";
        var packagesDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Library", "Caches", "velopack", id, "packages");
        Directory.CreateDirectory(packagesDir);
        File.Copy(Path.Combine(releaseDir, fileName), Path.Combine(packagesDir, fileName), true);

        // run with --autoupdate
        TestHelper.RunNoCoverage(appExe, ["--autoupdate"], installDir, logger, exitCode: null);

        // UpdateMac swaps the app in a separate process; poll until the new version is live
        TestHelper.WaitUntil(() => {
            var chk1version = TestHelper.RunNoCoverage(appExe, ["version"], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "2.0.0", chk1version);
        }, pollDelayMs: 1000);
        logger.Info($"TEST ({variant}): auto-update verified / complete");

        // cleanup packages dir
        try {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "velopack", id);
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, true);
        } catch { }
    }

    [Theory]
    [InlineData("LegacyTestApp-Velopack0359-osx-Portable.zip")]
    [InlineData("LegacyTestApp-Velopack1298-osx-Portable.zip")]
    public async Task LegacyOsxAppCanMigrate(string fixture)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");
        using var logger = _output.BuildLoggerFor<OsxPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);

        string id = "LegacyTestApp";
        var bundlePath = Path.Combine(installDir, $"{id}.app");
        var appExe = Path.Combine(bundlePath, "Contents", "MacOS", "TestApp");

        var fixturePath = PathHelper.GetFixture(fixture);
        Assert.True(File.Exists(fixturePath), $"Expected fixture {fixturePath} to exist");
        ExtractDittoZip(fixturePath, installDir);
        Assert.True(Directory.Exists(bundlePath), $"Expected {bundlePath} to exist");
        logger.Info("TEST: Legacy v1 installed from fixture");

        var chk1version = TestHelper.RunNoCoverage(appExe, ["version"], installDir, logger);
        Assert.EndsWith("1.0.0", chk1version);
        logger.Info("TEST: Legacy v1 version verified");

        await PackCSharpTestApp(id, "2.0.0", "version 2 test", releaseDir, logger);

        TestHelper.RunNoCoverage(appExe, ["download", releaseDir], installDir, logger, exitCode: 0);
        TestHelper.RunNoCoverage(appExe, ["apply", releaseDir], installDir, logger, exitCode: null);
        logger.Info("TEST: v2 applied");

        // UpdateMac swaps the app in a separate process; poll until the new version is live
        TestHelper.WaitUntil(() => {
            var chk2version = TestHelper.RunNoCoverage(appExe, ["version"], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "2.0.0", chk2version);
        }, pollDelayMs: 1000);
        var chk2test = TestHelper.RunNoCoverage(appExe, ["test"], installDir, logger);
        Assert.EndsWith(Environment.NewLine + "version 2 test", chk2test);
        logger.Info("TEST: v2 output verified / complete");

        try {
            var cacheDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                "Library", "Caches", "velopack", id);
            if (Directory.Exists(cacheDir))
                Directory.Delete(cacheDir, true);
        } catch { }
    }

    private const string LsRegister =
        "/System/Library/Frameworks/CoreServices.framework/Frameworks/LaunchServices.framework/Support/lsregister";
    private const string LaunchModeElement = "<associationLaunchMode>SingleInstance</associationLaunchMode>";

    [Fact]
    public async Task PackWithAssociationsBuildsHelperBundle()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");
        using var logger = _output.BuildLoggerFor<OsxPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var unzipDir);

        var suffix = TestHelper.RandomString(6);
        const string id = "OsxAssocPack";
        const string title = "Osx Assoc App";
        var ext = $".velotest{suffix}";
        var scheme = $"velotest{suffix}";

        await PackCSharpTestApp(id, "1.0.0", "version 1 test", releaseDir, logger, packTitle: title,
            fileAssociations: ext, urlProtocols: scheme, launchMode: AssociationLaunchMode.SingleInstance);
        ExtractDittoZip(Path.Combine(releaseDir, $"{id}-osx-Portable.zip"), unzipDir);
        var bundle = Path.Combine(unzipDir, $"{title}.app");
        Assert.True(Directory.Exists(bundle), $"Expected {bundle} to exist");

        // the nested helper bundle owns the association keys
        var helper = Path.Combine(bundle, "Contents", "Helpers", $"{title}.app");
        Assert.Equal(helper, OsxAssociationHelper.Find(bundle));
        var helperExe = Path.Combine(helper, "Contents", "MacOS", OsxAssociationHelper.ExecutableName);
        Assert.True(File.Exists(helperExe), $"Expected {helperExe} to exist");
        Assert.True(BinDetect.IsMachOImage(helperExe), "the helper executable is not a Mach-O image");
        Assert.True(File.GetUnixFileMode(helperExe).HasFlag(UnixFileMode.UserExecute), "the helper executable is not executable");
        var icon = Path.GetFileName(HelperFile.GetDefaultAppIcon(RuntimeOs.OSX));
        Assert.True(File.Exists(Path.Combine(helper, "Contents", "Resources", icon)), "the helper has no icon");

        var dict = LoadPlistDict(Path.Combine(helper, "Contents", "Info.plist"));
        Assert.Equal(OsxAssociationHelper.ExecutableName, PlistValue(dict, "CFBundleExecutable")?.Value);
        Assert.EndsWith(".velopack-assoc", PlistValue(dict, "CFBundleIdentifier")?.Value);
        Assert.Equal(title, PlistValue(dict, "CFBundleName")?.Value);
        Assert.Equal(title, PlistValue(dict, "CFBundleDisplayName")?.Value);
        Assert.Equal(icon, PlistValue(dict, "CFBundleIconFile")?.Value);
        Assert.Equal("true", PlistValue(dict, "LSUIElement")?.Name.LocalName);
        Assert.Equal(OsxAssociationHelper.MinimumSystemVersion, PlistValue(dict, "LSMinimumSystemVersion")?.Value);
        Assert.Equal("SingleInstance", PlistValue(dict, OsxAssociationHelper.LaunchModePlistKey)?.Value);
        Assert.Contains(scheme, PlistValue(dict, "CFBundleURLTypes")?.ToString());
        Assert.Contains(ext.TrimStart('.'), PlistValue(dict, "CFBundleDocumentTypes")?.ToString());

        // the main Info.plist never declares them
        var main = LoadPlistDict(Path.Combine(bundle, "Contents", "Info.plist"));
        Assert.Null(PlistValue(main, "CFBundleURLTypes"));
        Assert.Null(PlistValue(main, "CFBundleDocumentTypes"));

        Assert.Contains(LaunchModeElement, File.ReadAllText(Path.Combine(bundle, "Contents", "Resources", "sq.version")));
    }

    [Fact]
    public async Task BundleThenPackKeepsHelperAndMode()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");
        using var logger = _output.BuildLoggerFor<OsxPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        using var _2 = TempUtil.GetTempDirectory(out var bundleDir);
        using var _3 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _4 = TempUtil.GetTempDirectory(out var unzipDir);

        const string id = "OsxAssocBundle";
        var rid = RID.Parse(VelopackRuntimeInfo.SystemRid);
        var publishDir = Path.Combine(workDir, "publish");
        TestApp.PreparePublishDir(rid, "version 1 test", publishDir, logger);

        // `vpk bundle` builds the helper and records the mode in its Info.plist
        var appDir = new OsxBundleCommandRunner(logger).Bundle(new OsxBundleOptions {
            EntryExecutableName = "TestApp",
            ReleaseDir = new DirectoryInfo(bundleDir),
            PackId = id,
            PackVersion = "1.0.0",
            PackDirectory = publishDir,
            UrlProtocols = $"velotest{TestHelper.RandomString(6)}",
            AssociationLaunchMode = AssociationLaunchMode.SingleInstance,
        });
        var bundledHelper = OsxAssociationHelper.Find(appDir);
        Assert.NotNull(bundledHelper);
        Assert.Equal("SingleInstance",
            PlistValue(LoadPlistDict(Path.Combine(bundledHelper, "Contents", "Info.plist")), OsxAssociationHelper.LaunchModePlistKey)?.Value);

        // `vpk pack --packDir X.app` with no association options keeps the helper and carries its mode into the manifest
        var console = new Velopack.Vpk.Logging.BasicConsole(logger, new Velopack.Vpk.VelopackDefaults(false));
        await new OsxPackCommandRunner(logger, console).Run(new OsxPackOptions {
            EntryExecutableName = "TestApp",
            ReleaseDir = new DirectoryInfo(releaseDir),
            PackId = id,
            PackVersion = "1.0.0",
            TargetRuntime = rid,
            PackDirectory = appDir,
        });
        ExtractDittoZip(Path.Combine(releaseDir, $"{id}-osx-Portable.zip"), unzipDir);
        var bundle = Path.Combine(unzipDir, $"{id}.app");
        var helper = OsxAssociationHelper.Find(bundle);
        Assert.NotNull(helper);
        Assert.Equal(Path.GetFileName(bundledHelper), Path.GetFileName(helper));
        Assert.Contains(LaunchModeElement, File.ReadAllText(Path.Combine(bundle, "Contents", "Resources", "sq.version")));
    }

    [Fact]
    public async Task UrlActivationViaHelperReachesApp()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");
        using var logger = _output.BuildLoggerFor<OsxPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);

        const string id = "OsxAssocE2E";
        var scheme = $"velotest{TestHelper.RandomString(6)}";
        var bundle = Path.Combine(installDir, $"{id}.app");
        var contents = Path.Combine(bundle, "Contents");
        var appExe = Path.Combine(contents, "MacOS", "TestApp");
        var urlFile = Path.Combine(contents, "url-proto");
        var pidFile = Path.Combine(contents, "assoc-pid");
        var logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", $"velopack_{id}.log");

        await PackCSharpTestApp(id, "1.0.0", "version 1 test", releaseDir, logger,
            urlProtocols: scheme, launchMode: AssociationLaunchMode.SingleInstance);
        ExtractDittoZip(Path.Combine(releaseDir, $"{id}-osx-Portable.zip"), installDir);
        Assert.NotNull(OsxAssociationHelper.Find(bundle));

        Process server = null;
        var launchdVars = ExportLaunchdEnvironment(installDir);
        try {
            // register ONLY the outer bundle, as a portable zip install would: LaunchServices must find the nested helper itself
            Exe.InvokeAndThrowIfNonZero(LsRegister, ["-f", bundle], installDir);

            // (a) no instance running: the helper starts the app with the url, which handles it in-process and exits
            var first = $"{scheme}://first";
            OpenUrl(first, installDir);
            WaitForActivation(urlFile, $"OnUrlProto: {first}", 1, logFile, logger);
            logger.Info("TEST: the helper started the app for the first url");

            // (a)'s instance must be gone, or the waiting instance below would not be the one holding the endpoint lock
            TestHelper.WaitUntil(
                () => Assert.NotEqual(0, Exe.InvokeProcess("/usr/bin/pgrep", ["-f", appExe], installDir).ExitCode),
                pollDelayMs: 200);

            // (b) a running SingleInstance server receives the next activation on its IPC thread
            server = StartWaitingInstance(appExe, logger);
            var readyFile = Path.Combine(contents, "ready");
            TestHelper.WaitUntil(() => Assert.True(File.Exists(readyFile), "the waiting instance did not start"), pollDelayMs: 200);
            Assert.False(server.HasExited, "the waiting instance exited early");

            var second = $"{scheme}://second";
            OpenUrl(second, installDir);
            WaitForActivation(urlFile, $"OnUrlProto: {second}", 2, logFile, logger);
            Assert.Equal($"Pid: {server.Id}", TestHelper.ReadAllTextShared(pidFile).Trim().Split('\n').Last().Trim());
            logger.Info("TEST: the running instance received the forwarded url");

            File.WriteAllText(Path.Combine(contents, "stop"), "");
            Assert.True(server.WaitForExit(30_000), "the server instance did not stop");
            Assert.Equal(0, server.ExitCode);
        } finally {
            if (server is { HasExited: false }) {
                try { server.Kill(); } catch { }
            }

            server?.Dispose();
            CleanupActivationTest(bundle, installDir, launchdVars);
        }
    }

    [Fact]
    public async Task UrlActivationSurvivesUpdate()
    {
        // the production path that keeps a portable-zip install's helper registered when the bundle contents change:
        // UpdateMac `apply` calls LSRegisterURL on the new bundle and its helper(s) (util_osx.rs). No lsregister is
        // run here after the update, so a broken registration shows up as `open` failing or the url never arriving.
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");
        using var logger = _output.BuildLoggerFor<OsxPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);

        const string id = "OsxAssocUpdate";
        var scheme = $"velotest{TestHelper.RandomString(6)}";
        var bundle = Path.Combine(installDir, $"{id}.app");
        var contents = Path.Combine(bundle, "Contents");
        var appExe = Path.Combine(contents, "MacOS", "TestApp");
        var urlFile = Path.Combine(contents, "url-proto");
        var logFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Logs", $"velopack_{id}.log");

        await PackCSharpTestApp(id, "1.0.0", "version 1 test", releaseDir, logger, urlProtocols: scheme);
        ExtractDittoZip(Path.Combine(releaseDir, $"{id}-osx-Portable.zip"), installDir);
        var launchdVars = ExportLaunchdEnvironment(installDir);
        try {
            // the initial registration, as Finder's copy / the first launch would do it
            Exe.InvokeAndThrowIfNonZero(LsRegister, ["-f", bundle], installDir);

            await PackCSharpTestApp(id, "2.0.0", "version 2 test", releaseDir, logger, urlProtocols: scheme);
            TestHelper.RunNoCoverage(appExe, ["download", releaseDir], installDir, logger);
            TestHelper.RunNoCoverage(appExe, ["apply", releaseDir], installDir, logger, exitCode: null);
            TestHelper.WaitUntil(() => {
                var version = TestHelper.RunNoCoverage(appExe, ["version"], installDir, logger);
                Assert.EndsWith(Environment.NewLine + "2.0.0", version);
            }, pollDelayMs: 1000);
            logger.Info("TEST: v2 applied");

            var log = TestHelper.ReadAllTextShared(logFile);
            Assert.Contains("association helper(s) with LaunchServices", log);
            Assert.DoesNotContain("LSRegisterURL(", log);

            var url = $"{scheme}://after-update";
            OpenUrl(url, installDir);
            WaitForActivation(urlFile, $"OnUrlProto: {url}", 1, logFile, logger);
            logger.Info("TEST: the updated app received the url");
        } finally {
            CleanupActivationTest(bundle, installDir, launchdVars);
            try {
                var cacheDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Caches", "velopack", id);
                if (Directory.Exists(cacheDir))
                    Directory.Delete(cacheDir, true);
            } catch { }
        }
    }

    [Fact]
    public void BundleWithCustomPlistNamesHelperFromPlist()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "macOS only");
        using var logger = _output.BuildLoggerFor<OsxPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        using var _2 = TempUtil.GetTempDirectory(out var bundleDir);

        var rid = RID.Parse(VelopackRuntimeInfo.SystemRid);
        var publishDir = Path.Combine(workDir, "publish");
        TestApp.PreparePublishDir(rid, "version 1 test", publishDir, logger);

        // the user's plist identifies and names the app differently from the pack options: the helper must follow the
        // plist that ships, so it is matched to the app in Finder "Open With"
        var plist = Path.Combine(workDir, "Info.plist");
        File.WriteAllText(plist, """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE plist PUBLIC "-//Apple//DTD PLIST 1.0//EN" "http://www.apple.com/DTDs/PropertyList-1.0.dtd">
            <plist version="1.0">
            <dict>
              <key>CFBundleExecutable</key><string>TestApp</string>
              <key>CFBundleIdentifier</key><string>com.acme.app</string>
              <key>CFBundleName</key><string>Acme</string>
              <key>CFBundleDisplayName</key><string>Acme Studio</string>
              <key>CFBundlePackageType</key><string>APPL</string>
            </dict>
            </plist>
            """);

        var appDir = new OsxBundleCommandRunner(logger).Bundle(new OsxBundleOptions {
            EntryExecutableName = "TestApp",
            ReleaseDir = new DirectoryInfo(bundleDir),
            PackId = "AcmeStudio",
            PackVersion = "1.0.0",
            PackDirectory = publishDir,
            InfoPlistPath = plist,
            FileAssociations = $".velotest{TestHelper.RandomString(6)}",
        });

        var helper = OsxAssociationHelper.Find(appDir);
        Assert.NotNull(helper);
        Assert.Equal("Acme Studio.app", Path.GetFileName(helper));
        var dict = LoadPlistDict(Path.Combine(helper, "Contents", "Info.plist"));
        Assert.Equal("com.acme.app.velopack-assoc", PlistValue(dict, "CFBundleIdentifier")?.Value);
        Assert.Equal("Acme Studio", PlistValue(dict, "CFBundleName")?.Value);
        Assert.Equal("Acme Studio", PlistValue(dict, "CFBundleDisplayName")?.Value);
        Assert.Null(PlistValue(LoadPlistDict(Path.Combine(appDir, "Contents", "Info.plist")), "CFBundleDocumentTypes"));
    }

    /// <summary>
    /// `open` fails ("No application knows how to open URL", exit 1) exactly when LaunchServices did not pick up the
    /// nested helper's CFBundleURLTypes, which is what these tests exist to catch: a failure is only tolerated locally
    /// (eg. an ssh session without a GUI), never in CI.
    /// </summary>
    private static void OpenUrl(string url, string workingDir)
    {
        var open = Exe.InvokeProcess("/usr/bin/open", [url], workingDir);
        Assert.SkipWhen(!PathHelper.IsCI && open.ExitCode != 0, $"open failed (no GUI session?): {open.StdOutput} {open.StdErr}");
        Assert.True(open.ExitCode == 0, $"open '{url}' failed ({open.ExitCode}): {open.StdOutput} {open.StdErr}");
    }

    /// <summary> LS-launched processes do not inherit the job environment, and the framework-dependent TestApp needs DOTNET_ROOT. </summary>
    private static List<string> ExportLaunchdEnvironment(string workingDir)
    {
        var launchdVars = new List<string>();
        foreach (var name in new[] { "DOTNET_ROOT", "PATH" }) {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrEmpty(value)) {
                Exe.InvokeAndThrowIfNonZero("launchctl", ["setenv", name, value], workingDir);
                launchdVars.Add(name);
            }
        }

        return launchdVars;
    }

    private static void CleanupActivationTest(string bundle, string workingDir, List<string> launchdVars)
    {
        try { Exe.InvokeProcess(LsRegister, ["-u", bundle], workingDir); } catch { }
        try { Exe.InvokeProcess("/usr/bin/pkill", ["-f", bundle], workingDir); } catch { }
        foreach (var name in launchdVars) {
            try { Exe.InvokeProcess("launchctl", ["unsetenv", name], workingDir); } catch { }
        }
    }

    private static Process StartWaitingInstance(string appExe, ILogger logger)
    {
        var psi = new ProcessStartInfo(appExe) {
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(appExe)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.ArgumentList.Add("wait");
        var p = Process.Start(psi)!;
        p.OutputDataReceived += (_, e) => { if (e.Data != null) logger.Info("server: " + e.Data); };
        p.ErrorDataReceived += (_, e) => { if (e.Data != null) logger.Info("server: " + e.Data); };
        p.BeginOutputReadLine();
        p.BeginErrorReadLine();
        return p;
    }

    /// <summary>
    /// Waits (up to 60 s) for line <paramref name="lineNumber"/> of <paramref name="file"/> to be <paramref name="expected"/>;
    /// a timeout fails with the tail of the app + helper log, not just the timeout.
    /// </summary>
    private static void WaitForActivation(string file, string expected, int lineNumber, string logFile, ILogger logger)
    {
        var sw = Stopwatch.StartNew();
        string[] lines = [];
        while (sw.Elapsed < TimeSpan.FromSeconds(60)) {
            if (File.Exists(file)) {
                lines = TestHelper.ReadAllTextShared(file).Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0).ToArray();
                if (lines.Length >= lineNumber && lines[lineNumber - 1] == expected) {
                    return;
                }
            }

            Thread.Sleep(500);
        }

        var tail = File.Exists(logFile)
            ? string.Join(Environment.NewLine, TestHelper.ReadAllTextShared(logFile).Split('\n').TakeLast(60))
            : "(no log file)";
        logger.Error($"TEST: log tail of {logFile}:{Environment.NewLine}{tail}");
        Assert.Fail($"timed out waiting for '{expected}' as line {lineNumber} of {file} (got: [{string.Join(" | ", lines)}]). " +
                    $"Log tail of {logFile}:{Environment.NewLine}{tail}");
    }

    private static XElement LoadPlistDict(string plistPath) => XDocument.Load(plistPath).Root!.Element("dict")!;

    private static XElement PlistValue(XElement dict, string key)
    {
        return dict.Elements("key").FirstOrDefault(e => e.Value == key)?.ElementsAfterSelf().FirstOrDefault();
    }

    private async Task PackTestAppVariant(string variant, string id, string version, string testString, string releaseDir, ILogger logger)
    {
        if (variant == "csharp") {
            await PackCSharpTestApp(id, version, testString, releaseDir, logger);
        } else if (variant == "rust") {
            await PackRustTestApp(id, version, testString, releaseDir, logger);
        } else {
            throw new ArgumentException($"Unknown variant: {variant}");
        }
    }

    private static async Task PackRustTestApp(string id, string version, string testString, string releaseDir, ILogger logger)
    {
        using var _ = TempUtil.GetTempDirectory(out var packDir);

        var rustBinary = PathHelper.GetRustAsset("testapp");
        if (!File.Exists(rustBinary))
            throw new FileNotFoundException($"Rust testapp not found at: {rustBinary}. Run 'cargo build -p velopack_bins' first.");
        File.Copy(rustBinary, Path.Combine(packDir, "testapp"));
        Chmod.ChmodFileAsExecutable(Path.Combine(packDir, "testapp"));

        File.WriteAllText(Path.Combine(packDir, "test_string.txt"), testString);

        logger.Info($"TEST: Packing Rust testapp v{version} with test string '{testString}'");

        var rid = RID.Parse(VelopackRuntimeInfo.SystemRid);
        var console = new Velopack.Vpk.Logging.BasicConsole(logger, new Velopack.Vpk.VelopackDefaults(false));
        var options = new Velopack.Packaging.Unix.Commands.OsxPackOptions {
            EntryExecutableName = "testapp",
            ReleaseDir = new DirectoryInfo(releaseDir),
            PackId = id,
            PackVersion = version,
            TargetRuntime = rid,
            PackDirectory = packDir,
        };

        var runner = new Velopack.Packaging.Unix.Commands.OsxPackCommandRunner(logger, console);
        await runner.Run(options);
    }

    private static async Task PackCSharpTestApp(string id, string version, string testString, string releaseDir, ILogger logger,
        string packTitle = null, string fileAssociations = null, string urlProtocols = null,
        AssociationLaunchMode launchMode = AssociationLaunchMode.NewInstance)
    {
        using var _ = TempUtil.GetTempDirectory(out var workDir);

        {
            var rid = RID.Parse(VelopackRuntimeInfo.SystemRid);
            var publishDir = Path.Combine(workDir, "publish");
            TestApp.PreparePublishDir(rid, testString, publishDir, logger);

            var console = new Velopack.Vpk.Logging.BasicConsole(logger, new Velopack.Vpk.VelopackDefaults(false));
            var options = new Velopack.Packaging.Unix.Commands.OsxPackOptions {
                EntryExecutableName = "TestApp",
                ReleaseDir = new DirectoryInfo(releaseDir),
                PackId = id,
                PackVersion = version,
                TargetRuntime = rid,
                PackDirectory = publishDir,
                PackTitle = packTitle,
                FileAssociations = fileAssociations,
                UrlProtocols = urlProtocols,
                AssociationLaunchMode = launchMode,
            };

            var runner = new Velopack.Packaging.Unix.Commands.OsxPackCommandRunner(logger, console);
            await runner.Run(options);
        }
    }

    private static void ExtractDittoZip(string zipPath, string destDir)
    {
        Exe.InvokeAndThrowIfNonZero("ditto", ["-xk", zipPath, destDir], null);
    }
}
