using System.IO.Compression;
using System.Runtime.Versioning;
using Azure.Core;
using Azure.Identity;
using Velopack.Packaging.Tests.Signing;
using Velopack.Packaging.Windows;
using Velopack.Packaging.Windows.Signing;
using Velopack.TestCommon;
using Velopack.Util;

namespace Velopack.Packaging.Tests;

public class TrustedSigningTests
{
    private const string CodeSigningEndpoint = "https://eus.codesigning.azure.net";

    private readonly ITestOutputHelper _output;

    public TrustedSigningTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static async Task<bool> IsAuthenticatedForCodeSigningAsync()
    {
        // vpk uses DefaultAzureCredential to authenticate.
        // We preemptively check if there are valid creds to use and skip the test if not.
        // This allows the test to be skipped for everyone who does not have the "Trusted Signing Certificate Profile Signer" role.

        // We are more restrictive than the DefaultAzureCredentials, and only check for the AzureCliCredential and EnvironmentCredential.
        // To appropriately run this test, you will need to first run `az login` and authenticate with an account that has the "Trusted Signing Certificate Profile Signer" role within the Velopack Azure subscription.
        var creds = new ChainedTokenCredential(
            new AzureCliCredential(),
            new EnvironmentCredential());
        // var creds = new DefaultAzureCredential();
        try {
            var token = await creds.GetTokenAsync(new TokenRequestContext([$"https://codesigning.azure.net/.default"]));
            return token.Token != null;
        } catch (Exception) {
            return false;
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CanSignWithTrustedSigning(bool buildMsi)
    {
        Assert.SkipWhen(buildMsi && !VelopackRuntimeInfo.IsWindows, "MSI packages are only built on Windows");
        Assert.SkipUnless(TestApp.CanPackWindowsTarget(RID.Parse("win-x64")), Signing.AzureTrustedSigningPackTests.WindowsBinariesMissing);
        Assert.SkipUnless(await IsAuthenticatedForCodeSigningAsync(), "Sign in with az login first");

        using var logger = _output.BuildLoggerFor<TrustedSigningTests>(LogLevel.Debug);
        using var _ = TempUtil.GetTempDirectory(out var releaseDir);
        using var _2 = TempUtil.GetTempDirectory(out var extractDir);

        string metadataFile = Path.Combine(releaseDir, "metadata.json");
        File.WriteAllText(
            metadataFile,
            $$"""
            {
              "Endpoint": "{{CodeSigningEndpoint}}",
              "CodeSigningAccountName": "velopack-signing-account",
              "CertificateProfileName": "VelopackPublic"
            }
            """);

        var id = "AZTrustedSigningApp";
        TestApp.PackTestApp(
            id,
            "1.0.0",
            $"aztrusted-{DateTime.UtcNow.ToLongDateString()}",
            releaseDir,
            logger,
            targetRid: RID.Parse("win-x64"),
            azureTrustedSignFile: metadataFile,
            buildMsi: buildMsi);

        var files = Directory.EnumerateFiles(releaseDir)
            .Where(x => PathUtil.FileIsLikelyPEImage(x))
            .ToList();

        // the binaries inside the full package were signed by the pack dir signing step
        var fullNupkg = Assert.Single(Directory.EnumerateFiles(releaseDir, "*-full.nupkg"));
        ZipFile.ExtractToDirectory(fullNupkg, extractDir);
        var packagedFiles = Directory.EnumerateFiles(Path.Combine(extractDir, "lib", "app"))
            .Where(x => PathUtil.FileIsLikelyPEImage(x))
            .ToList();
        Assert.Contains(packagedFiles, x => Path.GetFileName(x) == "TestApp.exe");
        files.AddRange(packagedFiles);

        Assert.NotEmpty(files);
        if (OperatingSystem.IsWindows()) {
            foreach (var file in files) {
                Assert.True(CodeSign.IsTrusted(file), $"'{file}' is not trusted");
            }
        }

        // the binaries we signed (not pre-signed dependencies) verify on every OS, are timestamped, and chain to the
        // Trusted Signing CA
        var ourFiles = files.Where(x => Path.GetFileName(x) is "TestApp.exe" or "TestApp.dll" or "Velopack.dll" or "Squirrel.exe")
            .Concat(Directory.EnumerateFiles(releaseDir, "*-Setup.exe"))
            .ToList();
        Assert.Equal(5, ourFiles.Count);
        foreach (var file in ourFiles) {
            var result = AuthenticodeVerifier.VerifyFile(file);
            Assert.NotNull(result.Timestamp);
            Assert.Contains("Microsoft ID Verified", result.SignerCertificate.Issuer);
        }

        if (buildMsi && OperatingSystem.IsWindows()) {
            var msi = Assert.Single(Directory.EnumerateFiles(releaseDir, "*.msi"));
            Assert.True(CodeSign.IsTrusted(msi), $"'{msi}' is not trusted");
            ourFiles.Add(msi);
        }

        // Trusted Signing leaf certificates only live ~3 days, so the signatures stay valid only if Windows itself recognizes
        // the embedded RFC3161 timestamp (IsTrusted above passes while the leaf is still valid, timestamp or not)
        if (OperatingSystem.IsWindows()) {
            foreach (var file in ourFiles) {
                AssertSigntoolSeesTimestamp(file);
            }
        }
    }

    [SupportedOSPlatform("windows")]
    private void AssertSigntoolSeesTimestamp(string file)
    {
        var psi = new System.Diagnostics.ProcessStartInfo(HelperFile.SignToolPath) {
            ArgumentList = { "verify", "/pa", "/tw", "/v", file },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        var stderrTask = process.StandardError.ReadToEndAsync();
        string output = process.StandardOutput.ReadToEnd() + stderrTask.GetAwaiterResult();
        process.WaitForExit();
        _output.WriteLine(output);
        // signtool exits 2 for warnings, which /tw raises when the signature is not timestamped
        Assert.True(process.ExitCode == 0, $"signtool verify failed for '{file}' (exit code {process.ExitCode})");
        Assert.DoesNotContain("not timestamped", output, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("The signature is timestamped", output);
    }
}
