#nullable enable
using System.IO.Compression;
using System.Runtime.Versioning;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Packaging.Windows.Commands;
using Velopack.Packaging.Windows.Signing;
using Velopack.TestCommon;
using Velopack.Util;
using Velopack.Vpk;
using Velopack.Vpk.Logging;

namespace Velopack.Packaging.Tests.Signing;

/// <summary>
/// Packs the TestApp with --azureTrustedSignFile, swapping only the Azure service for an in-memory key, so the whole
/// pack-time signing path (pack dir, Setup.exe, MSI) runs on every OS without Azure credentials.
/// </summary>
public class AzureTrustedSigningPackTests
{
    private readonly ITestOutputHelper _output;

    public AzureTrustedSigningPackTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static TestSigningCertificates Certs => TestSigningCertificates.Shared;

    internal const string WindowsBinariesMissing = "Windows rust binaries not built (cross-OS packing needs the CI vendored binaries)";

    [Fact]
    public async Task Pack_WithAzureTrustedSignFile_SignsPackageAndSetup()
    {
        Assert.SkipUnless(TestApp.CanPackWindowsTarget(RID.Parse("win-x64")), WindowsBinariesMissing);
        using var logger = _output.BuildLoggerFor<AzureTrustedSigningPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var releaseDir = Path.Combine(workDir, "releases");
        var publishDir = Path.Combine(workDir, "publish");
        TestApp.PreparePublishDir(RID.Parse("win-x64"), "signed-pack", publishDir, logger);
        var originallySigned = Directory.EnumerateFiles(publishDir, "*", SearchOption.AllDirectories)
            .Where(f => PathUtil.FileIsLikelyPEImage(f) && PeAuthenticode.HasCertificateTable(f))
            .ToDictionary(f => Path.GetRelativePath(publishDir, f), File.ReadAllBytes, StringComparer.OrdinalIgnoreCase);

        var runner = await PackAsync(logger, workDir, publishDir, releaseDir, "AzSignedApp", "Azure Signed App", buildMsi: false);

        // one call for the pack dir, one for Setup.exe; the description and --signParallel reach the signer
        Assert.Equal(2, runner.Calls.Count);
        Assert.All(runner.Calls, c => Assert.Equal("Azure Signed App", c.Description));
        Assert.All(runner.Calls, c => Assert.Equal(10, c.Parallelism));

        var setupExe = Assert.Single(Directory.EnumerateFiles(releaseDir, "*-Setup.exe"));
        AssertSignedByTestLeaf(setupExe);

        // every PE in the full package was signed by us, except binaries that were already signed by their publisher
        var fullNupkg = Assert.Single(Directory.EnumerateFiles(releaseDir, "*-full.nupkg"));
        var extractDir = Path.Combine(workDir, "extracted");
        ZipFile.ExtractToDirectory(fullNupkg, extractDir);
        var appDir = Path.Combine(extractDir, "lib", "app");
        var packagedPe = Directory.EnumerateFiles(appDir, "*", SearchOption.AllDirectories)
            .Where(f => PathUtil.FileIsLikelyPEImage(f))
            .ToArray();
        Assert.Contains(packagedPe, f => Path.GetFileName(f) == "TestApp.exe");
        Assert.Contains(packagedPe, f => Path.GetFileName(f) == "Squirrel.exe");
        foreach (var file in packagedPe) {
            if (originallySigned.TryGetValue(Path.GetRelativePath(appDir, file), out var originalBytes)) {
                // left alone byte-for-byte (not verified here: the publisher's signature is not ours to check)
                Assert.Equal(originalBytes, File.ReadAllBytes(file));
            } else {
                Assert.Equal(Certs.Leaf.Thumbprint, AuthenticodeVerifier.VerifyFile(file).SignerCertificate.Thumbprint);
            }
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task Pack_WithAzureTrustedSignFile_SignsMsi()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "MSI packages are only built on Windows");
        using var logger = _output.BuildLoggerFor<AzureTrustedSigningPackTests>();
        using var _1 = TempUtil.GetTempDirectory(out var workDir);
        var releaseDir = Path.Combine(workDir, "releases");
        var publishDir = Path.Combine(workDir, "publish");
        TestApp.PreparePublishDir(RID.Parse("win-x64"), "signed-msi", publishDir, logger);

        await PackAsync(logger, workDir, publishDir, releaseDir, "AzSignedMsiApp", null, buildMsi: true);

        AssertSignedByTestLeaf(Assert.Single(Directory.EnumerateFiles(releaseDir, "*-Setup.exe")));
        var msi = Assert.Single(Directory.EnumerateFiles(releaseDir, "*.msi"));
        uint hr = WinTrust.Verify(msi);
        Assert.True(hr is WinTrust.CERT_E_UNTRUSTEDROOT or WinTrust.CERT_E_CHAINING, $"Unexpected WinVerifyTrust result 0x{hr:X8}");
    }

    private static void AssertSignedByTestLeaf(string file)
    {
        Assert.Equal(Certs.Leaf.Thumbprint, AuthenticodeVerifier.VerifyFile(file).SignerCertificate.Thumbprint);
        if (OperatingSystem.IsWindows()) {
            uint hr = WinTrust.Verify(file);
            Assert.True(hr is WinTrust.CERT_E_UNTRUSTEDROOT or WinTrust.CERT_E_CHAINING, $"Unexpected WinVerifyTrust result 0x{hr:X8}");
        }
    }

    private static async Task<LocalKeyPackRunner> PackAsync(ILogger logger, string workDir, string publishDir, string releaseDir,
        string id, string? title, bool buildMsi)
    {
        // the metadata file must exist to pass validation, but the in-memory key replaces the Azure service
        var metadataFile = Path.Combine(workDir, "metadata.json");
        File.WriteAllText(metadataFile, "{}");

        var options = new WindowsPackOptions {
            EntryExecutableName = "TestApp.exe",
            ReleaseDir = new DirectoryInfo(releaseDir),
            PackId = id,
            PackTitle = title,
            PackVersion = "1.0.0",
            TargetRuntime = RID.Parse("win-x64"),
            PackDirectory = publishDir,
            BuildMsi = buildMsi,
            AzureTrustedSignFile = metadataFile,
        };

        var console = new BasicConsole(logger, new VelopackDefaults(false));
        var runner = new LocalKeyPackRunner(logger, console, Certs.CreateKey());
        await runner.Run(options);
        return runner;
    }

    private sealed record SignCall(string[] Files, string Description, int Parallelism);

    private sealed class LocalKeyPackRunner(ILogger logger, IFancyConsole console, AuthenticodeSigningKey key)
        : WindowsPackCommandRunner(logger, console)
    {
        public List<SignCall> Calls { get; } = [];

        protected override Task SignWithAzureTrustedSigningAsync(string[] filePaths, string metadataPath, string description,
            int parallelism, Action<int> progress)
        {
            Calls.Add(new SignCall(filePaths, description, parallelism));
            return new AzureTrustedSigner(Log).SignFilesAsync(
                filePaths,
                new StaticAuthenticodeKeyProvider(key),
                timestamper: null,
                description,
                parallelism,
                progress,
                msiSignerFactory: CreateMsiSigner);
        }

        private IMsiSigner CreateMsiSigner(AuthenticodeSigningKey msiKey)
        {
            if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException();
            return new MsiAzureSigner(msiKey, Log, timestampUrl: null);
        }
    }
}
