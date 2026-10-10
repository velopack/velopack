#nullable enable
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Velopack.Core;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// Parses the certificate chain returned by Azure Trusted Signing and splits it into a leaf and intermediates.
/// </summary>
public static class CertificateChainParser
{
    /// <summary>
    /// Parses a certificate bundle: a PKCS#7 certs-only blob, the same blob base64 encoded (optionally PEM), or a
    /// single DER / base64 certificate.
    /// </summary>
    public static X509Certificate2Collection Parse(byte[] data)
    {
        if (TryParseBinary(data, out var certificates)) {
            return certificates;
        }

        if (TryDecodeBase64(data, out var decoded)) {
            if (TryParseBinary(decoded, out certificates)) {
                return certificates;
            }

            // the sign response's signingCertificate is a base64 encoded base64 PKCS#7
            if (TryDecodeBase64(decoded, out var twiceDecoded) && TryParseBinary(twiceDecoded, out certificates)) {
                return certificates;
            }
        }

        throw new UserInfoException("Unable to parse the certificate chain returned by Azure Trusted Signing.");
    }

    /// <summary>
    /// Picks the leaf certificate (the only certificate that did not issue another one in the set) and the
    /// intermediates (everything else that is not a self-signed root).
    /// </summary>
    public static (X509Certificate2 Leaf, X509Certificate2Collection Intermediates) SplitChain(X509Certificate2Collection certificates)
    {
        var all = certificates.Cast<X509Certificate2>().ToList();
        if (all.Count == 0) {
            throw new UserInfoException("The Azure Trusted Signing certificate chain does not contain any certificates.");
        }

        var leaves = all.Where(c => !all.Any(other => !ReferenceEquals(other, c) && !IsSelfSigned(other)
                                                     && other.IssuerName.RawData.AsSpan().SequenceEqual(c.SubjectName.RawData)))
            .ToList();
        if (leaves.Count != 1) {
            var subjects = String.Join(", ", (leaves.Count == 0 ? all : leaves).Select(c => $"'{c.Subject}'"));
            throw new UserInfoException(
                $"Unable to determine the signing certificate in the Azure Trusted Signing certificate chain (candidates: {subjects}).");
        }

        var leaf = leaves[0];
        using (var rsa = leaf.GetRSAPublicKey()) {
            if (rsa == null) {
                throw new UserInfoException($"The Azure Trusted Signing certificate '{leaf.Subject}' does not have an RSA key.");
            }
        }

        if (leaf.Extensions.OfType<X509BasicConstraintsExtension>().Any(e => e.CertificateAuthority)) {
            throw new UserInfoException($"The Azure Trusted Signing certificate '{leaf.Subject}' is a CA certificate.");
        }

        var intermediates = new X509Certificate2Collection();
        foreach (var certificate in all) {
            if (!ReferenceEquals(certificate, leaf) && !IsSelfSigned(certificate)) {
                intermediates.Add(certificate);
            }
        }

        return (leaf, intermediates);
    }

    private static bool IsSelfSigned(X509Certificate2 certificate)
    {
        return certificate.SubjectName.RawData.AsSpan().SequenceEqual(certificate.IssuerName.RawData);
    }

    private static bool TryParseBinary(byte[] data, out X509Certificate2Collection certificates)
    {
        try {
            var cms = new SignedCms();
            cms.Decode(data);
            if (cms.Certificates.Count > 0) {
                certificates = cms.Certificates;
                return true;
            }
        } catch (CryptographicException) {
        }

        try {
            certificates = [X509CertificateLoader.LoadCertificate(data)];
            return true;
        } catch (CryptographicException) {
        }

        certificates = [];
        return false;
    }

    private static bool TryDecodeBase64(byte[] data, out byte[] decoded)
    {
        decoded = [];
        string text;
        try {
            text = new UTF8Encoding(false, true).GetString(data).Trim().Trim('"');
        } catch (DecoderFallbackException) {
            return false;
        }

        var lines = text.Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0 && !l.StartsWith("-----", StringComparison.Ordinal));
        string base64 = String.Concat(lines);
        if (base64.Length == 0) return false;

        var buffer = new byte[base64.Length];
        if (!Convert.TryFromBase64String(base64, buffer, out int written) || written == 0) return false;
        decoded = buffer.AsSpan(0, written).ToArray();
        return true;
    }
}
