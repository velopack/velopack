#nullable enable
using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// Builds the Authenticode PKCS#7 SignedData for a PE image digest.
/// </summary>
public sealed class AuthenticodeCmsBuilder
{
    private static readonly HashAlgorithmName DigestAlgorithm = HashAlgorithmName.SHA256;

    private readonly AuthenticodeSigningKey _key;
    private readonly Rfc3161Timestamper? _timestamper;

    public AuthenticodeCmsBuilder(AuthenticodeSigningKey key, Rfc3161Timestamper? timestamper)
    {
        _key = key ?? throw new ArgumentNullException(nameof(key));
        _timestamper = timestamper;
    }

    /// <summary>The hash algorithm the image digest passed to <see cref="CreateSignature"/> must be computed with.</summary>
    public static HashAlgorithmName FileDigestAlgorithm => DigestAlgorithm;

    /// <summary>
    /// Signs a SHA-256 PE image digest (and optionally timestamps it), returning the DER encoded Authenticode
    /// SignedData to embed in the image.
    /// </summary>
    /// <param name="fileDigest">The Authenticode digest of the image, see <see cref="PeAuthenticode.ComputeDigest"/>.</param>
    /// <param name="programName">Description shown by Windows for the signature (SpcSpOpusInfo programName).</param>
    public byte[] CreateSignature(byte[] fileDigest, string? programName)
    {
        byte[] spcIndirectDataContent = AuthenticodeAsn.BuildSpcIndirectDataContent(fileDigest, DigestAlgorithm);

        // PKCS#7 v1.5 computes messageDigest over the *contents* of the SpcIndirectDataContent SEQUENCE (without its
        // tag and length), so SignedCms is given just those bytes; the full SEQUENCE is put back by the re-encoder.
        AsnDecoder.ReadEncodedValue(spcIndirectDataContent, AsnEncodingRules.DER, out int contentOffset, out int contentLength, out _);
        var contentInfo = new ContentInfo(
            new Oid(AuthenticodeAsn.SpcIndirectDataContentOid),
            spcIndirectDataContent.AsSpan(contentOffset, contentLength).ToArray());

        var cms = new SignedCms(contentInfo, detached: false);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, _key.Certificate, _key.PrivateKey, RSASignaturePadding.Pkcs1) {
            DigestAlgorithm = new Oid(AuthenticodeAsn.GetHashAlgorithmOid(DigestAlgorithm)),
            // never let .NET build a chain itself (that may hit the network on Linux / macOS); intermediates are explicit
            IncludeOption = X509IncludeOption.EndCertOnly,
        };
        signer.Certificates.AddRange(_key.Intermediates);
        signer.SignedAttributes.Add(new AsnEncodedData(AuthenticodeAsn.SpcSpOpusInfoOid, AuthenticodeAsn.BuildSpcSpOpusInfo(programName)));
        signer.SignedAttributes.Add(new AsnEncodedData(AuthenticodeAsn.SpcStatementTypeOid, AuthenticodeAsn.SpcStatementTypeValue.ToArray()));

        // this calls RSA.SignHash on the (remote) key, and checks the result against the certificate's public key
        cms.ComputeSignature(signer, silent: true);

        if (_timestamper != null) {
            SignerInfo signerInfo = cms.SignerInfos[0];
            byte[] token = _timestamper.RequestToken(signerInfo);
            signerInfo.AddUnsignedAttribute(new AsnEncodedData(AuthenticodeAsn.Rfc3161CounterSignatureOid, token));
        }

        byte[] der = AuthenticodeAsn.ReencodeAsAuthenticode(cms.Encode(), spcIndirectDataContent);
        SelfVerify(der, fileDigest, timestamped: _timestamper != null);
        return der;
    }

    private void SelfVerify(byte[] der, byte[] fileDigest, bool timestamped)
    {
        var cms = new SignedCms();
        cms.Decode(der);

        if (cms.Version != 1 || cms.ContentInfo.ContentType.Value != AuthenticodeAsn.SpcIndirectDataContentOid) {
            throw new CryptographicException("The Authenticode signature was not encoded as expected.");
        }

        byte[] embeddedDigest = AuthenticodeAsn.ReadIndirectDataDigest(cms.ContentInfo.Content, out _);
        if (!embeddedDigest.AsSpan().SequenceEqual(fileDigest)) {
            throw new CryptographicException("The Authenticode signature does not contain the expected file digest.");
        }

        if (cms.SignerInfos.Count != 1) {
            throw new CryptographicException("The Authenticode signature must contain exactly one signer.");
        }

        SignerInfo signerInfo = cms.SignerInfos[0];
        if (signerInfo.Certificate == null || !signerInfo.Certificate.RawData.AsSpan().SequenceEqual(_key.Certificate.RawData)) {
            throw new CryptographicException("The Authenticode signature does not reference the signing certificate.");
        }

        signerInfo.CheckSignature(verifySignatureOnly: true);

        if (timestamped) {
            var attribute = signerInfo.UnsignedAttributes.Cast<CryptographicAttributeObject>()
                .FirstOrDefault(a => a.Oid.Value == AuthenticodeAsn.Rfc3161CounterSignatureOid);
            if (attribute == null || attribute.Values.Count != 1
                || !Rfc3161TimestampToken.TryDecode(attribute.Values[0].RawData, out var token, out _)
                || !token.VerifySignatureForSignerInfo(signerInfo, out _)) {
                throw new CryptographicException("The Authenticode signature timestamp could not be verified.");
            }
        }
    }
}
