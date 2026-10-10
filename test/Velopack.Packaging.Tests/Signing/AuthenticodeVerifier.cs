#nullable enable
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using Velopack.Packaging.Windows.Signing;

namespace Velopack.Packaging.Tests.Signing;

/// <summary>The result of a successful <see cref="AuthenticodeVerifier.VerifyFile"/>.</summary>
/// <param name="SignedCms">The decoded signature.</param>
/// <param name="SignerCertificate">The certificate that produced the signature.</param>
/// <param name="DigestAlgorithm">The hash algorithm of the image digest.</param>
/// <param name="Timestamp">
/// The RFC 3161 timestamp counter signature, if there is one that .NET can decode (see <paramref name="TimestampInfo"/>).
/// </param>
/// <param name="TimestampInfo">The verified TSTInfo of the RFC 3161 timestamp, if there is one.</param>
internal sealed record AuthenticodeVerificationResult(
    SignedCms SignedCms,
    X509Certificate2 SignerCertificate,
    HashAlgorithmName DigestAlgorithm,
    Rfc3161TimestampToken? Timestamp,
    Rfc3161TimestampTokenInfo? TimestampInfo);

/// <summary>
/// Platform independent structural verification of an Authenticode signed PE image: the embedded image digest matches
/// the file, the signature is valid for the embedded signer certificate, and any RFC 3161 timestamp is valid for the
/// signature. Certificate trust is NOT evaluated.
/// </summary>
internal static class AuthenticodeVerifier
{
    private const string TstInfoContentTypeOid = "1.2.840.113549.1.9.16.1.4";

    /// <summary>Verifies <paramref name="path"/>, throwing a <see cref="CryptographicException"/> if it is not validly signed.</summary>
    public static AuthenticodeVerificationResult VerifyFile(string path)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
        var layout = PeAuthenticodeLayout.Read(stream, path);
        byte[] pkcs7 = PeAuthenticode.ReadEmbeddedPkcs7(stream, layout)
                       ?? throw new CryptographicException($"'{path}' does not contain an Authenticode signature.");

        var cms = new SignedCms();
        cms.Decode(pkcs7);
        if (cms.ContentInfo.ContentType.Value != AuthenticodeAsn.SpcIndirectDataContentOid) {
            throw new CryptographicException($"'{path}' has a signature with unexpected content type {cms.ContentInfo.ContentType.Value}.");
        }

        byte[] embeddedDigest = AuthenticodeAsn.ReadIndirectDataDigest(cms.ContentInfo.Content, out string digestOid);
        var digestAlgorithm = AuthenticodeAsn.GetHashAlgorithmName(digestOid);
        byte[] actualDigest = PeAuthenticode.ComputeDigest(stream, layout, digestAlgorithm);
        if (!actualDigest.AsSpan().SequenceEqual(embeddedDigest)) {
            throw new CryptographicException($"'{path}' has been modified after it was signed (Authenticode digest mismatch).");
        }

        if (cms.SignerInfos.Count != 1) {
            throw new CryptographicException($"'{path}' must have exactly one signer, but has {cms.SignerInfos.Count}.");
        }

        SignerInfo signerInfo = cms.SignerInfos[0];
        signerInfo.CheckSignature(verifySignatureOnly: true);
        var signerCertificate = signerInfo.Certificate
                                ?? throw new CryptographicException($"'{path}' does not embed its signing certificate.");

        Rfc3161TimestampToken? timestamp = null;
        Rfc3161TimestampTokenInfo? timestampInfo = null;
        foreach (var attribute in signerInfo.UnsignedAttributes) {
            if (attribute.Oid.Value != AuthenticodeAsn.Rfc3161CounterSignatureOid) continue;
            if (attribute.Values.Count != 1) {
                throw new CryptographicException($"'{path}' has a timestamp attribute with {attribute.Values.Count} values.");
            }

            byte[] rawToken = attribute.Values[0].RawData;
            if (Rfc3161TimestampToken.TryDecode(rawToken, out timestamp, out _)) {
                if (!timestamp.VerifySignatureForSignerInfo(signerInfo, out _)) {
                    throw new CryptographicException($"'{path}' has a timestamp that does not match its signature.");
                }

                timestampInfo = timestamp.TokenInfo;
            } else {
                timestampInfo = VerifyTimestampFallback(path, rawToken, signerInfo);
            }
        }

        return new AuthenticodeVerificationResult(cms, signerCertificate, digestAlgorithm, timestamp, timestampInfo);
    }

    /// <summary>
    /// Verifies a timestamp token that <see cref="Rfc3161TimestampToken.TryDecode"/> rejects. Its strict checks (e.g. of
    /// the signing-certificate attribute) fail on some tokens Windows accepts, such as those on many Microsoft binaries,
    /// so this only checks what the rest of the verifier relies on: the token is a validly signed TSTInfo whose message
    /// imprint is the hash of the Authenticode signature.
    /// </summary>
    private static Rfc3161TimestampTokenInfo VerifyTimestampFallback(string path, byte[] rawToken, SignerInfo signerInfo)
    {
        var token = new SignedCms();
        try {
            token.Decode(rawToken);
        } catch (CryptographicException ex) {
            throw new CryptographicException($"'{path}' has a timestamp that could not be decoded.", ex);
        }

        if (token.ContentInfo.ContentType.Value != TstInfoContentTypeOid
            || !Rfc3161TimestampTokenInfo.TryDecode(token.ContentInfo.Content, out var info, out _)) {
            throw new CryptographicException($"'{path}' has a timestamp that could not be decoded.");
        }

        token.CheckSignature(verifySignatureOnly: true);

        var hashAlgorithm = AuthenticodeAsn.GetHashAlgorithmName(info.HashAlgorithmId.Value!);
        using var hash = IncrementalHash.CreateHash(hashAlgorithm);
        hash.AppendData(signerInfo.GetSignature());
        if (!info.GetMessageHash().Span.SequenceEqual(hash.GetHashAndReset())) {
            throw new CryptographicException($"'{path}' has a timestamp that does not match its signature.");
        }

        return info;
    }
}
