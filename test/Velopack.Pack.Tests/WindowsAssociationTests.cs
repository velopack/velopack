using System.Diagnostics;
using System.Runtime.Versioning;
using System.Xml.Linq;
using Microsoft.Win32;
using NuGet.Packaging;
using Velopack.Core;
using Velopack.Packaging.Compression;
using Velopack.TestCommon;
using Velopack.Util;
using Velopack.Vpk.Logging;

namespace Velopack.Pack.Tests;

// End-to-end coverage of file association / URL protocol registration: vpk writes the nuspec metadata, Update.exe
// registers HKCU\Software\Classes keys on install, syncs them on update and removes them on uninstall, and
// VelopackApp recovers the opened value from the raw command line and invokes the OnFileAssoc /
// OnUrlProto callbacks (TestApp writes the value it received to a file next to the install dir).
[SupportedOSPlatform("windows")]
public class WindowsAssociationTests
{
    private const string FileAssocFlag = "--veloapp-fileassoc";
    private const string UrlProtoFlag = "--veloapp-urlproto";
    private const string RootDirValue = "VelopackRootDir";

    private readonly ITestOutputHelper _output;

    public WindowsAssociationTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Fact]
    public async Task AssociationsAreRegisteredSyncedAndRemoved()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsAssociationTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);
        using var _3 = TempUtil.GetTempDirectory(out var unzipDir);

        // unique names per run, so a leftover registration from an aborted run can never satisfy an assertion
        var suffix = TestHelper.RandomString(8);
        var id = "WinAssocTest";
        var extKeep = $".vpkassoc{suffix}a";
        var extDrop = $".vpkassoc{suffix}b";
        var extNew = $".vpkassoc{suffix}c";
        var schemeKeep = $"vpkassoc{suffix}a";
        var schemeDrop = $"vpkassoc{suffix}b";
        var unrelatedKey = $"VelopackTestUnrelated.{suffix}";
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");
        var updatePath = Path.Combine(installDir, "Update.exe");

        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes", true);
        try {
            // an unrelated key which must survive everything Update.exe does
            using (var unrelated = classes.CreateSubKey(unrelatedKey)) {
                unrelated.SetValue("", "keep me");
            }

            // pack v1 and check the nuspec metadata
            await WindowsPackTests.PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger,
                fileAssociations: $"{extKeep},{extDrop}", urlProtocols: $"{schemeKeep},{schemeDrop}");
            EasyZip.ExtractZipToDirectory(logger.ToVelopackLogger(), Path.Combine(releaseDir, $"{id}-1.0.0-full.nupkg"), unzipDir);
            var metadata = XDocument.Load(Path.Combine(unzipDir, $"{id}.nuspec")).Root.ElementsNoNamespace("metadata").Single();
            Assert.Equal($"{extKeep},{extDrop}", metadata.ElementsNoNamespace("fileAssociations").Single().Value);
            Assert.Equal($"{schemeKeep},{schemeDrop}", metadata.ElementsNoNamespace("urlProtocols").Single().Value);
            logger.Info("TEST: v1 nuspec verified");

            // install v1
            WindowsTestHelper.RunNoCoverage(
                Path.Combine(releaseDir, $"{id}-win-Setup.exe"),
                ["--silent", "--installto", installDir],
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                logger);
            Assert.True(File.Exists(appPath));

            AssertProtocolRegistered(classes, schemeKeep, id, appPath, installDir);
            AssertProtocolRegistered(classes, schemeDrop, id, appPath, installDir);
            AssertFileAssociationRegistered(classes, extKeep, id, appPath, installDir);
            AssertFileAssociationRegistered(classes, extDrop, id, appPath, installDir);
            logger.Info("TEST: v1 registrations verified");

            // open a URL via the registered command. The URL contains spaces and double quotes which argv parsing would
            // mangle, so an exact match proves VelopackApp recovered it from the raw command line.
            var url = $"{schemeKeep}://open?title=\"hi there\"&id=1";
            LaunchRegisteredCommand(classes, $@"{schemeKeep}\shell\open\command", url, logger);
            var urlFile = Path.Combine(installDir, "url-proto");
            TestHelper.WaitUntil(() => Assert.Equal($"OnUrlProto: {url}", TestHelper.ReadAllTextShared(urlFile).Trim()));
            logger.Info("TEST: url callback verified");

            // open a file (path with spaces) via the ProgId command
            var docPath = Path.Combine(installDir, $"my document{extKeep}");
            File.WriteAllText(docPath, "hello");
            LaunchRegisteredCommand(classes, $@"{ProgId(id, extKeep)}\shell\open\command", docPath, logger);
            var fileFile = Path.Combine(installDir, "file-assoc");
            TestHelper.WaitUntil(() => Assert.Equal($"OnFileAssoc: {docPath}", TestHelper.ReadAllTextShared(fileFile).Trim()));
            logger.Info("TEST: file callback verified");

            // pack v2 with a changed set and update to it
            await WindowsPackTests.PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger,
                fileAssociations: $"{extKeep},{extNew}", urlProtocols: schemeKeep);
            WindowsTestHelper.RunCoveredDotnet(appPath, ["download", releaseDir], installDir, logger);
            WindowsTestHelper.RunCoveredDotnet(appPath, ["apply", releaseDir], installDir, logger, exitCode: null);

            // update.exe swaps the app in a separate process and syncs the associations well after the directory swap
            // (after the update hooks and shortcut rewrite); the app is only relaunched once all of that is done, so the
            // "restarted" file written by OnRestarted is the end state to wait for
            var restartedPath = Path.Combine(installDir, "restarted");
            TestHelper.WaitUntil(() => Assert.True(File.Exists(restartedPath)), pollDelayMs: 1000);
            var version = WindowsTestHelper.RunNoCoverage(appPath, ["version"], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "2.0.0", version);
            logger.Info("TEST: v2 applied");

            AssertProtocolRegistered(classes, schemeKeep, id, appPath, installDir);
            AssertFileAssociationRegistered(classes, extKeep, id, appPath, installDir);
            AssertFileAssociationRegistered(classes, extNew, id, appPath, installDir);
            AssertProtocolRemoved(classes, schemeDrop);
            AssertFileAssociationRemoved(classes, extDrop, id);
            logger.Info("TEST: v2 registrations verified");

            // uninstall
            WindowsTestHelper.RunNoCoverage(updatePath, ["--silent", "--uninstall"], Environment.CurrentDirectory, logger);
            AssertProtocolRemoved(classes, schemeKeep);
            AssertProtocolRemoved(classes, schemeDrop);
            AssertFileAssociationRemoved(classes, extKeep, id);
            AssertFileAssociationRemoved(classes, extDrop, id);
            AssertFileAssociationRemoved(classes, extNew, id);

            using (var unrelated = classes.OpenSubKey(unrelatedKey)) {
                Assert.NotNull(unrelated);
                Assert.Equal("keep me", unrelated.GetValue(""));
            }

            logger.Info("TEST: uninstall verified / complete");
        } finally {
            foreach (var name in new[] {
                         unrelatedKey, schemeKeep, schemeDrop, extKeep, extDrop, extNew,
                         ProgId(id, extKeep), ProgId(id, extDrop), ProgId(id, extNew)
                     }) {
                try {
                    classes.DeleteSubKeyTree(name, throwOnMissingSubKey: false);
                } catch (Exception ex) {
                    logger.Warn($"TEST: failed to clean up HKCU\\Software\\Classes\\{name}: {ex.Message}");
                }
            }
        }
    }

    [Fact]
    public async Task UrlProtoLaunchSurvivesAutoApply()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");
        using var logger = _output.BuildLoggerFor<WindowsAssociationTests>();
        using var _1 = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var installDir);

        var suffix = TestHelper.RandomString(8);
        var id = "WinAssocUpdateTest";
        var scheme = $"vpkassoc{suffix}u";
        var appPath = Path.Combine(installDir, "current", "TestApp.exe");

        using var classes = Registry.CurrentUser.CreateSubKey(@"Software\Classes", true);
        try {
            // install v1 with a URL protocol
            await WindowsPackTests.PackTestApp(id, "1.0.0", "version 1 test", releaseDir, logger, urlProtocols: scheme);
            WindowsTestHelper.RunNoCoverage(
                Path.Combine(releaseDir, $"{id}-win-Setup.exe"),
                ["--silent", "--installto", installDir],
                Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
                logger);
            AssertProtocolRegistered(classes, scheme, id, appPath, installDir);

            // make v2 pending: VelopackApp auto-applies a newer full package found in the packages dir at startup
            await WindowsPackTests.PackTestApp(id, "2.0.0", "version 2 test", releaseDir, logger, urlProtocols: scheme);
            var packagesDir = Path.Combine(installDir, "packages");
            Directory.CreateDirectory(packagesDir);
            File.Copy(Path.Combine(releaseDir, $"{id}-2.0.0-full.nupkg"), Path.Combine(packagesDir, $"{id}-2.0.0-full.nupkg"), true);

            // open a URL (with spaces and double quotes) via the registered command: v1 recovers it from the raw command
            // line, restarts through Update.exe to apply v2 and forwards it as [flag, value]; v2 receives it from argv
            var url = $"{scheme}://open?title=\"hi there\"&id=1";
            LaunchRegisteredCommand(classes, $@"{scheme}\shell\open\command", url, logger);

            var urlFile = Path.Combine(installDir, "url-proto");
            TestHelper.WaitUntil(() => Assert.Equal($"OnUrlProto: {url}", TestHelper.ReadAllTextShared(urlFile).Trim()), pollDelayMs: 1000);
            // the callback ran in the restarted v2 process, after its OnRestarted hook
            Assert.True(File.Exists(Path.Combine(installDir, "restarted")), "OnRestarted did not run");
            var version = WindowsTestHelper.RunNoCoverage(appPath, ["version"], installDir, logger);
            Assert.EndsWith(Environment.NewLine + "2.0.0", version);
            logger.Info("TEST: url launch survived the auto-applied update");
        } finally {
            try {
                classes.DeleteSubKeyTree(scheme, throwOnMissingSubKey: false);
            } catch (Exception ex) {
                logger.Warn($"TEST: failed to clean up HKCU\\Software\\Classes\\{scheme}: {ex.Message}");
            }
        }
    }

    // mirrors make_progid in src/bins/src/windows/associations.rs for ids/extensions which need no sanitizing
    private static string ProgId(string id, string ext) => $"{id}.{ext.TrimStart('.')}";

    private static void AssertProtocolRegistered(RegistryKey classes, string scheme, string title, string appPath, string installDir)
    {
        using var key = classes.OpenSubKey(scheme);
        Assert.True(key != null, $"protocol key '{scheme}' is missing");
        Assert.Equal($"URL:{title}", key.GetValue(""));
        Assert.Equal("", key.GetValue("URL Protocol"));
        AssertRootDir(key, installDir);
        AssertHandlerKeys(key, appPath, UrlProtoFlag);
    }

    private static void AssertFileAssociationRegistered(RegistryKey classes, string ext, string id, string appPath, string installDir)
    {
        var progId = ProgId(id, ext);
        using var progIdKey = classes.OpenSubKey(progId);
        Assert.True(progIdKey != null, $"ProgId key '{progId}' is missing");
        Assert.Equal($"{id} {ext.TrimStart('.').ToUpperInvariant()} File", progIdKey.GetValue(""));
        AssertRootDir(progIdKey, installDir);
        AssertHandlerKeys(progIdKey, appPath, FileAssocFlag);

        using var extKey = classes.OpenSubKey(ext);
        Assert.True(extKey != null, $"extension key '{ext}' is missing");
        // nobody else handles our random extension, so we must have become the default
        Assert.Equal(progId, extKey.GetValue(""));
        using var openWith = extKey.OpenSubKey("OpenWithProgIds");
        Assert.True(openWith != null, $"'{ext}\\OpenWithProgIds' is missing");
        Assert.Contains(progId, openWith.GetValueNames());
    }

    private static void AssertRootDir(RegistryKey key, string installDir)
    {
        var rootDir = Assert.IsType<string>(key.GetValue(RootDirValue));
        Assert.Equal(
            Path.GetFullPath(installDir).TrimEnd('\\'),
            Path.GetFullPath(rootDir).TrimEnd('\\'),
            StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertHandlerKeys(RegistryKey key, string appPath, string flag)
    {
        using var icon = key.OpenSubKey("DefaultIcon");
        Assert.True(icon != null, $"'{key.Name}\\DefaultIcon' is missing");
        Assert.Equal($"\"{appPath}\",0", icon.GetValue("") as string, StringComparer.OrdinalIgnoreCase);

        using var command = key.OpenSubKey(@"shell\open\command");
        Assert.True(command != null, $"'{key.Name}\\shell\\open\\command' is missing");
        Assert.Equal($"\"{appPath}\" {flag} \"%1\"", command.GetValue("") as string, StringComparer.OrdinalIgnoreCase);
    }

    private static void AssertProtocolRemoved(RegistryKey classes, string scheme)
    {
        using var key = classes.OpenSubKey(scheme);
        Assert.True(key == null, $"protocol key '{scheme}' should have been removed");
    }

    private static void AssertFileAssociationRemoved(RegistryKey classes, string ext, string id)
    {
        var progId = ProgId(id, ext);
        using var progIdKey = classes.OpenSubKey(progId);
        Assert.True(progIdKey == null, $"ProgId key '{progId}' should have been removed");
        // nothing else referenced the extension, so the whole key goes too
        using var extKey = classes.OpenSubKey(ext);
        Assert.True(extKey == null, $"extension key '{ext}' should have been removed");
    }

    /// <summary>
    /// Launches the app the way the shell does for a registered verb: the command stored in the registry with %1
    /// substituted by the opened value, passed as one raw command line (no argv quoting applied to the value).
    /// </summary>
    private static void LaunchRegisteredCommand(RegistryKey classes, string commandKey, string value, ILogger logger)
    {
        using var key = classes.OpenSubKey(commandKey);
        Assert.True(key != null, $"'{commandKey}' is missing");
        var command = Assert.IsType<string>(key.GetValue(""));

        // "{exe}" {args}
        Assert.StartsWith("\"", command);
        var closingQuote = command.IndexOf('"', 1);
        Assert.True(closingQuote > 1, $"unexpected command format: {command}");
        var exe = command.Substring(1, closingQuote - 1);
        var arguments = command.Substring(closingQuote + 1).Trim().Replace("%1", value);

        // Process builds the command line as "{exe}" {arguments}, exactly what the shell would hand to CreateProcess
        var psi = new ProcessStartInfo(exe) {
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WorkingDirectory = Path.GetDirectoryName(exe)!,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        logger.Info($"TEST: Launching raw command line: \"{exe}\" {arguments}");
        using var p = Process.Start(psi)!;
        var stderr = p.StandardError.ReadToEndAsync();
        var stdout = p.StandardOutput.ReadToEnd();
        if (!p.WaitForExit((int) TimeSpan.FromMinutes(1).TotalMilliseconds)) {
            p.Kill();
            throw new TimeoutException("Association launch did not exit.");
        }

        logger.Info($"TEST: Association launch exited with {p.ExitCode}:{Environment.NewLine}{(stdout + stderr.Result).Trim()}");
        Assert.Equal(0, p.ExitCode);
    }
}
