#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Azure;
using Azure.CodeSigning;
using Azure.CodeSigning.Models;
using Azure.Core;
using Azure.Identity;
using Velopack.Core;
using Velopack.Packaging.Windows.Signing;
using Velopack.Util;

namespace Velopack.Packaging.Tests.Signing;

/// <summary>Offline tests for the Azure Trusted Signing pieces: metadata.json, chain parsing and the signing client.</summary>
public class AzureTrustedSigningUnitTests
{
    private static TestSigningCertificates Certs => TestSigningCertificates.Shared;

    // ---------------------------------------------------------------------------------------------------------------
    // metadata.json

    [Fact]
    public void Metadata_IsCaseInsensitive_AndToleratesCommentsAndTrailingCommas()
    {
        var metadata = AzureTrustedSigningMetadata.Parse(
            """
            {
              // the endpoint
              "endpoint": "https://eus.codesigning.azure.net/",
              "CODESIGNINGACCOUNTNAME": "my-account",
              "certificateProfileName": "my-profile",
              "CorrelationId": "abc-123",
              "AdditionalInfo": { "ignored": true },
              "SomethingUnknown": 42,
            }
            """,
            "metadata.json");

        Assert.Equal("https://eus.codesigning.azure.net", metadata.Endpoint);
        Assert.Equal("my-account", metadata.CodeSigningAccountName);
        Assert.Equal("my-profile", metadata.CertificateProfileName);
        Assert.Equal("abc-123", metadata.CorrelationId);
        Assert.Empty(metadata.ExcludeCredentials);
        Assert.Null(metadata.AccessToken);
        Assert.IsType<DefaultAzureCredential>(metadata.CreateCredential());
    }

    [Fact]
    public void Metadata_MissingRequired_ListsAll()
    {
        var ex = Assert.Throws<UserInfoException>(() => AzureTrustedSigningMetadata.Parse("""{ "Endpoint": "" }""", "meta.json"));
        Assert.Contains("meta.json", ex.Message);
        Assert.Contains("Endpoint", ex.Message);
        Assert.Contains("CodeSigningAccountName", ex.Message);
        Assert.Contains("CertificateProfileName", ex.Message);
    }

    [Fact]
    public void Metadata_InvalidJson()
    {
        var ex = Assert.Throws<UserInfoException>(() => AzureTrustedSigningMetadata.Parse("{ nope", "meta.json"));
        Assert.Contains("Unable to parse", ex.Message);
    }

    [Fact]
    public void Metadata_MissingFile()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var ex = Assert.Throws<UserInfoException>(() => AzureTrustedSigningMetadata.Load(Path.Combine(dir, "nope.json")));
        Assert.Contains("does not exist", ex.Message);
    }

    [Theory]
    [InlineData("eus.codesigning.azure.net", "https://eus.codesigning.azure.net")]
    [InlineData("https://eus.codesigning.azure.net//", "https://eus.codesigning.azure.net")]
    [InlineData("  https://wus2.codesigning.azure.net  ", "https://wus2.codesigning.azure.net")]
    public void Metadata_EndpointIsNormalized(string endpoint, string expected)
    {
        var metadata = Parse(endpoint: endpoint);
        Assert.Equal(expected, metadata.Endpoint);
        Assert.Equal(new Uri(expected), metadata.EndpointUri);
    }

    [Fact]
    public void Metadata_InvalidEndpoint()
    {
        Assert.Throws<UserInfoException>(() => Parse(endpoint: "ftp://example.com"));
        Assert.Throws<UserInfoException>(() => Parse(endpoint: "http://eus.codesigning.azure.net"));
        Assert.Throws<UserInfoException>(() => Parse(endpoint: "https://exa mple.com"));
    }

    [Fact]
    public async Task Metadata_AccessToken_UsesStaticCredential()
    {
        var metadata = AzureTrustedSigningMetadata.Parse(
            """
            {
              "Endpoint": "https://eus.codesigning.azure.net",
              "CodeSigningAccountName": "a",
              "CertificateProfileName": "p",
              "AccessToken": "my-token",
              "ExcludeCredentials": ["NotARealCredential"]
            }
            """,
            "metadata.json");

        var credential = Assert.IsType<AccessTokenCredential>(metadata.CreateCredential());
        var context = new TokenRequestContext(["https://codesigning.azure.net/.default"]);
        var token = credential.GetToken(context, default);
        Assert.Equal("my-token", token.Token);
        Assert.True(token.ExpiresOn > DateTimeOffset.UtcNow.AddHours(12));
        Assert.Equal("my-token", (await credential.GetTokenAsync(context, default)).Token);
    }

    [Fact]
    public void Metadata_ExcludeCredentials_MapsToOptions()
    {
        var metadata = Parse(excludeCredentials: ["azureclicredential", "ManagedIdentity"]);
        Assert.Equal(["azureclicredential", "ManagedIdentity"], metadata.ExcludeCredentials);

        // Windows: like the Dlib, every credential that is not excluded is enabled
        var options = AzureTrustedSigningMetadata.BuildCredentialOptions(metadata.ExcludeCredentials, enableAllCredentials: true);
        Assert.True(options.ExcludeAzureCliCredential);
        Assert.True(options.ExcludeManagedIdentityCredential);
        Assert.False(options.ExcludeEnvironmentCredential);
        Assert.False(options.ExcludeWorkloadIdentityCredential);
        Assert.False(options.ExcludeVisualStudioCredential);
        Assert.False(options.ExcludeVisualStudioCodeCredential);
        Assert.False(options.ExcludeAzurePowerShellCredential);
        Assert.False(options.ExcludeAzureDeveloperCliCredential);
        Assert.False(options.ExcludeInteractiveBrowserCredential);
#pragma warning disable CS0618
        Assert.False(options.ExcludeSharedTokenCacheCredential);
#pragma warning restore CS0618

        var all = AzureTrustedSigningMetadata.BuildCredentialOptions(enableAllCredentials: true, excludeCredentials: [
            "EnvironmentCredential", "WorkloadIdentityCredential", "ManagedIdentityCredential", "SharedTokenCacheCredential",
            "VisualStudioCredential", "VisualStudioCodeCredential", "AzureCliCredential", "AzurePowerShellCredential",
            "AzureDeveloperCliCredential", "InteractiveBrowserCredential",
        ]);
        Assert.True(all.ExcludeEnvironmentCredential && all.ExcludeWorkloadIdentityCredential && all.ExcludeVisualStudioCredential
                    && all.ExcludeVisualStudioCodeCredential && all.ExcludeAzurePowerShellCredential
                    && all.ExcludeAzureDeveloperCliCredential && all.ExcludeInteractiveBrowserCredential);
    }

    [Fact]
    public void Metadata_ExcludeCredentials_KeepsSdkDefaultsOutsideWindows()
    {
        var defaults = new DefaultAzureCredentialOptions();
        var options = AzureTrustedSigningMetadata.BuildCredentialOptions(["AzureCli"], enableAllCredentials: false);
        Assert.True(options.ExcludeAzureCliCredential);
        // left at the SDK default (excluded): SharedTokenCacheCredential needs a keyring on Linux, and an unavailable one
        // fails the whole chain instead of falling through to AzureCliCredential
#pragma warning disable CS0618
        Assert.True(options.ExcludeSharedTokenCacheCredential);
#pragma warning restore CS0618
        Assert.True(options.ExcludeInteractiveBrowserCredential);
        Assert.Equal(defaults.ExcludeEnvironmentCredential, options.ExcludeEnvironmentCredential);
        Assert.Equal(defaults.ExcludeManagedIdentityCredential, options.ExcludeManagedIdentityCredential);
        Assert.Equal(defaults.ExcludeAzurePowerShellCredential, options.ExcludeAzurePowerShellCredential);

        // the platform decides when not specified
        var platform = AzureTrustedSigningMetadata.BuildCredentialOptions([]);
        Assert.Equal(!OperatingSystem.IsWindows(), platform.ExcludeInteractiveBrowserCredential);
    }

    [Fact]
    public void Metadata_ExcludeCredentials_UnknownIsAnError()
    {
        var ex = Assert.Throws<UserInfoException>(() => Parse(excludeCredentials: ["AzureCli", "Bogus"]));
        Assert.Contains("Bogus", ex.Message);
        Assert.Contains("AzureCliCredential", ex.Message);
        Assert.DoesNotContain("AzureCli,", ex.Message);
    }

    private static AzureTrustedSigningMetadata Parse(string endpoint = "https://eus.codesigning.azure.net", string[]? excludeCredentials = null)
    {
        var json = new StringBuilder();
        json.Append($$"""{ "Endpoint": "{{endpoint}}", "CodeSigningAccountName": "acct", "CertificateProfileName": "prof" """);
        if (excludeCredentials != null) {
            json.Append(", \"ExcludeCredentials\": [" + String.Join(",", excludeCredentials.Select(x => $"\"{x}\"")) + "]");
        }

        json.Append('}');
        return AzureTrustedSigningMetadata.Parse(json.ToString(), "metadata.json");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // chain parsing

    [Fact]
    public void Chain_Pkcs7_AnyOrder()
    {
        foreach (var order in new[] {
                     new[] { Certs.Root, Certs.Intermediate, Certs.Leaf },
                     new[] { Certs.Leaf, Certs.Root, Certs.Intermediate },
                     new[] { Certs.Intermediate, Certs.Leaf, Certs.Root },
                 }) {
            var parsed = CertificateChainParser.Parse(Certs.ExportChainPkcs7(order));
            Assert.Equal(3, parsed.Count);
            AssertSplit(parsed, expectIntermediate: true);
        }
    }

    [Fact]
    public void Chain_Base64AndDoubleBase64()
    {
        var pkcs7 = Certs.ExportChainPkcs7(Certs.Root, Certs.Intermediate, Certs.Leaf);
        var once = Encoding.ASCII.GetBytes(Convert.ToBase64String(pkcs7));
        AssertSplit(CertificateChainParser.Parse(once), expectIntermediate: true);

        var twice = Encoding.ASCII.GetBytes(Convert.ToBase64String(once));
        AssertSplit(CertificateChainParser.Parse(twice), expectIntermediate: true);

        var pem = Encoding.ASCII.GetBytes("-----BEGIN PKCS7-----\n" + Convert.ToBase64String(pkcs7, Base64FormattingOptions.InsertLineBreaks)
                                          + "\n-----END PKCS7-----\n");
        AssertSplit(CertificateChainParser.Parse(pem), expectIntermediate: true);
    }

    [Fact]
    public void Chain_SingleDerCertificate()
    {
        var parsed = CertificateChainParser.Parse(Certs.Leaf.RawData);
        Assert.Single(parsed);
        AssertSplit(parsed, expectIntermediate: false);
    }

    [Fact]
    public void Chain_Garbage_IsAnError()
    {
        Assert.Throws<UserInfoException>(() => CertificateChainParser.Parse(Encoding.ASCII.GetBytes("hello world")));
        Assert.Throws<UserInfoException>(() => CertificateChainParser.Parse([1, 2, 3]));
    }

    [Fact]
    public void Chain_TwoLeaves_IsAnError()
    {
        using var otherKey = RSA.Create(2048);
        var request = new CertificateRequest("CN=Other Leaf", otherKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        using var other = request.Create(Certs.Intermediate, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), [5, 6, 7]);
        var parsed = CertificateChainParser.Parse(Certs.ExportChainPkcs7(Certs.Root, Certs.Intermediate, Certs.Leaf, other));
        var ex = Assert.Throws<UserInfoException>(() => CertificateChainParser.SplitChain(parsed));
        Assert.Contains("Other Leaf", ex.Message);
    }

    [Fact]
    public void Chain_NonRsaLeaf_IsAnError()
    {
        using var ecKey = ECDsa.Create(ECCurve.NamedCurves.nistP256);
        var request = new CertificateRequest("CN=EC Leaf", ecKey, HashAlgorithmName.SHA256);
        using var intermediateKey = Certs.Intermediate.GetRSAPrivateKey()!;
        var generator = X509SignatureGenerator.CreateForRSA(intermediateKey, RSASignaturePadding.Pkcs1);
        var now = DateTimeOffset.UtcNow;
        using var ecLeaf = request.Create(Certs.Intermediate.SubjectName, generator, now.AddDays(-1), now.AddDays(1), [9]);
        var parsed = CertificateChainParser.Parse(Certs.ExportChainPkcs7(Certs.Intermediate, ecLeaf));
        var ex = Assert.Throws<UserInfoException>(() => CertificateChainParser.SplitChain(parsed));
        Assert.Contains("RSA", ex.Message);
    }

    private static void AssertSplit(X509Certificate2Collection parsed, bool expectIntermediate)
    {
        var (leaf, intermediates) = CertificateChainParser.SplitChain(parsed);
        Assert.Equal(Certs.Leaf.Thumbprint, leaf.Thumbprint);
        if (expectIntermediate) {
            var intermediate = Assert.Single(intermediates);
            Assert.Equal(Certs.Intermediate.Thumbprint, intermediate.Thumbprint);
        } else {
            Assert.Empty(intermediates);
        }
    }

    // ---------------------------------------------------------------------------------------------------------------
    // AzureTrustedSigningClient over a mocked CertificateProfileClient

    private static AzureTrustedSigningClient CreateClient(FakeCertificateProfileClient fake)
    {
        var metadata = AzureTrustedSigningMetadata.Parse(
            """
            {
              "Endpoint": "https://eus.codesigning.azure.net",
              "CodeSigningAccountName": "acct",
              "CertificateProfileName": "prof",
              "CorrelationId": "corr"
            }
            """,
            "metadata.json");
        return new AzureTrustedSigningClient(metadata, fake, NullLogger.Instance) { RetryDelays = [TimeSpan.Zero, TimeSpan.Zero] };
    }

    [Fact]
    public void Client_SignsAndCachesChain()
    {
        var fake = new FakeCertificateProfileClient(Certs.ExportChainPkcs7(Certs.Root, Certs.Intermediate, Certs.Leaf));
        var client = CreateClient(fake);
        var chain = client.GetCertificateChain();
        Assert.Equal(Certs.Leaf.Thumbprint, chain.Leaf.Thumbprint);

        byte[] digest = SHA256.HashData("data"u8);
        byte[] signature = client.SignDigest(digest, HashAlgorithmName.SHA256);
        Assert.True(Certs.Leaf.GetRSAPublicKey()!.VerifyHash(digest, signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
        client.SignDigest(digest, HashAlgorithmName.SHA256);

        Assert.Equal(1, fake.ChainRequests);
        Assert.Equal(2, fake.SignRequests.Count);
        Assert.All(fake.SignRequests, r => {
            Assert.Equal(SignatureAlgorithm.RS256, r.Request.SignatureAlgorithm);
            Assert.Equal("acct", r.Account);
            Assert.Equal("prof", r.Profile);
            Assert.Equal("corr", r.CorrelationId);
            Assert.True(r.Request.FileHashList == null || r.Request.FileHashList.Count == 0);
        });
    }

    [Fact]
    public void Client_EndToEnd_ProducesValidFileSignature()
    {
        var chainBase64 = Convert.ToBase64String(Certs.ExportChainPkcs7(Certs.Root, Certs.Leaf, Certs.Intermediate));
        var fake = new FakeCertificateProfileClient(Encoding.ASCII.GetBytes(chainBase64));
        var client = CreateClient(fake);
        var chain = client.GetCertificateChain();
        var key = new AuthenticodeSigningKey(new RemoteRsa(client, chain.Leaf.GetRSAPublicKey()!), chain.Leaf, chain.Intermediates);

        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = Path.Combine(dir, "app.exe");
        File.Copy(PathHelper.GetFixture("NotSquirrelAwareApp.exe"), path);
        new PeAuthenticodeSigner(NullLogger.Instance).SignFile(path, key, null, "x");
        Assert.Equal(Certs.Leaf.Thumbprint, AuthenticodeVerifier.VerifyFile(path).SignerCertificate.Thumbprint);
    }

    [Fact]
    public void Client_RetriesTransientFailures()
    {
        var fake = new FakeCertificateProfileClient(Certs.ExportChainPkcs7(Certs.Intermediate, Certs.Leaf));
        fake.Script.Enqueue(_ => throw new RequestFailedException(503, "busy"));
        fake.Script.Enqueue(_ => throw new RequestFailedException(500, "oops"));
        var client = CreateClient(fake);
        byte[] signature = client.SignDigest(new byte[32], HashAlgorithmName.SHA256);
        Assert.Equal(384, signature.Length);
        Assert.Equal(3, fake.SignRequests.Count);
    }

    [Fact]
    public void Client_FailedOperation_IsReportedWithoutRetry()
    {
        // the real SDK throws a RequestFailedException carrying the 200 status-poll response for Failed / Canceled
        var fake = new FakeCertificateProfileClient(Certs.ExportChainPkcs7(Certs.Intermediate, Certs.Leaf));
        var operationId = Guid.NewGuid();
        fake.Script.Enqueue(_ => new SignStatus(operationId, Status.Failed));
        var client = CreateClient(fake);
        var ex = Assert.Throws<UserInfoException>(() => client.SignDigest(new byte[32], HashAlgorithmName.SHA256));
        Assert.Contains($"operation {operationId} failed", ex.Message);
        Assert.Contains("SignFailed", ex.Message);
        Assert.DoesNotContain("HTTP 200", ex.Message);
        Assert.Single(fake.SignRequests);
    }

    [Fact]
    public void Client_GivesUpAfterThreeAttempts()
    {
        var fake = new FakeCertificateProfileClient(Certs.ExportChainPkcs7(Certs.Intermediate, Certs.Leaf));
        for (int i = 0; i < 3; i++) fake.Script.Enqueue(_ => throw new RequestFailedException(429, "slow down"));
        var client = CreateClient(fake);
        var ex = Assert.Throws<UserInfoException>(() => client.SignDigest(new byte[32], HashAlgorithmName.SHA256));
        Assert.Contains("429", ex.Message);
        Assert.Equal(3, fake.SignRequests.Count);
    }

    [Fact]
    public void Client_Forbidden_MentionsRole()
    {
        var fake = new FakeCertificateProfileClient(Certs.ExportChainPkcs7(Certs.Intermediate, Certs.Leaf));
        fake.Script.Enqueue(_ => throw new RequestFailedException(403, "forbidden", "AuthorizationFailed", null));
        var client = CreateClient(fake);
        var ex = Assert.Throws<UserInfoException>(() => client.SignDigest(new byte[32], HashAlgorithmName.SHA256));
        Assert.Contains("Certificate Profile Signer", ex.Message);
        Assert.Single(fake.SignRequests);
    }

    [Fact]
    public void Client_AuthenticationFailure_IsUserFriendly()
    {
        var fake = new FakeCertificateProfileClient(Certs.ExportChainPkcs7(Certs.Intermediate, Certs.Leaf)) {
            ChainFailure = new CredentialUnavailableException("no credentials"),
        };
        var client = CreateClient(fake);
        var ex = Assert.Throws<UserInfoException>(() => client.GetCertificateChain());
        Assert.Contains("authenticate", ex.Message);
    }

    [Fact]
    public void Client_RejectsWrongDigestLengthAndAlgorithm()
    {
        var client = CreateClient(new FakeCertificateProfileClient(Certs.ExportChainPkcs7(Certs.Intermediate, Certs.Leaf)));
        Assert.Throws<CryptographicException>(() => client.SignDigest(new byte[20], HashAlgorithmName.SHA256));
        Assert.Throws<NotSupportedException>(() => client.SignDigest(new byte[20], HashAlgorithmName.SHA1));
    }

    [Fact]
    public void Client_DetectsCertificateRotation_AndRefreshesOnce()
    {
        using var newKey = RSA.Create(3072);
        var request = new CertificateRequest("CN=Velopack Test Signer Rotated", newKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var rotated = request.Create(Certs.Intermediate, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), [42]);

        var fake = new FakeCertificateProfileClient(Certs.ExportChainPkcs7(Certs.Intermediate, Certs.Leaf));
        var client = CreateClient(fake);
        var original = client.GetCertificateChain();

        fake.Script.Enqueue(digest => new SignStatus(
            Guid.NewGuid(),
            Status.Succeeded,
            newKey.SignHash(digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1),
            rotated.RawData));
        var ex = Assert.Throws<AzureSigningCertificateChangedException>(() => client.SignDigest(new byte[32], HashAlgorithmName.SHA256));
        Assert.Equal(Certs.Leaf.Thumbprint, ex.StaleThumbprint);

        fake.ChainData = Certs.ExportChainPkcs7(Certs.Intermediate, rotated);
        var refreshed = client.RefreshCertificateChain(ex.StaleThumbprint);
        Assert.Equal(rotated.Thumbprint, refreshed.Leaf.Thumbprint);
        Assert.NotEqual(original.Leaf.Thumbprint, refreshed.Leaf.Thumbprint);
        // a second caller with the same stale thumbprint does not fetch again
        client.RefreshCertificateChain(ex.StaleThumbprint);
        Assert.Equal(2, fake.ChainRequests);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // AzureTrustedSigner over a mocked CertificateProfileClient (the path vpk runs for --azureTrustedSignFile)

    private static string WriteMetadataFile(string dir)
    {
        var path = Path.Combine(dir, "metadata.json");
        File.WriteAllText(
            path,
            """{ "Endpoint": "https://eus.codesigning.azure.net", "CodeSigningAccountName": "acct", "CertificateProfileName": "prof" }""");
        return path;
    }

    private static string CopyUnsignedExe(string dir, string name)
    {
        var path = Path.Combine(dir, name);
        File.Copy(PathHelper.GetFixture("NotSquirrelAwareApp.exe"), path);
        return path;
    }

    [Fact]
    public async Task Signer_MetadataPath_SignsWithOneSession()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var metadataPath = WriteMetadataFile(dir);
        var fake = new FakeCertificateProfileClient(Certs.ExportChainPkcs7(Certs.Root, Certs.Intermediate, Certs.Leaf));
        var tsa = new FakeTimestampAuthority(Certs.TimestampAuthority);
        var timestamper = new Rfc3161Timestamper(new HttpClient(tsa), FakeTimestampAuthority.Url, retryDelays: [TimeSpan.Zero]);
        int factoryCalls = 0;
        using var signer = new AzureTrustedSigner(
            NullLogger.Instance,
            metadata => {
                factoryCalls++;
                Assert.Equal("acct", metadata.CodeSigningAccountName);
                return fake;
            },
            timestamper);

        // like a pack: the package files first, then Setup.exe
        var packageFiles = new[] { CopyUnsignedExe(dir, "app.exe"), CopyUnsignedExe(dir, "lib.dll") };
        await signer.SignFilesAsync(packageFiles, metadataPath, "My App", 2, _ => { });
        var setup = CopyUnsignedExe(dir, "Setup.exe");
        await signer.SignFilesAsync([setup], metadataPath, "My App", 2, _ => { });

        Assert.Equal(1, factoryCalls);
        Assert.Equal(1, fake.ChainRequests);
        Assert.Equal(3, fake.SignRequests.Count);
        Assert.Equal(3, tsa.RequestCount);
        foreach (var file in packageFiles.Append(setup)) {
            var result = AuthenticodeVerifier.VerifyFile(file);
            Assert.Equal(Certs.Leaf.Thumbprint, result.SignerCertificate.Thumbprint);
            Assert.NotNull(result.Timestamp);
        }
    }

    [Theory]
    [InlineData(true)] // the service reports the rotated certificate in the sign response
    [InlineData(false)] // it reports none, so the rotation is only seen when the signature does not verify
    public async Task Signer_MetadataPath_RefreshesChainWhenCertificateRotates(bool reportsSigningCertificate)
    {
        using var newKey = RSA.Create(3072);
        var request = new CertificateRequest("CN=Velopack Test Signer Rotated", newKey, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        using var rotated = request.Create(Certs.Intermediate, DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1), [43]);

        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var metadataPath = WriteMetadataFile(dir);
        var fake = new FakeCertificateProfileClient(Certs.ExportChainPkcs7(Certs.Intermediate, Certs.Leaf));
        using var signer = new AzureTrustedSigner(NullLogger.Instance, _ => fake, timestamper: null);

        var first = CopyUnsignedExe(dir, "first.exe");
        await signer.SignFilesAsync([first], metadataPath, "My App", 1, _ => { });
        Assert.Equal(Certs.Leaf.Thumbprint, AuthenticodeVerifier.VerifyFile(first).SignerCertificate.Thumbprint);

        // the profile certificate rotates between two signing calls of the same pack
        fake.SigningKey = newKey;
        fake.SigningCertificate = reportsSigningCertificate ? rotated.RawData : null;
        fake.ChainData = Certs.ExportChainPkcs7(Certs.Intermediate, rotated);

        var second = CopyUnsignedExe(dir, "second.exe");
        await signer.SignFilesAsync([second], metadataPath, "My App", 1, _ => { });
        Assert.Equal(rotated.Thumbprint, AuthenticodeVerifier.VerifyFile(second).SignerCertificate.Thumbprint);
        Assert.Equal(2, fake.ChainRequests);
        Assert.Equal(3, fake.SignRequests.Count); // first, the stale attempt, and its retry
    }

    [Fact]
    public async Task Signer_MetadataPath_WrongKeyWithUnchangedCertificate_IsReported()
    {
        // the service signs with another key, but the chain still has the same certificate: refreshed once, then reported
        using var otherKey = RSA.Create(3072);
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var metadataPath = WriteMetadataFile(dir);
        var fake = new FakeCertificateProfileClient(Certs.ExportChainPkcs7(Certs.Intermediate, Certs.Leaf)) {
            SigningKey = otherKey,
            SigningCertificate = null,
        };
        using var signer = new AzureTrustedSigner(NullLogger.Instance, _ => fake, timestamper: null);

        var file = CopyUnsignedExe(dir, "app.exe");
        var original = File.ReadAllBytes(file);
        var ex = await Assert.ThrowsAsync<UserInfoException>(() => signer.SignFilesAsync([file], metadataPath, "My App", 1, _ => { }));
        Assert.Contains(file, ex.Message);
        Assert.Contains("does not verify", ex.Message);
        Assert.Equal(2, fake.ChainRequests);
        Assert.Single(fake.SignRequests);
        Assert.Equal(original, File.ReadAllBytes(file));
    }

    [Fact]
    public void Client_TimesOutStuckOperations()
    {
        var fake = new FakeCertificateProfileClient(Certs.ExportChainPkcs7(Certs.Intermediate, Certs.Leaf)) { NeverComplete = true };
        var metadata = AzureTrustedSigningMetadata.Parse(
            """{ "Endpoint": "https://eus.codesigning.azure.net", "CodeSigningAccountName": "acct", "CertificateProfileName": "prof" }""",
            "metadata.json");
        var client = new AzureTrustedSigningClient(metadata, fake, NullLogger.Instance) {
            RetryDelays = [TimeSpan.Zero],
            OperationTimeout = TimeSpan.FromMilliseconds(300),
        };
        var ex = Assert.Throws<UserInfoException>(() => client.SignDigest(new byte[32], HashAlgorithmName.SHA256));
        Assert.Contains("did not complete", ex.Message);
        Assert.Equal(2, fake.SignRequests.Count);
    }

    private sealed class FakeCertificateProfileClient : CertificateProfileClient
    {
        public FakeCertificateProfileClient(byte[] chainData)
        {
            ChainData = chainData;
        }

        public byte[] ChainData { get; set; }
        public Exception? ChainFailure { get; set; }

        /// <summary>The key the service signs with by default.</summary>
        public RSA SigningKey { get; set; } = Certs.LeafKey;

        /// <summary>The certificate the service reports in each sign response by default; null to report none.</summary>
        public byte[]? SigningCertificate { get; set; } = Certs.Leaf.RawData;
        public bool NeverComplete { get; set; }
        public int ChainRequests { get; private set; }
        public Queue<Func<byte[], SignStatus>> Script { get; } = new();
        public List<(string Account, string Profile, SignRequest Request, string? CorrelationId)> SignRequests { get; } = [];

        public override Response<Stream> GetSignCertificateChain(string codeSigningAccountName, string certificateProfileName,
            CancellationToken cancellationToken = default)
        {
            ChainRequests++;
            if (ChainFailure != null) throw ChainFailure;
            return Response.FromValue<Stream>(new MemoryStream(ChainData), new FakeResponse(200));
        }

        public override CertificateProfileSignOperation StartSign(string codeSigningAccountName, string certificateProfileName, SignRequest body,
            string? xCorrelationId = null, string? clientVersion = null, string? certificateThumbprint = null,
            CancellationToken cancellationToken = default)
        {
            lock (SignRequests) {
                SignRequests.Add((codeSigningAccountName, certificateProfileName, body, xCorrelationId));
            }

            if (NeverComplete) return new FakeSignOperation(null);
            var step = Script.Count > 0 ? Script.Dequeue() : null;
            if (step != null) return new FakeSignOperation(step(body.Digest));
            byte[] signature = SigningKey.SignHash(body.Digest, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
            var status = new SignStatus(Guid.NewGuid(), Status.Succeeded, signature, SigningCertificate);
            return new FakeSignOperation(status);
        }
    }

    /// <summary>
    /// Mirrors the SDK's CertificateProfileSignOperation: Id is not implemented, the raw response body is the SignStatus
    /// JSON, and a Failed / Canceled status throws a RequestFailedException built from the 200 status response.
    /// </summary>
    private sealed class FakeSignOperation(SignStatus? status) : CertificateProfileSignOperation
    {
        private bool IsFailed => status != null && (status.Status == Status.Failed || status.Status.ToString() == "Canceled");
        public override string Id => throw new NotImplementedException();
        public override SignStatus Value => status != null && !IsFailed ? status : throw new InvalidOperationException("not complete");
        public override bool HasCompleted => status != null;
        public override bool HasValue => status != null && !IsFailed;
        public override Response GetRawResponse() => CreateStatusResponse();

        public override Response UpdateStatus(CancellationToken cancellationToken = default)
        {
            if (IsFailed) throw new RequestFailedException(200, "The signing operation failed.", "SignFailed", null);
            return CreateStatusResponse();
        }

        public override ValueTask<Response> UpdateStatusAsync(CancellationToken cancellationToken = default) => new(UpdateStatus(cancellationToken));

        private Response CreateStatusResponse()
        {
            if (status == null) return new FakeResponse(202);
            var json = $$"""{ "operationId": "{{status.OperationId}}", "status": "{{status.Status}}" }""";
            return new FakeResponse(200) { ContentStream = new MemoryStream(Encoding.UTF8.GetBytes(json)) };
        }

        public override ValueTask<Response<SignStatus>> WaitForCompletionAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public override ValueTask<Response<SignStatus>> WaitForCompletionAsync(TimeSpan pollingInterval, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private sealed class FakeResponse(int status) : Response
    {
        public override int Status => status;
        public override string ReasonPhrase => "OK";
        public override Stream? ContentStream { get; set; }
        public override string ClientRequestId { get; set; } = "";
        public override void Dispose() { }
        protected override bool TryGetHeader(string name, [NotNullWhen(true)] out string? value) { value = null; return false; }
        protected override bool TryGetHeaderValues(string name, [NotNullWhen(true)] out IEnumerable<string>? values) { values = null; return false; }
        protected override bool ContainsHeader(string name) => false;
        protected override IEnumerable<HttpHeader> EnumerateHeaders() => [];
    }
}
