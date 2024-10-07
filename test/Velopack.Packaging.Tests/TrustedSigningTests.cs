using Velopack.Util;

namespace Velopack.Packaging.Tests;

public class TrustedSigningTests
{
    private readonly ITestOutputHelper _output;

    public TrustedSigningTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [SkippableFact]
    public void CanSignWithTrustedSigning()
    {
        Skip.If(!VelopackRuntimeInfo.IsWindows);

        using var logger = _output.BuildLoggerFor<TrustedSigningTests>();
        using var _ = TempUtil.GetTempDirectory(out var releaseDir);

        string channel = string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("CI"))
            ? VelopackRuntimeInfo.SystemOs.GetOsShortName()
            : "ci-" + VelopackRuntimeInfo.SystemOs.GetOsShortName();

        string metadataFile = Path.Combine(releaseDir, "metadata.json");
        File.WriteAllText(metadataFile, """
            {
              "Endpoint": "https://eus.codesigning.azure.net",
              "CodeSigningAccountName": "velopack-signing-account",
              "CertificateProfileName": "VelopackPublic",
              "CorrelationId": "test"
            }
            """);

        var id = "AZTrustedSigningApp";
        TestApp.PackTestApp(id, "1.0.0", $"aztrusted-{DateTime.UtcNow.ToLongDateString()}", releaseDir, logger, channel: channel, azureTrustedSignFile: metadataFile);


    }
}
