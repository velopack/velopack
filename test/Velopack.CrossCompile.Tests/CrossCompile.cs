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

    private readonly ITestOutputHelper _output;

    public CrossCompile(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    [InlineData("win-x64")]
    [InlineData("linux-x64")]
    [InlineData("osx-arm64")]
    public void PackCrossApp(string target)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        var rid = RID.Parse(target);
        if (rid.BaseRID == RuntimeOs.OSX) {
            TestHelper.SkipLocallyFailInCI(TestApp.GetOsxPackBlocker(logger));
        }

        string id = $"from-{VelopackRuntimeInfo.SystemOs.GetOsShortName()}-targets-{rid.BaseRID.GetOsShortName()}";
        using var _1 = TempUtil.GetTempDirectory(out var tempDir);
        // only the portable zip goes to the macOS leg; the .pkg (built on macOS) is covered by Velopack.Pack.Tests
        TestApp.PackTestApp(id, "1.0.0", id, tempDir, logger, targetRid: rid, configureOsx: options => options.NoInst = true);

        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        Directory.CreateDirectory(artifactsDir);

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
    /// rcodesign cannot seal a non-Mach-O file in Contents/MacOS, so this is a single-file publish without
    /// test_string.txt, packed without the .pdb and .xml files such a publish still puts beside the exe.
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
            id, "1.0.0", null, tempDir, logger, targetRid: rid, singleFile: true,
            configureOsx: options => {
                if (!signDisableDeep) {
                    File.Copy(HelperFile.GetUpdatePath(rid, logger), Path.Combine(options.PackDirectory, DeepRouteExtraMachO));
                }

                options.Exclude = @".*\.(pdb|xml)$";
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
        AssertMacOSDirIsAllMachO(src, id, signingDir, expectedMachOs);
        File.Copy(src, Path.Combine(artifactsDir, id + ".zip"), overwrite: true);
    }

    /// <summary>
    /// Asserts that every file in the zipped bundle's Contents/MacOS is a Mach-O, apart from the sq.version symlink (which
    /// codesign seals as a link): anything else is what rcodesign leaves out of the signature, with only a warning.
    /// Each of <paramref name="expectedMachOs"/> must be there too.
    /// </summary>
    private static void AssertMacOSDirIsAllMachO(string portableZip, string id, string scratchDir, string[] expectedMachOs)
    {
        var macosDir = $"{id}.app/Contents/MacOS/";
        var scratchFile = Path.Combine(scratchDir, "macho-check");

        using var zip = ZipFile.OpenRead(portableZip);
        var files = zip.Entries
            .Where(e => e.FullName.StartsWith(macosDir, StringComparison.Ordinal) && !e.FullName.EndsWith('/'))
            .ToArray();
        foreach (var expected in expectedMachOs) {
            Assert.Contains(files, e => e.FullName == macosDir + expected);
        }

        var notMachO = new List<string>();
        foreach (var entry in files) {
            var name = entry.FullName.Substring(macosDir.Length);
            var type = UnixModeOf(entry) & S_IFMT;
            if (type == S_IFLNK && name == "sq.version") {
                continue;
            }

            if (type == S_IFREG) {
                entry.ExtractToFile(scratchFile, overwrite: true);
                if (BinDetect.IsMachOImage(scratchFile)) {
                    continue;
                }
            }

            notMachO.Add(name);
        }

        Assert.True(
            notMachO.Count == 0,
            $"Expected only Mach-O files in {macosDir}, as rcodesign leaves anything else unsealed, but found: {String.Join(", ", notMachO)}");
    }

    [Theory]
    [InlineData("from-win-targets-linux")]
    [InlineData("from-linux-targets-linux")]
    [InlineData("from-osx-targets-linux")]
    public async Task RunCrossAppLinux(string artifactId)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        Assert.SkipWhen(
            String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOPACK_CROSS_ARTIFACTS")),
            "VELOPACK_CROSS_ARTIFACTS not set");
        Assert.SkipUnless(VelopackRuntimeInfo.IsLinux, "AppImage's can only run on Linux");

        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        var artifactPath = Path.Combine(artifactsDir, artifactId + ".AppImage");

        Assert.True(File.Exists(artifactPath), $"Expected {artifactPath} to exist");
        Chmod.ChmodFileAsExecutable(artifactPath);

        var output = Exe.InvokeAndThrowIfNonZero(artifactPath, new[] { "test" }, null);
        logger.LogInformation(output);
        Assert.EndsWith(artifactId, output.Trim());

        // var appImageLintPath = PathHelper.GetTestRootPath("appimagelint.AppImage");
        // if (!File.Exists(appImageLintPath)) {
        //     var downloader = HttpUtil.CreateDefaultDownloader();
        //     await downloader.DownloadFile(
        //         "https://github.com/TheAssassin/appimagelint/releases/download/continuous/appimagelint-x86_64.AppImage",
        //         appImageLintPath,
        //         _ => { });
        //     Chmod.ChmodFileAsExecutable(appImageLintPath);
        // }
        //
        // var lintOutput = Exe.InvokeAndThrowIfNonZero(appImageLintPath, new[] { artifactPath }, null);
        // logger.LogInformation(lintOutput);
    }

    [Theory]
    [InlineData("from-win-targets-win")]
    [InlineData("from-linux-targets-win")]
    [InlineData("from-osx-targets-win")]
    public void RunCrossAppWindows(string artifactId)
    {
        using var logger = _output.BuildLoggerFor<CrossCompile>();
        Assert.SkipWhen(
            String.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("VELOPACK_CROSS_ARTIFACTS")),
            "VELOPACK_CROSS_ARTIFACTS not set");
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "PE files can only run on Windows");

        var artifactsDir = PathHelper.GetTestRootPath("artifacts");
        var artifactPath = Path.Combine(artifactsDir, artifactId + ".exe");

        Assert.True(File.Exists(artifactPath), $"Expected {artifactPath} to exist");

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var appRoot = Path.Combine(appData, artifactId);
        var appExe = Path.Combine(appRoot, "current", "TestApp.exe");
        var appUpdate = Path.Combine(appRoot, "Update.exe");

        IoUtil.DeleteFileOrDirectoryHard(appRoot);

        Assert.False(File.Exists(appExe));

        var installOutput = Exe.InvokeAndThrowIfNonZero(artifactPath, new[] { "--silent" }, null);
        logger.LogInformation(installOutput);

        Assert.True(File.Exists(appExe));

        var output = Exe.InvokeAndThrowIfNonZero(appExe, new[] { "test" }, null);
        logger.LogInformation(output);
        Assert.EndsWith(artifactId, output.Trim());

        var uninstallOutput = Exe.RunHostedCommand($"\"{appUpdate}\" --uninstall --silent");
        logger.LogInformation(uninstallOutput);

        // the uninstaller schedules the rmdir of its own directory ~3s after it exits; poll for it
        TestHelper.WaitUntil(() => Assert.False(Directory.Exists(appRoot)), timeoutMs: 15_000);
    }

    /// <summary>
    /// Expands a macOS portable zip as Finder does (ditto), checks the executable bits and the sq.version symlink, and
    /// runs the app. Signed artifacts must also pass codesign's verification, with the hardened runtime on every Mach-O
    /// and the expected entitlements. The unsigned artifacts only run on Apple Silicon because the .NET 9+ SDK ad-hoc
    /// signs the osx apphost on any host (and macOS's linker ad-hoc signs UpdateMac).
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

        if (signed) {
            VerifySignedBundle(bundle, appExe, updateMac, executables, logger);
        }

        var version = Exe.InvokeAndThrowIfNonZero(appExe, new[] { "version" }, null);
        logger.LogInformation(version);
        Assert.EndsWith("1.0.0", version.Trim());

        if (!signed) {
            var output = Exe.InvokeAndThrowIfNonZero(appExe, new[] { "test" }, null);
            logger.LogInformation(output);
            Assert.EndsWith(artifactId, output.Trim());
        }
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
