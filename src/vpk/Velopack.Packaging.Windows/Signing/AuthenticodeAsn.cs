#nullable enable
// The Authenticode ASN.1 structures in this file are adapted from PowerShell-OpenAuthenticode
// (https://github.com/jborean93/PowerShell-OpenAuthenticode, MIT License, Copyright (c) 2023 Jordan Borean).
// The PKCS#7 v1.5 re-encoding (SignedData version 1, eContent as a SEQUENCE, explicit NULL algorithm parameters)
// matches what signtool, osslsigncode and jsign produce.

using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Text;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// OIDs and DER encoders / decoders for the Authenticode specific ASN.1 structures.
/// </summary>
public static class AuthenticodeAsn
{
    public const string SignedDataOid = "1.2.840.113549.1.7.2";
    public const string SpcIndirectDataContentOid = "1.3.6.1.4.1.311.2.1.4";
    public const string SpcPeImageDataOid = "1.3.6.1.4.1.311.2.1.15";
    public const string SpcStatementTypeOid = "1.3.6.1.4.1.311.2.1.11";
    public const string SpcSpOpusInfoOid = "1.3.6.1.4.1.311.2.1.12";
    public const string IndividualCodeSigningOid = "1.3.6.1.4.1.311.2.1.21";
    public const string Rfc3161CounterSignatureOid = "1.3.6.1.4.1.311.3.3.1";
    public const string ContentTypeOid = "1.2.840.113549.1.9.3";
    public const string MessageDigestOid = "1.2.840.113549.1.9.4";
    public const string SigningTimeOid = "1.2.840.113549.1.9.5";
    public const string CmsAlgorithmProtectionOid = "1.2.840.113549.1.9.52";
    public const string RsaEncryptionOid = "1.2.840.113549.1.1.1";
    public const string Sha1Oid = "1.3.14.3.2.26";
    public const string Sha256Oid = "2.16.840.1.101.3.4.2.1";
    public const string Sha384Oid = "2.16.840.1.101.3.4.2.2";
    public const string Sha512Oid = "2.16.840.1.101.3.4.2.3";

    /// <summary>
    /// SpcPeImageData { flags BIT STRING (empty), file [0] SpcLink { [2] SpcString { [0] unicode "" } } }, byte for
    /// byte what signtool (and jsign) emit.
    /// </summary>
    public static ReadOnlySpan<byte> SpcPeImageDataValue => [0x30, 0x09, 0x03, 0x01, 0x00, 0xA0, 0x04, 0xA2, 0x02, 0x80, 0x00];

    /// <summary>SpcStatementType { individualCodeSigning }.</summary>
    public static ReadOnlySpan<byte> SpcStatementTypeValue =>
        [0x30, 0x0C, 0x06, 0x0A, 0x2B, 0x06, 0x01, 0x04, 0x01, 0x82, 0x37, 0x02, 0x01, 0x15];

    public static string GetHashAlgorithmOid(HashAlgorithmName hashAlgorithm)
    {
        if (hashAlgorithm == HashAlgorithmName.SHA256) return Sha256Oid;
        if (hashAlgorithm == HashAlgorithmName.SHA384) return Sha384Oid;
        if (hashAlgorithm == HashAlgorithmName.SHA512) return Sha512Oid;
        if (hashAlgorithm == HashAlgorithmName.SHA1) return Sha1Oid;
        throw new NotSupportedException($"Hash algorithm '{hashAlgorithm.Name}' is not supported for Authenticode.");
    }

    public static HashAlgorithmName GetHashAlgorithmName(string oid)
    {
        return oid switch {
            Sha256Oid => HashAlgorithmName.SHA256,
            Sha384Oid => HashAlgorithmName.SHA384,
            Sha512Oid => HashAlgorithmName.SHA512,
            Sha1Oid => HashAlgorithmName.SHA1,
            _ => throw new NotSupportedException($"Hash algorithm '{oid}' is not supported for Authenticode."),
        };
    }

    /// <summary>
    /// Builds the full DER encoding of
    /// <c>SpcIndirectDataContent { data SpcAttributeTypeAndOptionalValue, messageDigest DigestInfo }</c>
    /// for a PE image digest.
    /// </summary>
    public static byte[] BuildSpcIndirectDataContent(byte[] digest, HashAlgorithmName hashAlgorithm)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence()) {
            using (writer.PushSequence()) {
                writer.WriteObjectIdentifier(SpcPeImageDataOid);
                writer.WriteEncodedValue(SpcPeImageDataValue);
            }

            using (writer.PushSequence()) {
                using (writer.PushSequence()) {
                    writer.WriteObjectIdentifier(GetHashAlgorithmOid(hashAlgorithm));
                    writer.WriteNull();
                }

                writer.WriteOctetString(digest);
            }
        }

        return writer.Encode();
    }

    /// <summary>
    /// Reads the image digest (and its algorithm OID) out of a DER encoded SpcIndirectDataContent.
    /// </summary>
    public static byte[] ReadIndirectDataDigest(ReadOnlyMemory<byte> spcIndirectDataContent, out string digestAlgorithmOid)
    {
        var reader = new AsnReader(spcIndirectDataContent, AsnEncodingRules.BER);
        var sequence = reader.ReadSequence();
        sequence.ReadSequence(); // SpcAttributeTypeAndOptionalValue
        var digestInfo = sequence.ReadSequence();
        var algorithm = digestInfo.ReadSequence();
        digestAlgorithmOid = algorithm.ReadObjectIdentifier();
        return digestInfo.ReadOctetString();
    }

    /// <summary>
    /// Builds <c>SpcSpOpusInfo { programName [0] SpcString OPTIONAL }</c> with the program name as a unicode
    /// (BMPString) SpcString, like signtool's /d option. Returns an empty SEQUENCE when there is no name.
    /// </summary>
    public static byte[] BuildSpcSpOpusInfo(string? programName)
    {
        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence()) {
            if (!String.IsNullOrEmpty(programName)) {
                using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true))) {
                    // signtool copies the UTF-16 buffer as-is (surrogate pairs included) and Windows displays it, so the
                    // value is written as raw UTF-16BE rather than via AsnWriter's BMPString, which rejects surrogates.
                    writer.WriteOctetString(
                        Encoding.BigEndianUnicode.GetBytes(ReplaceLoneSurrogates(programName)),
                        new Asn1Tag(TagClass.ContextSpecific, 0));
                }
            }
        }

        return writer.Encode();
    }

    /// <summary>
    /// Replaces unpaired UTF-16 surrogates with '?'. Valid surrogate pairs (characters outside the basic multilingual
    /// plane) are kept.
    /// </summary>
    public static string ReplaceLoneSurrogates(string value)
    {
        var sb = new StringBuilder(value.Length);
        for (int i = 0; i < value.Length; i++) {
            char c = value[i];
            if (Char.IsHighSurrogate(c) && i + 1 < value.Length && Char.IsLowSurrogate(value[i + 1])) {
                sb.Append(c).Append(value[i + 1]);
                i++;
            } else if (Char.IsSurrogate(c)) {
                sb.Append('?');
            } else {
                sb.Append(c);
            }
        }

        return sb.ToString();
    }

    /// <summary>
    /// Converts the CMS encoding produced by <see cref="System.Security.Cryptography.Pkcs.SignedCms"/> into the PKCS#7
    /// v1.5 form that Authenticode verifiers expect: SignedData version 1, the SpcIndirectDataContent embedded as a
    /// SEQUENCE (not wrapped in an OCTET STRING), and explicit NULL parameters on the digest and signature algorithm
    /// identifiers. The signed attributes, signature and unsigned attributes (timestamp) are copied verbatim, so the
    /// signature stays valid as long as SignedCms was given the SpcIndirectDataContent without its SEQUENCE header.
    /// </summary>
    public static byte[] ReencodeAsAuthenticode(byte[] cmsDer, byte[] spcIndirectDataContent)
    {
        var contentInfo = new AsnReader(cmsDer, AsnEncodingRules.DER).ReadSequence();
        string contentType = contentInfo.ReadObjectIdentifier();
        if (contentType != SignedDataOid) {
            throw new CryptographicException($"Expected a SignedData content type but found '{contentType}'.");
        }

        var signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
        signedData.ReadInteger(); // version, rewritten as 1 below
        var digestAlgorithms = signedData.ReadSetOf();
        var encapsulatedContentInfo = signedData.ReadSequence();
        string encapsulatedContentType = encapsulatedContentInfo.ReadObjectIdentifier();

        // certificates [0] and crls [1] are copied as-is, then the SignerInfos SET
        var optionalFields = new List<ReadOnlyMemory<byte>>();
        ReadOnlyMemory<byte>? signerInfos = null;
        while (signedData.HasData) {
            var tag = signedData.PeekTag();
            var value = signedData.ReadEncodedValue();
            if (tag.HasSameClassAndValue(Asn1Tag.SetOf)) {
                signerInfos = value;
            } else {
                optionalFields.Add(value);
            }
        }

        if (signerInfos == null) {
            throw new CryptographicException("SignedData does not contain any SignerInfos.");
        }

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence()) {
            writer.WriteObjectIdentifier(contentType);
            using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true)))
            using (writer.PushSequence()) {
                writer.WriteInteger(1);

                using (writer.PushSetOf()) {
                    while (digestAlgorithms.HasData) {
                        WriteAlgorithmIdentifierWithNullParameters(writer, digestAlgorithms.ReadEncodedValue());
                    }
                }

                using (writer.PushSequence()) {
                    writer.WriteObjectIdentifier(encapsulatedContentType);
                    using (writer.PushSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true))) {
                        writer.WriteEncodedValue(spcIndirectDataContent);
                    }
                }

                foreach (var field in optionalFields) {
                    writer.WriteEncodedValue(field.Span);
                }

                var signerInfoSet = new AsnReader(signerInfos.Value, AsnEncodingRules.DER).ReadSetOf();
                using (writer.PushSetOf()) {
                    while (signerInfoSet.HasData) {
                        var signerInfo = signerInfoSet.ReadSequence();
                        using (writer.PushSequence()) {
                            writer.WriteEncodedValue(signerInfo.ReadEncodedValue().Span); // version
                            writer.WriteEncodedValue(signerInfo.ReadEncodedValue().Span); // sid
                            WriteAlgorithmIdentifierWithNullParameters(writer, signerInfo.ReadEncodedValue()); // digestAlgorithm
                            if (signerInfo.PeekTag().HasSameClassAndValue(new Asn1Tag(TagClass.ContextSpecific, 0))) {
                                writer.WriteEncodedValue(signerInfo.ReadEncodedValue().Span); // signedAttrs (covered by signature)
                            }

                            WriteAlgorithmIdentifierWithNullParameters(writer, signerInfo.ReadEncodedValue()); // signatureAlgorithm
                            while (signerInfo.HasData) {
                                writer.WriteEncodedValue(signerInfo.ReadEncodedValue().Span); // signature, unsignedAttrs
                            }
                        }
                    }
                }
            }
        }

        return writer.Encode();
    }

    private static void WriteAlgorithmIdentifierWithNullParameters(AsnWriter writer, ReadOnlyMemory<byte> algorithmIdentifier)
    {
        var reader = new AsnReader(algorithmIdentifier, AsnEncodingRules.DER).ReadSequence();
        using (writer.PushSequence()) {
            writer.WriteObjectIdentifier(reader.ReadObjectIdentifier());
            if (reader.HasData) {
                while (reader.HasData) {
                    writer.WriteEncodedValue(reader.ReadEncodedValue().Span);
                }
            } else {
                writer.WriteNull();
            }
        }
    }
}
