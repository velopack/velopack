using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using SignUniversal.Authenticode;
using SignUniversal.Msi;
using SignUniversal.Signing;
using SignUniversal.Signing.Azure;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.Packaging.Windows;

/// <summary>
/// Signs Windows binaries with Azure Trusted Signing without signtool.exe, so it works
/// on Linux and macOS as well as Windows.
/// </summary>
/// <remarks>
/// The signtool path needs the Trusted Signing Dlib, which is a native Windows binary.
/// This uses the managed client instead: the digest goes to the service over HTTPS and a
/// signature comes back, so the private key is never present locally on any platform.
/// The metadata file is the same one <c>--azureTrustedSignFile</c> already takes.
/// </remarks>
public class NativeCodeSign
{
    private static readonly Uri DefaultTimestampUrl = new("http://timestamp.digicert.com");

    public ILogger Log { get; }

    public NativeCodeSign(ILogger logger)
    {
        Log = logger;
    }

    /// <summary>Signs each file with the Trusted Signing account named in the metadata file.</summary>
    public void SignWithTrustedSigning(string[] filePaths, string metadataFilePath, Action<int> progress)
    {
        var metadata = TrustedSigningMetadata.Read(metadataFilePath);

        Log.Info($"Code signing with Azure Trusted Signing (account '{metadata.AccountName}', profile '{metadata.CertificateProfileName}').");

        // One session for the whole batch: each construction fetches the signing
        // certificate, and the service mints a short-lived one per session.
        using var signer = new TrustedSigningRemoteSigner(
            metadata.Endpoint,
            metadata.AccountName,
            metadata.CertificateProfileName);

        var toSign = filePaths.Where(f => !String.IsNullOrWhiteSpace(f)).ToArray();

        for (int i = 0; i < toSign.Length; i++) {
            SignOneFile(toSign[i], signer);
            Log.Info($"Code-signed {i + 1}/{toSign.Length} files");
            progress((int) ((double) (i + 1) / toSign.Length * 100));
        }
    }

    private void SignOneFile(string filePath, IRemoteSigner signer)
    {
        var fullPath = Path.GetFullPath(filePath);

        if (!File.Exists(fullPath)) {
            Log.Warn($"Cannot sign '{fullPath}', file does not exist.");
            return;
        }

        // Trusted Signing certificates live about three days, so an untimestamped
        // signature stops verifying almost immediately. Timestamping is not optional here.
        if (Path.GetExtension(fullPath).Equals(".msi", StringComparison.OrdinalIgnoreCase)) {
            MsiSigner.SignFile(fullPath, signer, HashAlgorithmName.SHA256, DefaultTimestampUrl);
        } else {
            PeSigner.SignFile(fullPath, signer, HashAlgorithmName.SHA256, DefaultTimestampUrl);
        }
    }

    /// <summary>The subset of the signtool Dlib metadata file that identifies the account.</summary>
    internal sealed record TrustedSigningMetadata(Uri Endpoint, string AccountName, string CertificateProfileName)
    {
        public static TrustedSigningMetadata Read(string path)
        {
            JsonElement root;
            try {
                using var stream = File.OpenRead(path);
                using var document = JsonDocument.Parse(stream);
                root = document.RootElement.Clone();
            } catch (JsonException ex) {
                throw new UserInfoException($"Trusted Signing metadata file '{path}' is not valid JSON: {ex.Message}");
            }

            var endpoint = GetString(root, "Endpoint");
            var account = GetString(root, "CodeSigningAccountName");
            var profile = GetString(root, "CertificateProfileName");

            var missing = new[] {
                endpoint == null ? "Endpoint" : null,
                account == null ? "CodeSigningAccountName" : null,
                profile == null ? "CertificateProfileName" : null,
            }.Where(x => x != null).ToArray();

            if (missing.Length > 0) {
                throw new UserInfoException(
                    $"Trusted Signing metadata file '{path}' is missing required field(s): {String.Join(", ", missing)}.");
            }

            if (!Uri.TryCreate(endpoint, UriKind.Absolute, out var endpointUri)) {
                throw new UserInfoException(
                    $"Trusted Signing metadata file '{path}' has an Endpoint that is not an absolute URL: '{endpoint}'.");
            }

            return new TrustedSigningMetadata(endpointUri, account, profile);
        }

        // The Dlib reads these case-insensitively, so metadata files in the wild vary.
        private static string GetString(JsonElement root, string name)
        {
            foreach (var property in root.EnumerateObject()) {
                if (String.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String) {
                    var value = property.Value.GetString();
                    return String.IsNullOrWhiteSpace(value) ? null : value;
                }
            }

            return null;
        }
    }
}
