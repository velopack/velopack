using System.IO.Compression;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;
using Velopack.Core;
using Velopack.Packaging;
using Velopack.Packaging.Unix;
using Velopack.Util;

using Velopack.TestCommon;
using static Velopack.TestCommon.OsxTestUtil;

namespace Velopack.CrossCompile.Tests;

public class CrossCompile
{
    // An entitlement the app's own entitlements carry and Velopack's (UpdateMac's) do not, so the macOS leg can tell that
    // each binary was signed with the right file. Harmless outside the app sandbox, which the TestApp does not opt into.
    private const string AppOnlyEntitlement = "com.apple.security.network.client";

    // One of Velopack's own entitlements (vendor/Velopack.entitlements); a .NET app with the hardened runtime needs it.
    private const string VelopackEntitlement = "com.apple.security.cs.allow-jit";

    // A Mach-O the deep row packs beside the main executable, as a native library or createdump would sit (see
    // PackCrossAppOsxSigned), so the macOS leg can check that the deep route gave it the hardened runtime.
    private const string DeepRouteExtraMachO = "helper";

    // A directory of data files the signed rows pack beside the main executable, as satellite assemblies or content
    // would sit: holding no Mach-O, vpk moves it out of Contents/MacOS whole and links it as a directory before rcodesign
    // signs, so the macOS leg checks that a directory link is sealed and followed too (files are linked one by one).
    private const string SignedRowsDataDirectory = "data";
    private const string SignedRowsDataFile = "notes.txt";
    private const string SignedRowsDataContent = "packed beside the executable";

    private readonly ITestOutputHelper _output;

    public CrossCompile(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Packs v1 of an app for the target OS and publishes its installer as {id}.exe / {id}.AppImage, plus an
    /// update feed directory {id}-feed holding v2, to the artifacts dir. The "rust" variant packs the rust
    /// testapp, which (unlike the .NET 8 TestApp) also runs on Windows 7; its win-x86 build gets a "-x86" id suffix.
    /// </summary>
    [Theory]
    [InlineData("win-x64", "csharp")]
    [InlineData("win-x64", "rust")]
    [InlineData("win-x86", "rust")]
    [InlineData("linux-x64", "csharp")]
    [InlineData("osx-arm64", "csharp")]
    public void PackCrossApp(string target, string variant)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        var rid = RID.Parse(target);
        if (rid.BaseRID == RuntimeOs.OSX) {
            TestHelper.SkipLocallyFailInCI(TestApp.GetOsxPackBlocker(logger));
        }

        string id = $"from-{VelopackRuntimeInfo.SystemOs.GetOsShortName()}-targets-{rid.BaseRID.GetOsShortName()}";
        if (variant == "rust") id += "-rust";
        if (variant == "rust" && rid.Architecture == RuntimeCpu.x86) {
            id += "-x86";
            // only CI downloads the x86 testapp (testapp_x86.exe); a missing binary there must fail, not skip
            Assert.SkipWhen(
                !PathHelper.IsCI && TestApp.FindRustTestAppWindows(RuntimeCpu.x86) == null,
                "Rust testapp_x86.exe not built locally");
        }

        using var _1 = TempUtil.GetTempDirectory(out var tempDir);
        void pack(string version, string testString)
        {
            if (variant == "rust") {
                TestApp.PackRustTestAppWindows(id, version, testString, tempDir, logger, rid.Architecture);
            } else {
                // only the portable zip goes to the macOS leg; the .pkg (built on macOS) is covered by Velopack.Pack.Tests
                TestApp.PackTestApp(id, version, testString, tempDir, logger, targetRid: rid, configureOsx: options => options.NoInst = true);
            }
        }

        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        Directory.CreateDirectory(artifactsDir);

        pack("1.0.0", id);

        string src, dest;
        if (rid.BaseRID == RuntimeOs.Windows) {
            src = Path.Combine(tempDir, id + "-win-Setup.exe");
            dest = Path.Combine(artifactsDir, id + ".exe");
        } else if (rid.BaseRID == RuntimeOs.OSX) {
            src = Path.Combine(tempDir, id + "-osx-Portable.zip");
            dest = Path.Combine(artifactsDir, id + ".zip");
        } else {
            src = Path.Combine(tempDir, id + ".AppImage");
            dest = Path.Combine(artifactsDir, id + ".AppImage");
        }

        Assert.True(File.Exists(src), $"Expected {src} to exist");
        File.Copy(src, dest, overwrite: true);

        // v2 lands in the same release dir, so the feed also gets a v1 -> v2 delta
        pack("2.0.0", id + "-v2");

        var feedDir = Path.Combine(artifactsDir, id + "-feed");
        IoUtil.DeleteFileOrDirectoryHard(feedDir);
        Directory.CreateDirectory(feedDir);
        var feedFiles = Directory.GetFiles(tempDir, "releases.*.json").Concat(Directory.GetFiles(tempDir, "*.nupkg")).ToArray();
        // non-Windows channels are part of the file name, e.g. {id}-2.0.0-linux-full.nupkg
        Assert.Contains(feedFiles, f => Path.GetFileName(f).StartsWith($"{id}-2.0.0-") && f.EndsWith("-full.nupkg"));
        foreach (var file in feedFiles) {
            File.Copy(file, Path.Combine(feedDir, Path.GetFileName(file)));
        }
    }

    /// <summary>
    /// Packs the macOS TestApp off macOS, signed with rcodesign by a self-signed certificate made here (not notarized:
    /// that needs Apple credentials), once on the default deep route and once with --signDisableDeep. The app gets its own
    /// entitlements file, so the macOS leg can check UpdateMac kept Velopack's.
    ///
    /// The deep row also packs <see cref="DeepRouteExtraMachO"/>, a copy of UpdateMac with only the linker's ad-hoc
    /// signature: only the deep route's scope for loose Mach-O files hardens it, so the macOS leg fails if that scope is
    /// lost or does not match on Windows. The --signDisableDeep route keeps such a binary's flags by design, so it is left out.
    ///
    /// This is an ordinary (not single-file) publish, so the .dll, .json, .pdb and test_string.txt land beside the exe in
    /// Contents/MacOS, where rcodesign cannot seal them: vpk must move them into Contents/Resources and link them, and the
    /// macOS leg checks the links are sealed and that the app still finds its files through them. Both rows also pack
    /// <see cref="SignedRowsDataDirectory"/>, which is moved and linked as a whole directory.
    /// </summary>
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PackCrossAppOsxSigned(bool signDisableDeep)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        Assert.SkipWhen(VelopackRuntimeInfo.IsOSX, "macOS signs with codesign (Velopack.Pack.Tests); rcodesign is the route off macOS");
        TestHelper.SkipLocallyFailInCI(TestApp.GetOsxPackBlocker(logger));
        TestHelper.SkipLocallyFailInCI(GetRcodesignBlocker());

        var rid = RID.Parse("osx-arm64");
        string id = $"from-{VelopackRuntimeInfo.SystemOs.GetOsShortName()}-targets-osx-signed-{(signDisableDeep ? "nodeep" : "deep")}";
        using var _1 = TempUtil.GetTempDirectory(out var tempDir);
        using var _2 = TempUtil.GetTempDirectory(out var signingDir);

        var (p12File, p12PasswordFile) = CreateSelfSignedCodeSigningP12(signingDir);
        var appEntitlements = CreateAppEntitlements(signingDir);

        TestApp.PackTestApp(
            id, "1.0.0", id, tempDir, logger, targetRid: rid,
            configureOsx: options => {
                if (!signDisableDeep) {
                    File.Copy(HelperFile.GetUpdatePath(rid, logger), Path.Combine(options.PackDirectory, DeepRouteExtraMachO));
                }

                var dataDir = Directory.CreateDirectory(Path.Combine(options.PackDirectory, SignedRowsDataDirectory));
                File.WriteAllText(Path.Combine(dataDir.FullName, SignedRowsDataFile), SignedRowsDataContent);

                options.SignP12File = p12File;
                options.SignP12PasswordFile = p12PasswordFile;
                options.SignEntitlements = appEntitlements;
                options.SignDisableDeep = signDisableDeep;
            });

        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        Directory.CreateDirectory(artifactsDir);

        var src = Path.Combine(tempDir, id + "-osx-Portable.zip");
        Assert.True(File.Exists(src), $"Expected {src} to exist");
        var expectedMachOs = signDisableDeep
            ? new[] { "TestApp", "UpdateMac" }
            : new[] { "TestApp", "UpdateMac", DeepRouteExtraMachO };
        AssertMacOSDirIsSealable(src, id, signingDir, expectedMachOs);
        File.Copy(src, Path.Combine(artifactsDir, id + ".zip"), overwrite: true);
    }

    /// <summary>
    /// Asserts that everything in the zipped bundle's Contents/MacOS is a Mach-O file or a symlink: anything else is what
    /// rcodesign leaves out of the signature, with only a warning. Each of <paramref name="expectedMachOs"/> must be
    /// there, and every link but sq.version must point at the same path under Contents/Resources/MacOS, which the zip
    /// must hold: the files vpk moved out of the way (the .dll, test_string.txt...) and <see cref="SignedRowsDataDirectory"/>.
    /// </summary>
    private static void AssertMacOSDirIsSealable(string portableZip, string id, string scratchDir, string[] expectedMachOs)
    {
        var macosDir = $"{id}.app/Contents/MacOS/";
        var relocatedDir = $"{id}.app/Contents/{RcodesignTools.RelocatedResourcesDirectory}/";
        var scratchFile = Path.Combine(scratchDir, "macho-check");

        using var zip = ZipFile.OpenRead(portableZip);
        var entries = zip.Entries.ToDictionary(e => e.FullName);
        var files = entries.Values
            .Where(e => e.FullName.StartsWith(macosDir, StringComparison.Ordinal) && !e.FullName.EndsWith('/'))
            .ToArray();
        foreach (var expected in expectedMachOs) {
            Assert.Contains(files, e => e.FullName == macosDir + expected);
        }

        var unsealable = new List<string>();
        var links = new List<string>();
        foreach (var entry in files) {
            var name = entry.FullName.Substring(macosDir.Length);
            var type = UnixModeOf(entry) & S_IFMT;
            if (type == S_IFLNK) {
                if (name != "sq.version") {
                    links.Add(name);
                    Assert.Equal($"../{RcodesignTools.RelocatedResourcesDirectory}/{name}", ReadText(entry));
                    Assert.True(
                        entries.ContainsKey(relocatedDir + name) || entries.ContainsKey(relocatedDir + name + "/"),
                        $"Expected the zip to hold {relocatedDir + name}, the target of the link {entry.FullName}");
                }

                continue;
            }

            if (type == S_IFREG) {
                entry.ExtractToFile(scratchFile, overwrite: true);
                if (BinDetect.IsMachOImage(scratchFile)) {
                    continue;
                }
            }

            unsealable.Add(name);
        }

        Assert.True(
            unsealable.Count == 0,
            $"Expected only Mach-O files and links in {macosDir}, as rcodesign leaves anything else unsealed, " +
            $"but found: {String.Join(", ", unsealable)}");

        // the publish's loose files were moved and linked one by one, the data directory as a whole
        Assert.Contains("TestApp.dll", links);
        Assert.Contains(TestApp.TestStringFileName, links);
        Assert.Contains(SignedRowsDataDirectory, links);
        Assert.Equal(S_IFREG, UnixModeOf(entries[$"{relocatedDir}{SignedRowsDataDirectory}/{SignedRowsDataFile}"]) & S_IFMT);
        Assert.DoesNotContain(entries.Keys, e => e.StartsWith($"{macosDir}{SignedRowsDataDirectory}/", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("from-win-targets-linux")]
    [InlineData("from-linux-targets-linux")]
    [InlineData("from-osx-targets-linux")]
    public void RunCrossAppLinux(string artifactId)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        Assert.SkipWhen(
            String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOPACK_CROSS_ARTIFACTS")),
            "VELOPACK_CROSS_ARTIFACTS not set");
        Assert.SkipUnless(VelopackRuntimeInfo.IsLinux, "AppImage's can only run on Linux");

        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        var artifactPath = Path.Combine(artifactsDir, artifactId + ".AppImage");
        var feedDir = Path.Combine(artifactsDir, artifactId + "-feed");
        Assert.True(File.Exists(artifactPath), $"Expected {artifactPath} to exist");

        // applying an update replaces the AppImage file, so work on a copy
        using var _1 = TempUtil.GetTempDirectory(out var tempDir);
        var appImage = Path.Combine(tempDir, artifactId + ".AppImage");
        File.Copy(artifactPath, appImage);
        Chmod.ChmodFileAsExecutable(appImage);
        IoUtil.DeleteFileOrDirectoryHard($"/var/tmp/velopack/{artifactId}");

        string run(params string[] args)
        {
            var output = Exe.InvokeAndThrowIfNonZero(appImage, args, tempDir);
            logger.LogInformation(output);
            return output.Trim();
        }

        Assert.EndsWith(artifactId, run("test"));
        Assert.EndsWith("update: 2.0.0", run("check", feedDir));
        run("download", feedDir);
        Exe.InvokeProcess(appImage, ["apply", feedDir], tempDir);

        // the update binary swaps the AppImage in a separate process; poll until the new version is live
        TestHelper.WaitUntil(() => Assert.EndsWith("2.0.0", run("version")), timeoutMs: 60_000, pollDelayMs: 1000);
        Assert.EndsWith(artifactId + "-v2", run("test"));
    }

    [Theory]
    [InlineData("from-win-targets-win")]
    [InlineData("from-linux-targets-win")]
    [InlineData("from-osx-targets-win")]
    [InlineData("from-win-targets-win-rust")]
    [InlineData("from-linux-targets-win-rust")]
    [InlineData("from-osx-targets-win-rust")]
    [InlineData("from-win-targets-win-rust-x86")]
    [InlineData("from-linux-targets-win-rust-x86")]
    [InlineData("from-osx-targets-win-rust-x86")]
    public void RunCrossAppWindows(string artifactId)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        Assert.SkipWhen(
            String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOPACK_CROSS_ARTIFACTS")),
            "VELOPACK_CROSS_ARTIFACTS not set");
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "PE files can only run on Windows");

        InstallUpdateUninstall(new LocalWindowsHost(), artifactId, logger);
    }

    /// <summary>
    /// Runs the rust-testapp installers on a Windows 7 VM over SSH (see the win7 job in build-tests.yml);
    /// the .NET 8 TestApp variants are excluded since .NET 8 does not support Windows 7. The VM is x64, so the
    /// x86 packages (x86 update.exe/setup.exe/testapp.exe) run under WOW64.
    /// </summary>
    [Theory]
    [InlineData("from-win-targets-win-rust")]
    [InlineData("from-linux-targets-win-rust")]
    [InlineData("from-osx-targets-win-rust")]
    [InlineData("from-win-targets-win-rust-x86")]
    [InlineData("from-linux-targets-win-rust-x86")]
    [InlineData("from-osx-targets-win-rust-x86")]
    public void RunCrossAppWindows7(string artifactId)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        var connection = Environment.GetEnvironmentVariable("VELOPACK_WIN7_SSH");
        Assert.SkipWhen(String.IsNullOrWhiteSpace(connection), "VELOPACK_WIN7_SSH not set");

        InstallUpdateUninstall(new SshWindowsHost(connection!, logger), artifactId, logger);
    }

    private static void InstallUpdateUninstall(IWindowsHost host, string artifactId, ILogger logger)
    {
        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        var artifactPath = Path.Combine(artifactsDir, artifactId + ".exe");
        var feedPath = Path.Combine(artifactsDir, artifactId + "-feed");
        Assert.True(File.Exists(artifactPath), $"Expected {artifactPath} to exist");
        Assert.True(Directory.Exists(feedPath), $"Expected {feedPath} to exist");

        var appRoot = host.Combine(host.LocalAppData, artifactId);
        var appExe = host.Combine(appRoot, "current", artifactId.Contains("-rust") ? "testapp.exe" : "TestApp.exe");
        var appUpdate = host.Combine(appRoot, "Update.exe");

        host.Delete(appRoot);
        Assert.False(host.Exists(appExe));

        // install v1
        var setup = host.Stage(artifactPath);
        var feed = host.Stage(feedPath);
        host.RunAndThrowIfNonZero(setup, "--silent");
        Assert.True(host.Exists(appExe), $"Expected {appExe} to exist after install");
        Assert.EndsWith(artifactId, host.RunAndThrowIfNonZero(appExe, "test").Trim());
        logger.Info($"TEST: {artifactId} v1 installed");

        // update to v2
        Assert.EndsWith("update: 2.0.0", host.RunAndThrowIfNonZero(appExe, "check", feed).Trim());
        host.RunAndThrowIfNonZero(appExe, "download", feed);
        // apply hands off to Update.exe and exits; its exit code is not meaningful
        host.Run(appExe, "apply", feed);

        // Update.exe swaps the app in a separate process; poll until the new version is live
        TestHelper.WaitUntil(
            () => Assert.EndsWith("2.0.0", host.RunAndThrowIfNonZero(appExe, "version").Trim()),
            timeoutMs: 60_000,
            pollDelayMs: 1000);
        Assert.EndsWith(artifactId + "-v2", host.RunAndThrowIfNonZero(appExe, "test").Trim());
        logger.Info($"TEST: {artifactId} updated to v2");

        // uninstall
        host.Run(appUpdate, "--uninstall", "--silent");

        // the uninstaller schedules the rmdir of its own directory ~3s after it exits; poll for it
        TestHelper.WaitUntil(() => Assert.False(host.Exists(appRoot)), timeoutMs: 30_000, pollDelayMs: 1000);
    }

    /// <summary>
    /// Expands a macOS portable zip as Finder does (ditto), checks the executable bits and the sq.version symlink, and
    /// runs the app. Signed artifacts must also pass codesign's verification, with the hardened runtime on every Mach-O
    /// and the expected entitlements, and hold the non-Mach-O files vpk moved out of Contents/MacOS as links into
    /// Contents/Resources, which the app must find its files through. The unsigned artifacts only run on Apple Silicon
    /// because the .NET 9+ SDK ad-hoc signs the osx apphost on any host (and macOS's linker ad-hoc signs UpdateMac).
    /// </summary>
    [Theory]
    [InlineData("from-win-targets-osx")]
    [InlineData("from-linux-targets-osx")]
    [InlineData("from-osx-targets-osx")]
    [InlineData("from-win-targets-osx-signed-deep")]
    [InlineData("from-win-targets-osx-signed-nodeep")]
    [InlineData("from-linux-targets-osx-signed-deep")]
    [InlineData("from-linux-targets-osx-signed-nodeep")]
    [SupportedOSPlatform("osx")]
    public void RunCrossAppOsx(string artifactId)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        Assert.SkipWhen(
            String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOPACK_CROSS_ARTIFACTS")),
            "VELOPACK_CROSS_ARTIFACTS not set");
        Assert.SkipUnless(VelopackRuntimeInfo.IsOSX, "Mach-O files can only run on macOS");

        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        var artifactPath = Path.Combine(artifactsDir, artifactId + ".zip");

        Assert.True(File.Exists(artifactPath), $"Expected {artifactPath} to exist");

        using var _1 = TempUtil.GetTempDirectory(out var extractDir);
        Exe.InvokeAndThrowIfNonZero("ditto", new[] { "-x", "-k", artifactPath, extractDir }, null);

        var bundle = Path.Combine(extractDir, artifactId + ".app");
        var macosDir = Path.Combine(bundle, "Contents", "MacOS");
        var appExe = Path.Combine(macosDir, "TestApp");
        var updateMac = Path.Combine(macosDir, "UpdateMac");
        Assert.True(Directory.Exists(bundle), $"Expected the zip to expand to {bundle}");

        var signed = artifactId.Contains("-signed-");
        var executables = new List<string> { appExe, updateMac };
        if (artifactId.EndsWith("-signed-deep", StringComparison.Ordinal)) {
            executables.Add(Path.Combine(macosDir, DeepRouteExtraMachO));
        }

        foreach (var exe in executables) {
            Assert.True(File.Exists(exe), $"Expected {exe} to exist");
            Assert.True(BinDetect.IsMachOImage(exe), $"Expected {exe} to be a Mach-O image");
            Assert.True(File.GetUnixFileMode(exe).HasFlag(UnixFileMode.UserExecute), $"Expected {exe} to be executable");
        }

        // the manifest lives in Resources and is linked into MacOS, where the locator reads it
        var manifestLink = new FileInfo(Path.Combine(macosDir, "sq.version"));
        Assert.True(manifestLink.LinkTarget != null, $"Expected {manifestLink.FullName} to be a symlink");
        Assert.Equal(
            Path.Combine(bundle, "Contents", "Resources", "sq.version"),
            Path.GetFullPath(Path.Combine(macosDir, manifestLink.LinkTarget)));
        Assert.Contains(artifactId, File.ReadAllText(manifestLink.FullName));

        // the app's own loose files: moved into Resources and linked when rcodesign signed, left in place otherwise
        var relocatedDir = Path.Combine(bundle, "Contents", Path.Combine(RcodesignTools.RelocatedResourcesDirectory.Split('/')));
        var appAssembly = new FileInfo(Path.Combine(macosDir, "TestApp.dll"));
        if (signed) {
            Assert.True(appAssembly.LinkTarget != null, $"Expected {appAssembly.FullName} to be a symlink");
            Assert.Equal(Path.Combine(relocatedDir, "TestApp.dll"), Path.GetFullPath(Path.Combine(macosDir, appAssembly.LinkTarget)));
            Assert.Null(new FileInfo(Path.Combine(relocatedDir, "TestApp.dll")).LinkTarget);

            var dataLink = new DirectoryInfo(Path.Combine(macosDir, SignedRowsDataDirectory));
            Assert.True(dataLink.LinkTarget != null, $"Expected {dataLink.FullName} to be a symlink");
            Assert.Equal(Path.Combine(relocatedDir, SignedRowsDataDirectory), Path.GetFullPath(Path.Combine(macosDir, dataLink.LinkTarget)));
            Assert.Equal(SignedRowsDataContent, File.ReadAllText(Path.Combine(dataLink.FullName, SignedRowsDataFile)));

            VerifySignedBundle(bundle, appExe, updateMac, executables, logger);
        } else {
            Assert.Null(appAssembly.LinkTarget);
            Assert.False(Directory.Exists(relocatedDir), $"Expected no {relocatedDir} in an unsigned bundle");
        }

        var version = Exe.InvokeAndThrowIfNonZero(appExe, new[] { "version" }, null);
        logger.LogInformation(version);
        Assert.EndsWith("1.0.0", version.Trim());

        // test_string.txt is read from beside the exe: through a link, when signed
        var output = Exe.InvokeAndThrowIfNonZero(appExe, new[] { "test" }, null);
        logger.LogInformation(output);
        Assert.EndsWith(artifactId, output.Trim());
    }

    [SupportedOSPlatform("osx")]
    private static void VerifySignedBundle(string bundle, string appExe, string updateMac, IEnumerable<string> expectedMachOs, ILogger logger)
    {
        // Verifies the seal of the bundle and of everything nested in it. A self-signed certificate is enough for this:
        // codesign checks the signature against the certificate it carries; only Gatekeeper (spctl) asks who issued it.
        var verify = Codesign(logger, "--verify", "--deep", "--strict", "--verbose=2", bundle);
        Assert.True(verify.ExitCode == 0, $"codesign --verify failed:{Environment.NewLine}{verify.Output}");

        // notarization refuses any Mach-O without the hardened runtime, nested ones included
        var machOs = Directory.EnumerateFiles(bundle, "*", SearchOption.AllDirectories)
            .Where(f => new FileInfo(f).LinkTarget == null && BinDetect.IsMachOImage(f))
            .ToArray();
        foreach (var expected in expectedMachOs) {
            Assert.Contains(expected, machOs);
        }

        foreach (var machO in machOs) {
            var info = Codesign(logger, "--display", "--verbose=2", machO);
            Assert.True(info.ExitCode == 0, $"codesign --display failed for {machO}:{Environment.NewLine}{info.Output}");
            Assert.True(
                Regex.IsMatch(info.Output, @"flags=0x[0-9a-f]+\([^)]*\bruntime\b"),
                $"Expected {machO} to be signed with the hardened runtime:{Environment.NewLine}{info.Output}");
        }

        // the app's entitlements on the main executable, Velopack's own on UpdateMac
        var appEntitlements = Codesign(logger, "--display", "--entitlements", "-", "--xml", appExe);
        Assert.Contains(AppOnlyEntitlement, appEntitlements.Output);
        Assert.Contains(VelopackEntitlement, appEntitlements.Output);

        var updateEntitlements = Codesign(logger, "--display", "--entitlements", "-", "--xml", updateMac);
        Assert.Contains(VelopackEntitlement, updateEntitlements.Output);
        Assert.DoesNotContain(AppOnlyEntitlement, updateEntitlements.Output);
    }

    /// <summary> Runs codesign, returning stdout and stderr together: --display writes most of what it shows to stderr. </summary>
    private static (int ExitCode, string Output) Codesign(ILogger logger, params string[] args)
    {
        var result = Exe.InvokeProcess("codesign", args, null);
        var output = result.StdOutput + Environment.NewLine + result.StdErr;
        logger.LogInformation($"{result.Command}{Environment.NewLine}{output}");
        return (result.ExitCode, output);
    }

    /// <summary>
    /// A self-signed code signing certificate as a PKCS#12 file plus a password file, the form --signP12File and
    /// --signP12PasswordFile take.
    /// </summary>
    private static (string P12File, string PasswordFile) CreateSelfSignedCodeSigningP12(string dir)
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Velopack Cross-Compile Test", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        request.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(new OidCollection { new Oid("1.3.6.1.5.5.7.3.3", "Code Signing") }, true));
        request.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        using var certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(30));

        var password = "velopack-" + Guid.NewGuid().ToString("N");
        var p12File = Path.Combine(dir, "codesign.p12");
        var passwordFile = Path.Combine(dir, "codesign.password");
        File.WriteAllBytes(p12File, certificate.Export(X509ContentType.Pkcs12, password));
        File.WriteAllText(passwordFile, password);
        return (p12File, passwordFile);
    }

    /// <summary> Velopack's entitlements plus <see cref="AppOnlyEntitlement"/>, as an app's own --signEntitlements file. </summary>
    private static string CreateAppEntitlements(string dir)
    {
        var plist = File.ReadAllText(HelperFile.VelopackEntitlements);
        var end = plist.LastIndexOf("</dict>", StringComparison.Ordinal);
        Assert.True(end > 0, "Expected Velopack.entitlements to be a plist dictionary");

        var path = Path.Combine(dir, "TestApp.entitlements");
        File.WriteAllText(path, plist.Insert(end, $"\t<key>{AppOnlyEntitlement}</key>\n\t<true/>\n"));
        return path;
    }

    /// <summary> Returns why vpk could not sign with rcodesign here, looking it up as vpk does, or null when it can. </summary>
    private static string GetRcodesignBlocker()
    {
        return RcodesignTools.FindOnPath(Environment.GetEnvironmentVariable("PATH")) == null
            ? "rcodesign was not found on the PATH (install it with 'cargo install --locked apple-codesign')"
            : null;
    }
}
