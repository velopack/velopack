using Azure.Core;
using Azure.Identity;
using Microsoft.Security.Extensions;
using Velopack.Packaging.Windows;
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
        //SingTool.exe will use DefaultAzureCredentials to authenticate.
        //We preemptively check if there are valid creds to use and skip the test if not.
        //This allows the test to be skipped for everyone who does not have the "Trusted Signing Certificate Profile Signer" role.

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

    [SkippableFact]
    public async void CanSignWithTrustedSigning()
    {
        Skip.IfNot(VelopackRuntimeInfo.IsWindows, "Only supported on Windows");
        Skip.IfNot(await IsAuthenticatedForCodeSigningAsync(), "Sign in with az login first");

        using var logger = _output.BuildLoggerFor<TrustedSigningTests>(LogLevel.Debug);
        using var _ = TempUtil.GetTempDirectory(out var releaseDir);

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
            azureTrustedSignFile: metadataFile);

        var files = Directory.EnumerateFiles(releaseDir)
            .Where(x => PathUtil.FileIsLikelyPEImage(x))
            .ToList();

        Assert.NotEmpty(files);
#pragma warning disable CA1416 // Validate platform compatibility, this test only executes on Windows
        Assert.All(files, x => Assert.True(IsTrusted(x)));
#pragma warning restore CA1416 // Validate platform compatibility
    }
    
    private bool IsTrusted(string filePath)
    {
        using var fileStream = File.OpenRead(filePath);
        var targetPackageSignatureInfo = FileSignatureInfo.GetFromFileStream(fileStream);
        return targetPackageSignatureInfo.State == SignatureState.SignedAndTrusted;
    }
}