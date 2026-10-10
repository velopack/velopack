#nullable enable
using System.Buffers.Binary;
using System.Formats.Asn1;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using Microsoft.Security.Extensions;
using Neovolve.Logging.Xunit;
using Velopack.Core;
using Velopack.Packaging.Windows;
using Velopack.Packaging.Windows.Signing;
using Velopack.Util;

namespace Velopack.Packaging.Tests.Signing;

public class AuthenticodeSigningTests
{
    private const string ProgramName = "My App";

    private static readonly string[] UnsignedFixtures = [
        "NotSquirrelAwareApp.exe", // PE32
        "PublishSingleFileAwareApp.exe", // PE32+, single-file bundle overlay, length % 8 == 4
        "LegacyTestApp-Velopack1298-Setup.exe", // PE32, setup with an appended package
    ];

    private readonly ITestOutputHelper _output;

    public AuthenticodeSigningTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static TestSigningCertificates Certs => TestSigningCertificates.Shared;

    public static TheoryData<string> UnsignedFixtureData => new(UnsignedFixtures);

    public static TheoryData<string> SignedFixtureData => new("obs29.1.2.dll", "atom.exe", "signtool.exe");

    private static string GetFixturePath(string name)
    {
        return name == "signtool.exe" ? Path.Combine(PathHelper.GetVendorLibDir(), "signing", "signtool.exe") : PathHelper.GetFixture(name);
    }

    private static string CopyFixture(string name, string dir, string? newName = null)
    {
        var target = Path.Combine(dir, newName ?? name);
        File.Copy(GetFixturePath(name), target);
        return target;
    }

    private static PeAuthenticodeLayout ReadLayout(string path)
    {
        using var fs = File.OpenRead(path);
        return PeAuthenticodeLayout.Read(fs, path);
    }

    private static void SignFile(string path, AuthenticodeSigningKey? key = null, Rfc3161Timestamper? timestamper = null,
        string? programName = ProgramName)
    {
        new PeAuthenticodeSigner(NullLogger.Instance).SignFile(path, key ?? Certs.CreateKey(), timestamper, programName);
    }

    private static byte[] ReadEmbeddedSignature(string path)
    {
        using var fs = File.OpenRead(path);
        var layout = PeAuthenticodeLayout.Read(fs, path);
        return PeAuthenticode.ReadEmbeddedPkcs7(fs, layout) ?? throw new Exception("No signature");
    }

    // ---------------------------------------------------------------------------------------------------------------
    // Independent reference implementations

    private static (int CheckSumOffset, int SecurityDirectoryOffset) NaiveOffsets(byte[] file)
    {
        int pe = BitConverter.ToInt32(file, 0x3C);
        int optionalHeader = pe + 24;
        bool pe32Plus = BitConverter.ToUInt16(file, optionalHeader) == 0x20B;
        return (optionalHeader + 64, optionalHeader + (pe32Plus ? 144 : 128));
    }

    private static byte[] NaiveDigest(byte[] file, HashAlgorithmName? algorithm = null)
    {
        var (checksum, directory) = NaiveOffsets(file);
        uint certOffset = BitConverter.ToUInt32(file, directory);
        uint certSize = BitConverter.ToUInt32(file, directory + 4);
        int end = certOffset != 0 && certSize != 0 ? (int) certOffset : file.Length;

        var ms = new MemoryStream();
        ms.Write(file, 0, checksum);
        ms.Write(file, checksum + 4, directory - checksum - 4);
        ms.Write(file, directory + 8, end - directory - 8);
        // the padding aligns the end of the content (not the number of hashed bytes) to 8 bytes
        for (int i = end; i % 8 != 0; i++) ms.WriteByte(0);
        using var hash = IncrementalHash.CreateHash(algorithm ?? HashAlgorithmName.SHA256);
        hash.AppendData(ms.ToArray());
        return hash.GetHashAndReset();
    }

    private static uint NaiveChecksum(byte[] file)
    {
        var (checksumOffset, _) = NaiveOffsets(file);
        var copy = (byte[]) file.Clone();
        Array.Clear(copy, checksumOffset, 4);
        ulong sum = 0;
        for (int i = 0; i + 1 < copy.Length; i += 2) {
            sum += BitConverter.ToUInt16(copy, i);
        }

        if (copy.Length % 2 == 1) sum += copy[^1];
        while (sum >> 16 != 0) sum = (sum & 0xFFFF) + (sum >> 16);
        return (uint) sum + (uint) copy.Length;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 13.1 Digest / layout

    [Theory]
    [MemberData(nameof(SignedFixtureData))]
    public void ComputeDigest_MatchesExistingThirdPartySignatures(string fixture)
    {
        var path = GetFixturePath(fixture);
        using var fs = File.OpenRead(path);
        var layout = PeAuthenticodeLayout.Read(fs, path);
        Assert.True(layout.HasCertificateTable);

        var cms = new SignedCms();
        cms.Decode(PeAuthenticode.ReadEmbeddedPkcs7(fs, layout)!);
        byte[] expected = AuthenticodeAsn.ReadIndirectDataDigest(cms.ContentInfo.Content, out var oid);
        byte[] actual = PeAuthenticode.ComputeDigest(fs, layout, AuthenticodeAsn.GetHashAlgorithmName(oid));
        Assert.Equal(Convert.ToHexString(expected), Convert.ToHexString(actual));

        var result = AuthenticodeVerifier.VerifyFile(path);
        _output.WriteLine($"{fixture}: {oid} signed by {result.SignerCertificate.Subject}");
        Assert.True(PeAuthenticode.HasCertificateTable(path));
    }

    [Theory]
    [MemberData(nameof(SignedFixtureData))]
    public void ComputeChecksum_MatchesChecksumWrittenBySigners(string fixture)
    {
        var path = GetFixturePath(fixture);
        var bytes = File.ReadAllBytes(path);
        var layout = ReadLayout(path);
        uint stored = BitConverter.ToUInt32(bytes, (int) layout.CheckSumOffset);
        using var fs = File.OpenRead(path);
        uint computed = PeAuthenticode.ComputeChecksum(fs, layout.CheckSumOffset);
        Assert.Equal(NaiveChecksum(bytes), computed);
        if (stored != 0) {
            Assert.Equal(stored, computed);
        }
    }

    [Theory]
    [MemberData(nameof(UnsignedFixtureData))]
    public void ComputeDigest_MatchesNaiveImplementation(string fixture)
    {
        var path = GetFixturePath(fixture);
        var bytes = File.ReadAllBytes(path);
        using var fs = File.OpenRead(path);
        var layout = PeAuthenticodeLayout.Read(fs, path);
        Assert.False(layout.HasCertificateTable);
        var digest = PeAuthenticode.ComputeDigest(fs, layout, HashAlgorithmName.SHA256);
        Assert.Equal(Convert.ToHexString(NaiveDigest(bytes)), Convert.ToHexString(digest));
        Assert.Equal(NaiveChecksum(bytes), PeAuthenticode.ComputeChecksum(fs, layout.CheckSumOffset));
        Assert.False(PeAuthenticode.HasCertificateTable(path));
    }

    [Fact]
    public void ComputeChecksum_HandlesOddLengthAndUnalignedField()
    {
        // a synthetic buffer: checksum field straddling a word boundary and an odd trailing byte
        var data = RandomNumberGenerator.GetBytes(1001);
        using var ms = new MemoryStream(data);
        uint actual = PeAuthenticode.ComputeChecksum(ms, 101);

        var copy = (byte[]) data.Clone();
        Array.Clear(copy, 101, 4);
        ulong sum = 0;
        for (int i = 0; i + 1 < copy.Length; i += 2) sum += BitConverter.ToUInt16(copy, i);
        sum += copy[^1];
        while (sum >> 16 != 0) sum = (sum & 0xFFFF) + (sum >> 16);
        Assert.Equal((uint) sum + 1001u, actual);
    }

    [Theory]
    [InlineData("NotSquirrelAwareApp.exe", false)]
    [InlineData("LegacyTestApp-Velopack1298-Setup.exe", false)]
    [InlineData("PublishSingleFileAwareApp.exe", true)]
    [InlineData("obs29.1.2.dll", true)]
    public void Layout_PE32_and_PE32Plus_Offsets(string fixture, bool pe32Plus)
    {
        var path = GetFixturePath(fixture);
        var bytes = File.ReadAllBytes(path);
        var layout = ReadLayout(path);
        var (checksum, directory) = NaiveOffsets(bytes);
        Assert.Equal(pe32Plus, layout.IsPe32Plus);
        Assert.Equal(checksum, layout.CheckSumOffset);
        Assert.Equal(directory, layout.SecurityDirectoryOffset);
        Assert.Equal(bytes.Length, layout.FileLength);
    }

    public static TheoryData<string> MalformedCases => new(
        "cert-not-at-eof", "cert-dir-into-sections", "cert-entry-length-mismatch", "rva-count-4", "text-file", "truncated-after-pe",
        "bad-magic");

    [Theory]
    [MemberData(nameof(MalformedCases))]
    public void Malformed_IsRejected_AndFileUntouched(string kind)
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        string path = Path.Combine(dir, "bad.dll");
        byte[] content;
        switch (kind) {
        case "cert-not-at-eof":
            content = [.. File.ReadAllBytes(GetFixturePath("obs29.1.2.dll")), .. new byte[16]];
            break;
        case "cert-dir-into-sections": {
            // a bogus table that runs to EOF but starts inside the section data; replacing it would truncate the image
            content = File.ReadAllBytes(GetFixturePath("NotSquirrelAwareApp.exe"));
            var (_, directory) = NaiveOffsets(content);
            BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(directory), 0x400);
            BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(directory + 4), (uint) content.Length - 0x400);
            break;
        }
        case "cert-entry-length-mismatch": {
            // a table at EOF whose WIN_CERTIFICATE dwLength values do not add up to the directory size
            var original = File.ReadAllBytes(GetFixturePath("obs29.1.2.dll"));
            var (_, directory) = NaiveOffsets(original);
            uint certOffset = BitConverter.ToUInt32(original, directory);
            content = [.. original, .. new byte[16]];
            BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(directory + 4), (uint) content.Length - certOffset);
            break;
        }
        case "rva-count-4": {
            content = File.ReadAllBytes(GetFixturePath("NotSquirrelAwareApp.exe"));
            int optionalHeader = BitConverter.ToInt32(content, 0x3C) + 24;
            BinaryPrimitives.WriteUInt32LittleEndian(content.AsSpan(optionalHeader + 92), 4);
            break;
        }
        case "text-file":
            content = Encoding.UTF8.GetBytes(String.Join("\n", Enumerable.Repeat("this is not a dll", 20)));
            break;
        case "truncated-after-pe": {
            var full = File.ReadAllBytes(GetFixturePath("NotSquirrelAwareApp.exe"));
            content = full.AsSpan(0, BitConverter.ToInt32(full, 0x3C) + 4).ToArray();
            break;
        }
        case "bad-magic": {
            content = File.ReadAllBytes(GetFixturePath("NotSquirrelAwareApp.exe"));
            int optionalHeader = BitConverter.ToInt32(content, 0x3C) + 24;
            BinaryPrimitives.WriteUInt16LittleEndian(content.AsSpan(optionalHeader), 0x1234);
            break;
        }
        default:
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        File.WriteAllBytes(path, content);
        var ex = Assert.Throws<UserInfoException>(() => SignFile(path));
        _output.WriteLine(ex.Message);
        Assert.Contains(path, ex.Message);
        Assert.Equal(content, File.ReadAllBytes(path));
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 13.2 Sign + parse back

    [Theory]
    [MemberData(nameof(UnsignedFixtureData))]
    public void Sign_Unsigned(string fixture)
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture(fixture, dir);
        var original = File.ReadAllBytes(path);
        var (checksumOffset, directoryOffset) = NaiveOffsets(original);

        SignFile(path);

        var signed = File.ReadAllBytes(path);
        var result = AuthenticodeVerifier.VerifyFile(path);
        Assert.Equal(Certs.Leaf.RawData, result.SignerCertificate.RawData);
        Assert.Null(result.Timestamp);

        Assert.Equal(0, signed.Length % 8);
        uint certOffset = BitConverter.ToUInt32(signed, directoryOffset);
        uint certSize = BitConverter.ToUInt32(signed, directoryOffset + 4);
        Assert.Equal(signed.Length, (long) certOffset + certSize);
        Assert.Equal(0u, certOffset % 8);
        Assert.Equal(certSize, BitConverter.ToUInt32(signed, (int) certOffset));
        Assert.Equal(0x0200, BitConverter.ToUInt16(signed, (int) certOffset + 4));
        Assert.Equal(2, BitConverter.ToUInt16(signed, (int) certOffset + 6));
        Assert.Equal(NaiveChecksum(signed), BitConverter.ToUInt32(signed, checksumOffset));

        // everything except the checksum and the security directory entry is unchanged; then only zero padding
        for (int i = 0; i < original.Length; i++) {
            bool isChecksum = i >= checksumOffset && i < checksumOffset + 4;
            bool isDirectory = i >= directoryOffset && i < directoryOffset + 8;
            if (!isChecksum && !isDirectory && original[i] != signed[i]) {
                Assert.Fail($"Byte {i} changed");
            }
        }

        Assert.All(signed.AsSpan(original.Length, (int) certOffset - original.Length).ToArray(), b => Assert.Equal(0, b));
        Assert.Equal(fixture == "PublishSingleFileAwareApp.exe" ? original.Length + 4 : original.Length, (int) certOffset);
        Assert.True(PeAuthenticode.HasCertificateTable(path));
    }

    [Fact]
    public void Sign_ReplacesExistingSignature()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("obs29.1.2.dll", dir);
        var original = File.ReadAllBytes(path);
        var oldLayout = ReadLayout(path);

        SignFile(path);

        var signed = File.ReadAllBytes(path);
        var newLayout = ReadLayout(path);
        Assert.Equal(oldLayout.CertificateTableOffset, newLayout.CertificateTableOffset);
        Assert.Equal(signed.Length, (long) newLayout.CertificateTableOffset + newLayout.CertificateTableSize);
        // exactly one WIN_CERTIFICATE spanning the whole table
        Assert.Equal(newLayout.CertificateTableSize, BitConverter.ToUInt32(signed, (int) newLayout.CertificateTableOffset));

        var (checksumOffset, directoryOffset) = NaiveOffsets(original);
        for (int i = 0; i < oldLayout.CertificateTableOffset; i++) {
            bool isChecksum = i >= checksumOffset && i < checksumOffset + 4;
            bool isDirectory = i >= directoryOffset && i < directoryOffset + 8;
            if (!isChecksum && !isDirectory && original[i] != signed[i]) {
                Assert.Fail($"Byte {i} changed");
            }
        }

        var result = AuthenticodeVerifier.VerifyFile(path);
        Assert.Equal(Certs.Leaf.Thumbprint, result.SignerCertificate.Thumbprint);
        Assert.Equal(NaiveChecksum(signed), BitConverter.ToUInt32(signed, checksumOffset));
    }

    [Fact]
    public void Sign_Twice_IsStable()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("PublishSingleFileAwareApp.exe", dir);
        SignFile(path);
        var first = File.ReadAllBytes(path);
        SignFile(path);
        var second = File.ReadAllBytes(path);
        Assert.Equal(first.Length, second.Length);
        // PKCS#1 v1.5 signatures are deterministic and there is no signing time, so the output is identical
        Assert.Equal(first, second);
        AuthenticodeVerifier.VerifyFile(path);
    }

    [Fact]
    public void CmsShape_MatchesAuthenticode()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        SignFile(path);
        byte[] der = ReadEmbeddedSignature(path);

        var contentInfo = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
        Assert.Equal("1.2.840.113549.1.7.2", contentInfo.ReadObjectIdentifier());
        var signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
        Assert.Equal(1, (int) signedData.ReadInteger());

        var digestAlgorithms = signedData.ReadSetOf();
        AssertAlgorithmWithNull(digestAlgorithms.ReadSequence(), AuthenticodeAsn.Sha256Oid);
        Assert.False(digestAlgorithms.HasData);

        var encapsulated = signedData.ReadSequence();
        Assert.Equal(AuthenticodeAsn.SpcIndirectDataContentOid, encapsulated.ReadObjectIdentifier());
        var explicitContent = encapsulated.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0));
        byte[] spcIndirect = explicitContent.ReadEncodedValue().ToArray();
        Assert.Equal(0x30, spcIndirect[0]);
        var spc = new AsnReader(spcIndirect, AsnEncodingRules.DER).ReadSequence();
        var data = spc.ReadSequence();
        Assert.Equal(AuthenticodeAsn.SpcPeImageDataOid, data.ReadObjectIdentifier());
        Assert.Equal("3009030100A004A2028000", Convert.ToHexString(data.ReadEncodedValue().Span));
        var digestInfo = spc.ReadSequence();
        AssertAlgorithmWithNull(digestInfo.ReadSequence(), AuthenticodeAsn.Sha256Oid);
        Assert.Equal(NaiveDigest(File.ReadAllBytes(path)), digestInfo.ReadOctetString());

        var certificates = signedData.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 0));
        var certs = new List<byte[]>();
        while (certificates.HasData) certs.Add(certificates.ReadEncodedValue().ToArray());
        Assert.Equal(2, certs.Count);
        Assert.Contains(certs, c => c.AsSpan().SequenceEqual(Certs.Leaf.RawData));
        Assert.Contains(certs, c => c.AsSpan().SequenceEqual(Certs.Intermediate.RawData));
        Assert.DoesNotContain(certs, c => c.AsSpan().SequenceEqual(Certs.Root.RawData));
        Assert.True(CompareDer(certs[0], certs[1]) < 0, "certificates must be in DER SET OF order");

        var signerInfos = signedData.ReadSetOf();
        Assert.False(signedData.HasData);
        var signerInfo = signerInfos.ReadSequence();
        Assert.False(signerInfos.HasData);
        Assert.Equal(1, (int) signerInfo.ReadInteger());
        var sid = signerInfo.ReadSequence(); // IssuerAndSerialNumber
        Assert.Equal(Certs.Leaf.IssuerName.RawData, sid.ReadEncodedValue().ToArray());
        AssertAlgorithmWithNull(signerInfo.ReadSequence(), AuthenticodeAsn.Sha256Oid);

        var signedAttributes = signerInfo.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 0));
        var attributes = new Dictionary<string, byte[]>();
        while (signedAttributes.HasData) {
            var attribute = signedAttributes.ReadSequence();
            string oid = attribute.ReadObjectIdentifier();
            var values = attribute.ReadSetOf();
            attributes.Add(oid, values.ReadEncodedValue().ToArray());
            Assert.False(values.HasData);
        }

        string[] expectedAttributes = [
            AuthenticodeAsn.ContentTypeOid, AuthenticodeAsn.MessageDigestOid, AuthenticodeAsn.SpcStatementTypeOid, AuthenticodeAsn.SpcSpOpusInfoOid,
        ];
        Assert.Equal(expectedAttributes.OrderBy(x => x), attributes.Keys.OrderBy(x => x));
        Assert.DoesNotContain(AuthenticodeAsn.SigningTimeOid, attributes.Keys);
        Assert.DoesNotContain(AuthenticodeAsn.CmsAlgorithmProtectionOid, attributes.Keys);
        var contentType = new AsnReader(attributes[AuthenticodeAsn.ContentTypeOid], AsnEncodingRules.DER).ReadObjectIdentifier();
        Assert.Equal(AuthenticodeAsn.SpcIndirectDataContentOid, contentType);
        Assert.Equal("300C060A2B060104018237020115", Convert.ToHexString(attributes[AuthenticodeAsn.SpcStatementTypeOid]));
        Assert.Equal(AuthenticodeAsn.BuildSpcSpOpusInfo(ProgramName), attributes[AuthenticodeAsn.SpcSpOpusInfoOid]);
        Assert.Equal(
            "3010A00E800C004D00790020004100700070",
            Convert.ToHexString(attributes[AuthenticodeAsn.SpcSpOpusInfoOid]));

        AsnDecoder.ReadEncodedValue(spcIndirect, AsnEncodingRules.DER, out int contentOffset, out int contentLength, out _);
        byte[] expectedMessageDigest = SHA256.HashData(spcIndirect.AsSpan(contentOffset, contentLength));
        Assert.Equal(expectedMessageDigest, new AsnReader(attributes[AuthenticodeAsn.MessageDigestOid], AsnEncodingRules.DER).ReadOctetString());

        AssertAlgorithmWithNull(signerInfo.ReadSequence(), AuthenticodeAsn.RsaEncryptionOid);
        Assert.Equal(3072 / 8, signerInfo.ReadOctetString().Length);
        Assert.False(signerInfo.HasData); // no unsigned attributes without a timestamp
    }

    private static void AssertAlgorithmWithNull(AsnReader algorithm, string oid)
    {
        Assert.Equal(oid, algorithm.ReadObjectIdentifier());
        algorithm.ReadNull();
        Assert.False(algorithm.HasData);
    }

    private static int CompareDer(byte[] a, byte[] b)
    {
        for (int i = 0; i < Math.Min(a.Length, b.Length); i++) {
            if (a[i] != b[i]) return a[i].CompareTo(b[i]);
        }

        return a.Length.CompareTo(b.Length);
    }

    [Fact]
    public void Opus_NonBmpDescription_RoundTrips()
    {
        Assert.Equal("App \U0001F600", AuthenticodeAsn.ReplaceLoneSurrogates("App \U0001F600"));
        Assert.Equal("a?b", AuthenticodeAsn.ReplaceLoneSurrogates("a\uD800b"));
        Assert.Equal("a?b?", AuthenticodeAsn.ReplaceLoneSurrogates("a\uDC00b\uD800"));

        // { [0] { [0] IMPLICIT raw UTF-16BE } } with the surrogate pair kept, the same bytes signtool /d writes
        var opus = AuthenticodeAsn.BuildSpcSpOpusInfo("App \U0001F600");
        Assert.Equal("3010A00E800C0041007000700020D83DDE00", Convert.ToHexString(opus));
        var programName = new AsnReader(opus, AsnEncodingRules.DER).ReadSequence()
            .ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0, isConstructed: true))
            .ReadOctetString(new Asn1Tag(TagClass.ContextSpecific, 0));
        Assert.Equal("App \U0001F600", Encoding.BigEndianUnicode.GetString(programName));
        Assert.Equal(AuthenticodeAsn.BuildSpcSpOpusInfo("a?b"), AuthenticodeAsn.BuildSpcSpOpusInfo("a\uD800b"));
        Assert.Equal(new byte[] { 0x30, 0x00 }, AuthenticodeAsn.BuildSpcSpOpusInfo(null));
        Assert.Equal(new byte[] { 0x30, 0x00 }, AuthenticodeAsn.BuildSpcSpOpusInfo(""));

        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        SignFile(path, programName: "App \U0001F600");
        AuthenticodeVerifier.VerifyFile(path);
    }

    [Fact]
    public void KeyMismatch_Fails_FileUntouched()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        var original = File.ReadAllBytes(path);
        using var otherKey = RSA.Create(3072);
        var key = Certs.CreateKey(new InMemoryDigestSigner(otherKey));

        Assert.Throws<SigningKeyMismatchException>(() => SignFile(path, key));
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public async Task RemoteFailure_FileUntouched()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        var original = File.ReadAllBytes(path);
        var signer = new InMemoryDigestSigner(Certs.LeafKey) { Intercept = _ => throw new InvalidOperationException("service unavailable") };
        var provider = new StaticAuthenticodeKeyProvider(Certs.CreateKey(signer));

        var ex = await Assert.ThrowsAsync<UserInfoException>(() =>
            new AzureTrustedSigner(NullLogger.Instance).SignFilesAsync([path], provider, null, ProgramName, 1, _ => { }, _ => true));
        Assert.Contains("service unavailable", ex.Message);
        Assert.Contains(path, ex.Message);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void RemoteRsa_ShortSignature_IsLeftPadded()
    {
        // find a digest whose real signature starts with a zero byte (1 in 256), and return it without that byte
        byte[] hash;
        byte[] signature;
        int i = 0;
        do {
            hash = SHA256.HashData(BitConverter.GetBytes(i++));
            signature = Certs.LeafKey.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        } while (signature[0] != 0);

        var shortKey = new RemoteRsa(new FixedSignatureSigner(signature[1..]), Certs.Leaf.GetRSAPublicKey()!);
        var padded = shortKey.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        Assert.Equal(384, padded.Length);
        Assert.Equal(signature, padded);

        var longKey = new RemoteRsa(new FixedSignatureSigner(new byte[385]), Certs.Leaf.GetRSAPublicKey()!);
        Assert.Throws<CryptographicException>(() => longKey.SignHash(hash, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void RemoteRsa_SignatureFromAnotherKey_IsAKeyMismatch()
    {
        using var otherKey = RSA.Create(3072);
        var key = Certs.CreateKey(new InMemoryDigestSigner(otherKey));
        Assert.Throws<SigningKeyMismatchException>(
            () => key.PrivateKey.SignHash(SHA256.HashData("hello"u8), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));

        var zeros = new RemoteRsa(new FixedSignatureSigner(new byte[384]), Certs.Leaf.GetRSAPublicKey()!);
        Assert.Throws<SigningKeyMismatchException>(
            () => zeros.SignHash(SHA256.HashData("hello"u8), HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1));
    }

    [Fact]
    public void RemoteRsa_Properties()
    {
        var key = Certs.CreateKey();
        Assert.Equal(3072, key.PrivateKey.KeySize);
        Assert.Throws<CryptographicException>(() => key.PrivateKey.SignHash(new byte[32], HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
        Assert.Throws<CryptographicException>(() => key.PrivateKey.ExportParameters(true));
        Assert.Equal(Certs.Leaf.GetRSAPublicKey()!.ExportParameters(false).Modulus, key.PrivateKey.ExportParameters(false).Modulus);
        Assert.Throws<NotSupportedException>(() => key.PrivateKey.ImportParameters(new RSAParameters()));
    }

    private sealed class FixedSignatureSigner(byte[] signature) : IRemoteDigestSigner
    {
        public byte[] SignDigest(byte[] digest, HashAlgorithmName hashAlgorithm) => signature;
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 13.3 Timestamp

    private static Rfc3161Timestamper CreateFakeTimestamper(FakeTimestampAuthority tsa)
    {
        return new Rfc3161Timestamper(new HttpClient(tsa), FakeTimestampAuthority.Url, retryDelays: [TimeSpan.Zero, TimeSpan.Zero]);
    }

    [Fact]
    public void Timestamp_Offline_FakeTsa()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        var tsa = new FakeTimestampAuthority(Certs.TimestampAuthority);
        SignFile(path, timestamper: CreateFakeTimestamper(tsa));

        var result = AuthenticodeVerifier.VerifyFile(path);
        Assert.NotNull(result.Timestamp);
        var signerInfo = result.SignedCms.SignerInfos[0];
        Assert.True(result.Timestamp!.VerifySignatureForSignerInfo(signerInfo, out var tsaCert));
        Assert.Equal(Certs.TimestampAuthority.Thumbprint, tsaCert!.Thumbprint);

        var attribute = signerInfo.UnsignedAttributes.Cast<CryptographicAttributeObject>().Single();
        Assert.Equal(AuthenticodeAsn.Rfc3161CounterSignatureOid, attribute.Oid.Value);

        var request = Assert.Single(tsa.Requests);
        Assert.True(request.RequestSignerCertificate);
        Assert.Equal(AuthenticodeAsn.Sha256Oid, request.HashAlgorithmId.Value);
        Assert.Equal(SHA256.HashData(signerInfo.GetSignature()), request.GetMessageHash().ToArray());
        var nonce = request.GetNonce();
        Assert.NotNull(nonce);
        Assert.Equal(8, nonce!.Value.Length);
        Assert.InRange(nonce.Value.Span[0], 1, 0x7F);
        Assert.Equal(nonce.Value.ToArray(), result.Timestamp.TokenInfo.GetNonce()!.Value.ToArray());
    }

    [Fact]
    public void Timestamp_RetriesTransient()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        var tsa = new FakeTimestampAuthority(Certs.TimestampAuthority) { FailFirstRequests = 2 };
        SignFile(path, timestamper: CreateFakeTimestamper(tsa));
        Assert.Equal(3, tsa.RequestCount);
        Assert.NotNull(AuthenticodeVerifier.VerifyFile(path).Timestamp);
    }

    [Fact]
    public void Timestamp_GivesUpAfterThreeAttempts_FileUntouched()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        var original = File.ReadAllBytes(path);
        var tsa = new FakeTimestampAuthority(Certs.TimestampAuthority) { FailFirstRequests = 3 };
        var ex = Assert.Throws<TimestampException>(() => SignFile(path, timestamper: CreateFakeTimestamper(tsa)));
        Assert.Contains("503", ex.Message);
        Assert.Contains("after 3 attempts", ex.Message);
        Assert.True(ex.IsConnectivityFailure);
        Assert.Equal(3, tsa.RequestCount);
        Assert.Equal(original, File.ReadAllBytes(path));
    }

    [Fact]
    public void Timestamp_Rejection_IsAnError()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        var tsa = new FakeTimestampAuthority(Certs.TimestampAuthority) { RejectWithStatus = 2 };
        var ex = Assert.Throws<TimestampException>(() => SignFile(path, timestamper: CreateFakeTimestamper(tsa)));
        // a protocol failure must never be mistaken for the TSA being unreachable (Timestamp_Online_Acs skips on those)
        Assert.False(ex.IsConnectivityFailure);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Verifier_AcceptsTimestampsDotNetCannotDecode(bool imprintMatches)
    {
        // many Microsoft binaries carry timestamp tokens that Rfc3161TimestampToken.TryDecode rejects, but Windows accepts
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        SignFile(path);
        var tsa = new FakeTimestampAuthority(Certs.TimestampAuthority);

        using (var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite)) {
            var layout = PeAuthenticodeLayout.Read(fs, path);
            var cms = new SignedCms();
            cms.Decode(PeAuthenticode.ReadEmbeddedPkcs7(fs, layout)!);
            var signerInfo = cms.SignerInfos[0];
            var tsq = imprintMatches
                ? Rfc3161TimestampRequest.CreateFromSignerInfo(signerInfo, HashAlgorithmName.SHA256, requestSignerCertificates: true)
                : Rfc3161TimestampRequest.CreateFromHash(SHA256.HashData("other"u8), HashAlgorithmName.SHA256, requestSignerCertificates: true);
            byte[] token = tsa.CreateToken(tsq, includeSigningCertificate: false);
            Assert.False(Rfc3161TimestampToken.TryDecode(token, out _, out _));

            signerInfo.AddUnsignedAttribute(new AsnEncodedData(AuthenticodeAsn.Rfc3161CounterSignatureOid, token));
            byte[] digest = PeAuthenticode.ComputeDigest(fs, layout, HashAlgorithmName.SHA256);
            byte[] spc = AuthenticodeAsn.BuildSpcIndirectDataContent(digest, HashAlgorithmName.SHA256);
            PeAuthenticode.EmbedSignature(fs, layout, AuthenticodeAsn.ReencodeAsAuthenticode(cms.Encode(), spc));
        }

        if (imprintMatches) {
            var result = AuthenticodeVerifier.VerifyFile(path);
            Assert.Null(result.Timestamp);
            Assert.NotNull(result.TimestampInfo);
            Assert.Equal(SHA256.HashData(result.SignedCms.SignerInfos[0].GetSignature()), result.TimestampInfo!.GetMessageHash().ToArray());
        } else {
            var ex = Assert.Throws<CryptographicException>(() => AuthenticodeVerifier.VerifyFile(path));
            Assert.Contains("does not match its signature", ex.Message);
        }
    }

    [Fact]
    public void Timestamp_Online_Acs()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        var timestamper = new Rfc3161Timestamper(
            Rfc3161Timestamper.SharedHttpClient,
            new Uri(Rfc3161Timestamper.AzureTimestampUrl),
            retryDelays: [TimeSpan.FromSeconds(1)]);
        try {
            SignFile(path, timestamper: timestamper);
        } catch (TimestampException ex) when (ex.IsConnectivityFailure) {
            // only skip when the TSA could not be reached; a rejected request or bad response is a real failure
            Assert.Skip("Timestamp server is unreachable: " + ex.Message);
        }

        var result = AuthenticodeVerifier.VerifyFile(path);
        Assert.NotNull(result.Timestamp);
        Assert.True(result.Timestamp!.VerifySignatureForSignerInfo(result.SignedCms.SignerInfos[0], out var tsaCert));
        _output.WriteLine("TSA: " + tsaCert!.Subject);
        Assert.Contains("Microsoft", tsaCert.Subject);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 13.5 Orchestration

    [Fact]
    public async Task AzureTrustedSigner_Parallel()
    {
        using var logger = _output.BuildLoggerFor<AuthenticodeSigningTests>();
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var files = Enumerable.Range(0, 12).Select(i => CopyFixture("NotSquirrelAwareApp.exe", dir, $"app{i}.exe")).ToArray();
        // the first 3 signs wait for each other, so reaching 3 concurrent calls does not depend on thread pool timing
        var signer = new InMemoryDigestSigner(Certs.LeafKey, TimeSpan.FromMilliseconds(50)) { WaitForConcurrentCalls = 3 };
        var progress = new List<int>();

        await new AzureTrustedSigner(logger).SignFilesAsync(
            files,
            new StaticAuthenticodeKeyProvider(Certs.CreateKey(signer)),
            null,
            ProgramName,
            3,
            p => { lock (progress) progress.Add(p); },
            _ => true);

        Assert.All(files, f => AuthenticodeVerifier.VerifyFile(f));
        Assert.Equal(12, signer.Calls);
        Assert.Equal(3, signer.MaxConcurrency); // reached, and never exceeded
        Assert.Equal(100, progress.Max());
        Assert.Equal(12, progress.Count);
    }

    [Fact]
    public async Task AzureTrustedSigner_FailureStopsAndThrows()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var good = CopyFixture("NotSquirrelAwareApp.exe", dir, "good.exe");
        var bad = Path.Combine(dir, "bad.dll");
        File.WriteAllText(bad, "not a dll");
        var signer = new AzureTrustedSigner(NullLogger.Instance);
        var provider = new StaticAuthenticodeKeyProvider(Certs.CreateKey());

        var ex = await Assert.ThrowsAsync<UserInfoException>(
            () => signer.SignFilesAsync([good, bad], provider, null, ProgramName, 1, _ => { }, _ => true));
        Assert.Contains(bad, ex.Message);
    }

    [Fact]
    public async Task AzureTrustedSigner_RemoteUserErrorNamesTheFile()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        var failing = new InMemoryDigestSigner(Certs.LeafKey) { Intercept = _ => throw new UserInfoException("Azure said no") };
        var provider = new StaticAuthenticodeKeyProvider(Certs.CreateKey(failing));

        var ex = await Assert.ThrowsAsync<UserInfoException>(
            () => new AzureTrustedSigner(NullLogger.Instance).SignFilesAsync([path], provider, null, ProgramName, 1, _ => { }, _ => true));
        Assert.Contains(path, ex.Message);
        Assert.Contains("Azure said no", ex.Message);
    }

    [Fact]
    public async Task AzureTrustedSigner_UnsupportedFileType()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var txt = Path.Combine(dir, "readme.txt");
        File.WriteAllText(txt, "hello");
        var signer = new AzureTrustedSigner(NullLogger.Instance);
        var provider = new StaticAuthenticodeKeyProvider(Certs.CreateKey());
        var ex = await Assert.ThrowsAsync<UserInfoException>(() => signer.SignFilesAsync([txt], provider, null, null, 1, _ => { }, _ => true));
        Assert.Contains("does not support", ex.Message);
    }

    [Fact]
    public async Task AzureTrustedSigner_RetriesOnceWithRefreshedKeyWhenCertificateRotates()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        var staleSigner = new InMemoryDigestSigner(Certs.LeafKey) {
            Intercept = _ => throw new AzureSigningCertificateChangedException("stale"),
        };
        var provider = new RotatingKeyProvider(Certs.CreateKey(staleSigner), Certs.CreateKey());

        await new AzureTrustedSigner(NullLogger.Instance).SignFilesAsync([path], provider, null, ProgramName, 1, _ => { }, _ => true);
        Assert.Equal(1, provider.Refreshes);
        AuthenticodeVerifier.VerifyFile(path);
    }

    [Fact]
    public async Task AzureTrustedSigner_RetriesOnceWithRefreshedKeyWhenSignatureDoesNotVerify()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        using var otherKey = RSA.Create(3072);
        var provider = new RotatingKeyProvider(Certs.CreateKey(new InMemoryDigestSigner(otherKey)), Certs.CreateKey());

        await new AzureTrustedSigner(NullLogger.Instance).SignFilesAsync([path], provider, null, ProgramName, 1, _ => { }, _ => true);
        Assert.Equal(1, provider.Refreshes);
        AuthenticodeVerifier.VerifyFile(path);
    }

    [Fact]
    public async Task AzureTrustedSigner_OtherCryptographicErrors_DoNotRefreshKey()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        var failing = new InMemoryDigestSigner(Certs.LeafKey) { Intercept = _ => throw new CryptographicException("bad digest") };
        var provider = new RotatingKeyProvider(Certs.CreateKey(failing), Certs.CreateKey());

        var ex = await Assert.ThrowsAsync<UserInfoException>(
            () => new AzureTrustedSigner(NullLogger.Instance).SignFilesAsync([path], provider, null, ProgramName, 1, _ => { }, _ => true));
        Assert.Contains("bad digest", ex.Message);
        Assert.Equal(0, provider.Refreshes);
    }

    [Fact]
    public async Task AzureTrustedSigner_RefreshFailure_ReportsBothErrors()
    {
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture("NotSquirrelAwareApp.exe", dir);
        var staleSigner = new InMemoryDigestSigner(Certs.LeafKey) {
            Intercept = _ => throw new AzureSigningCertificateChangedException("stale"),
        };
        var provider = new RotatingKeyProvider(Certs.CreateKey(staleSigner), Certs.CreateKey()) {
            RefreshFailure = new UserInfoException("Azure Trusted Signing denied the request (HTTP 403)"),
        };

        var ex = await Assert.ThrowsAsync<UserInfoException>(
            () => new AzureTrustedSigner(NullLogger.Instance).SignFilesAsync([path], provider, null, ProgramName, 1, _ => { }, _ => true));
        Assert.Contains(path, ex.Message);
        Assert.Contains("certificate changed", ex.Message);
        Assert.Contains("HTTP 403", ex.Message);
    }

    private sealed class RotatingKeyProvider(AuthenticodeSigningKey first, AuthenticodeSigningKey second) : IAuthenticodeKeyProvider
    {
        private AuthenticodeSigningKey _current = first;
        public int Refreshes { get; private set; }
        public Exception? RefreshFailure { get; init; }

        public AuthenticodeSigningKey GetKey() => _current;

        public AuthenticodeSigningKey? RefreshKey(AuthenticodeSigningKey staleKey)
        {
            Refreshes++;
            if (RefreshFailure != null) throw RefreshFailure;
            return _current = second;
        }
    }

    [Fact]
    public async Task SkipLogic_CertificateTable()
    {
        using var logger = _output.BuildLoggerFor<AuthenticodeSigningTests>();
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var signed = CopyFixture("obs29.1.2.dll", dir);
        var unsigned = CopyFixture("NotSquirrelAwareApp.exe", dir);
        var signedBytes = File.ReadAllBytes(signed);
        Assert.False(AzureTrustedSigner.ShouldSignByCertificateTable(logger, signed));
        Assert.True(AzureTrustedSigner.ShouldSignByCertificateTable(logger, unsigned));
        Assert.False(AzureTrustedSigner.ShouldSignByCertificateTable(logger, Path.Combine(dir, "missing.exe")));

        await new AzureTrustedSigner(logger).SignFilesAsync(
            [signed, unsigned],
            new StaticAuthenticodeKeyProvider(Certs.CreateKey()),
            null,
            ProgramName,
            2,
            _ => { },
            f => AzureTrustedSigner.ShouldSignByCertificateTable(logger, f));

        Assert.Equal(signedBytes, File.ReadAllBytes(signed));
        Assert.Equal(Certs.Leaf.Thumbprint, AuthenticodeVerifier.VerifyFile(unsigned).SignerCertificate.Thumbprint);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task SkipLogic_Windows_TrustedIsSkipped_UntrustedIsReplaced()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var logger = _output.BuildLoggerFor<AuthenticodeSigningTests>();
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var trusted = CopyFixture("signtool.exe", dir);
        var untrusted = CopyFixture("NotSquirrelAwareApp.exe", dir);
        SignFile(untrusted, key: CreateOtherKey(out var otherCertificate));
        Assert.Equal(otherCertificate.Thumbprint, AuthenticodeVerifier.VerifyFile(untrusted).SignerCertificate.Thumbprint);
        var trustedBytes = File.ReadAllBytes(trusted);

        // the default filter on Windows is the trust check
        await new AzureTrustedSigner(logger).SignFilesAsync(
            [trusted, untrusted],
            new StaticAuthenticodeKeyProvider(Certs.CreateKey()),
            null,
            ProgramName,
            2,
            _ => { });

        Assert.Equal(trustedBytes, File.ReadAllBytes(trusted));
        Assert.Equal(Certs.Leaf.Thumbprint, AuthenticodeVerifier.VerifyFile(untrusted).SignerCertificate.Thumbprint);
    }

    private static AuthenticodeSigningKey CreateOtherKey(out X509Certificate2 certificate)
    {
        var rsa = RSA.Create(2048);
        var request = new CertificateRequest("CN=Some Other Signer", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        certificate = request.CreateSelfSigned(DateTimeOffset.UtcNow.AddDays(-1), DateTimeOffset.UtcNow.AddDays(1));
        using var publicCert = X509CertificateLoader.LoadCertificate(certificate.RawData);
        return new AuthenticodeSigningKey(new RemoteRsa(new InMemoryDigestSigner(rsa), publicCert.GetRSAPublicKey()!), certificate, []);
    }

    // ---------------------------------------------------------------------------------------------------------------
    // 13.6 Windows-only verification

    [Theory]
    [MemberData(nameof(UnsignedFixtureData))]
    [SupportedOSPlatform("windows")]
    public void WinVerifyTrust_ReportsOnlyUntrustedRoot(string fixture)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var tempDir = TempUtil.GetTempDirectory(out var dir);

        var plain = CopyFixture(fixture, dir, "plain-" + fixture);
        SignFile(plain);
        AssertOnlyUntrusted(plain);

        var timestamped = CopyFixture(fixture, dir, "ts-" + fixture);
        SignFile(timestamped, timestamper: CreateFakeTimestamper(new FakeTimestampAuthority(Certs.TimestampAuthority)));
        AssertOnlyUntrusted(timestamped);

        // negative control: flip a byte inside the hashed range
        var bytes = File.ReadAllBytes(plain);
        bytes[0x10] ^= 0xFF;
        File.WriteAllBytes(plain, bytes);
        AssertBadDigest(plain);
    }

    [Theory]
    [MemberData(nameof(UnsignedFixtureData))]
    [SupportedOSPlatform("windows")]
    public void WinVerifyTrust_RecognizesTimestamp(string fixture)
    {
        // Trusted Signing leaf certificates live ~3 days, so a timestamp Windows ignores would break signatures soon after the
        // pack. The signature state above is the same chain error with or without it, so ask for the timestamp signer.
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var tempDir = TempUtil.GetTempDirectory(out var dir);

        var plain = CopyFixture(fixture, dir, "plain-" + fixture);
        SignFile(plain);
        Assert.Null(GetTimestampThumbprint(plain));

        var timestamped = CopyFixture(fixture, dir, "ts-" + fixture);
        SignFile(timestamped, timestamper: CreateFakeTimestamper(new FakeTimestampAuthority(Certs.TimestampAuthority)));
        Assert.Equal(Certs.TimestampAuthority.Thumbprint, GetTimestampThumbprint(timestamped));
    }

    // Microsoft.Security.Extensions types only appear in these helpers: the runtime loads the (Windows-only) assembly when it
    // compiles a method that references them, which would fail Linux/macOS test bodies before their Windows skip check runs.
    [SupportedOSPlatform("windows")]
    private static void AssertBadDigest(string path)
    {
        var info = CodeSign.GetSignatureInfo(path);
        Assert.Equal((SignatureState.Invalid, SignatureStateReason.BadDigest), (info.State, info.StateReason));
    }

    [SupportedOSPlatform("windows")]
    private static string? GetTimestampThumbprint(string path) => CodeSign.GetSignatureInfo(path).TimestampCertificate?.Thumbprint;

    [SupportedOSPlatform("windows")]
    private void AssertOnlyUntrusted(string path)
    {
        // Unsigned/Unknown is not specific to an untrusted chain, so also prove the signature itself is intact
        AuthenticodeVerifier.VerifyFile(path);
        Certs.AssertIntactButUntrusted(path);

        // the trust check used to decide what to skip must not treat this signature as trusted
        Assert.False(CodeSign.IsTrusted(path));
    }

    [Theory]
    [MemberData(nameof(UnsignedFixtureData))]
    [SupportedOSPlatform("windows")]
    public void Checksum_MatchesImageHlp(string fixture)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var path = CopyFixture(fixture, dir);
        SignFile(path);
        Assert.Equal(0u, MapFileAndCheckSumW(path, out uint headerSum, out uint checkSum));
        Assert.Equal(checkSum, headerSum);
    }

    [DllImport("imagehlp.dll", CharSet = CharSet.Unicode)]
    private static extern uint MapFileAndCheckSumW(string fileName, out uint headerSum, out uint checkSum);

    [Theory]
    [MemberData(nameof(UnsignedFixtureData))]
    [SupportedOSPlatform("windows")]
    public void Signtool_CrossCheck(string fixture)
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var withSigntool = CopyFixture(fixture, dir, "signtool-" + fixture);
        var withVelopack = CopyFixture(fixture, dir, "velopack-" + fixture);

        string password = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        string pfx = Path.Combine(dir, "test.pfx");
        var collection = new X509Certificate2Collection { Certs.LeafWithKey, Certs.Intermediate };
        File.WriteAllBytes(pfx, collection.Export(X509ContentType.Pfx, password)!);

        var psi = new System.Diagnostics.ProcessStartInfo(HelperFile.SignToolPath) {
            ArgumentList = { "sign", "/f", pfx, "/p", password, "/sha1", Certs.Leaf.Thumbprint, "/fd", "SHA256", "/d", ProgramName, withSigntool },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using (var process = System.Diagnostics.Process.Start(psi)!) {
            string stdout = process.StandardOutput.ReadToEnd();
            string stderr = process.StandardError.ReadToEnd();
            process.WaitForExit();
            _output.WriteLine(stdout + stderr);
            Assert.Equal(0, process.ExitCode);
        }

        SignFile(withVelopack);

        var theirs = ParseSignature(ReadEmbeddedSignature(withSigntool));
        var ours = ParseSignature(ReadEmbeddedSignature(withVelopack));
        Assert.Equal(Convert.ToHexString(theirs.SpcIndirectData), Convert.ToHexString(ours.SpcIndirectData));
        Assert.Equal(theirs.SignedAttributes.Keys.OrderBy(x => x), ours.SignedAttributes.Keys.OrderBy(x => x));
        Assert.Equal(
            Convert.ToHexString(theirs.SignedAttributes[AuthenticodeAsn.SpcSpOpusInfoOid]),
            Convert.ToHexString(ours.SignedAttributes[AuthenticodeAsn.SpcSpOpusInfoOid]));
        Assert.Equal(
            Convert.ToHexString(theirs.SignedAttributes[AuthenticodeAsn.SpcStatementTypeOid]),
            Convert.ToHexString(ours.SignedAttributes[AuthenticodeAsn.SpcStatementTypeOid]));
        Assert.Equal(theirs.Version, ours.Version);

        var theirLayout = ReadLayout(withSigntool);
        var ourLayout = ReadLayout(withVelopack);
        Assert.Equal(theirLayout.CertificateTableOffset, ourLayout.CertificateTableOffset);
        var theirBytes = File.ReadAllBytes(withSigntool);
        var ourBytes = File.ReadAllBytes(withVelopack);
        int checksumOffset = (int) ourLayout.CheckSumOffset;
        int directoryOffset = (int) ourLayout.SecurityDirectoryOffset;
        for (int i = 0; i < ourLayout.CertificateTableOffset; i++) {
            bool isChecksum = i >= checksumOffset && i < checksumOffset + 4;
            bool isDirectorySize = i >= directoryOffset + 4 && i < directoryOffset + 8;
            if (!isChecksum && !isDirectorySize && theirBytes[i] != ourBytes[i]) {
                Assert.Fail($"Byte {i} differs between signtool and velopack");
            }
        }

        _output.WriteLine($"signed lengths: signtool {theirBytes.Length}, velopack {ourBytes.Length}");

        // the PE checksum signtool wrote must match ours for its own output
        Assert.Equal(NaiveChecksum(theirBytes), BitConverter.ToUInt32(theirBytes, checksumOffset));
        if (theirBytes.Length == ourBytes.Length) {
            Assert.Equal(theirBytes.AsSpan(checksumOffset, 4).ToArray(), ourBytes.AsSpan(checksumOffset, 4).ToArray());
        }
    }

    private sealed record ParsedSignature(int Version, byte[] SpcIndirectData, Dictionary<string, byte[]> SignedAttributes);

    private static ParsedSignature ParseSignature(byte[] der)
    {
        var contentInfo = new AsnReader(der, AsnEncodingRules.BER).ReadSequence();
        contentInfo.ReadObjectIdentifier();
        var signedData = contentInfo.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadSequence();
        int version = (int) signedData.ReadInteger();
        signedData.ReadSetOf();
        var encapsulated = signedData.ReadSequence();
        encapsulated.ReadObjectIdentifier();
        byte[] spc = encapsulated.ReadSequence(new Asn1Tag(TagClass.ContextSpecific, 0)).ReadEncodedValue().ToArray();
        while (!signedData.PeekTag().HasSameClassAndValue(Asn1Tag.SetOf)) signedData.ReadEncodedValue();
        var signerInfo = signedData.ReadSetOf().ReadSequence();
        signerInfo.ReadInteger();
        signerInfo.ReadEncodedValue();
        signerInfo.ReadEncodedValue();
        var attrs = signerInfo.ReadSetOf(new Asn1Tag(TagClass.ContextSpecific, 0));
        var result = new Dictionary<string, byte[]>();
        while (attrs.HasData) {
            var attr = attrs.ReadSequence();
            string oid = attr.ReadObjectIdentifier();
            result[oid] = attr.ReadSetOf().ReadEncodedValue().ToArray();
        }

        return new ParsedSignature(version, spc, result);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void MsiAzureSigner_InMemoryKey()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var msi = CreateTestMsi(dir, "test.msi");

        // the post-signing check accepts an intact signature whose (test) root Windows does not trust
        using (var signer = new MsiAzureSigner(Certs.CreateKey(), NullLogger.Instance, timestampUrl: null)) {
            signer.SignFile(msi, ProgramName);
        }

        Certs.AssertIntactButUntrusted(msi);

        // a failing remote signer surfaces its own exception, not an HRESULT or a crash
        var msi2 = CreateTestMsi(dir, "test2.msi");
        var failing = new InMemoryDigestSigner(Certs.LeafKey) { Intercept = _ => throw new InvalidOperationException("remote boom") };
        using (var signer = new MsiAzureSigner(Certs.CreateKey(failing), NullLogger.Instance, timestampUrl: null)) {
            var ex = Assert.Throws<UserInfoException>(() => signer.SignFile(msi2, ProgramName));
            Assert.Contains("remote boom", ex.Message);
        }
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task AzureTrustedSigner_SignsMsiAndPe()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var msi = CreateTestMsi(dir, "test.msi");
        var exe = CopyFixture("NotSquirrelAwareApp.exe", dir);
        var progress = new List<int>();

        await new AzureTrustedSigner(NullLogger.Instance).SignFilesAsync(
            [exe, msi],
            new StaticAuthenticodeKeyProvider(Certs.CreateKey()),
            null,
            ProgramName,
            2,
            p => progress.Add(p),
            _ => true,
            key => new MsiAzureSigner(key, NullLogger.Instance, timestampUrl: null));

        Assert.Equal([50, 100], progress);
        AuthenticodeVerifier.VerifyFile(exe);
        Certs.AssertIntactButUntrusted(msi);
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public async Task AzureTrustedSigner_Msi_RetriesOnceWithRefreshedKeyWhenCertificateRotates()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "Windows only");
        using var tempDir = TempUtil.GetTempDirectory(out var dir);
        var msi = CreateTestMsi(dir, "test.msi");
        var staleSigner = new InMemoryDigestSigner(Certs.LeafKey) {
            Intercept = _ => throw new AzureSigningCertificateChangedException("stale"),
        };
        var provider = new RotatingKeyProvider(Certs.CreateKey(staleSigner), Certs.CreateKey());
        var created = new List<AuthenticodeSigningKey>();

        await new AzureTrustedSigner(NullLogger.Instance).SignFilesAsync(
            [msi],
            provider,
            null,
            ProgramName,
            1,
            _ => { },
            _ => true,
            key => {
                created.Add(key);
                return new MsiAzureSigner(key, NullLogger.Instance, timestampUrl: null);
            });

        Assert.Equal(1, provider.Refreshes);
        Assert.Equal(2, created.Count);
        Assert.NotSame(created[0], created[1]);
        Certs.AssertIntactButUntrusted(msi);
    }

    /// <summary>Builds a tiny MSI with the vendored WiX (MSIs authored directly with msi.dll do not verify after signing).</summary>
    [SupportedOSPlatform("windows")]
    private static string CreateTestMsi(string dir, string name)
    {
        var wxs = Path.Combine(dir, Path.ChangeExtension(name, ".wxs"));
        File.WriteAllText(
            wxs,
            """
            <Wix xmlns="http://wixtoolset.org/schemas/v4/wxs">
              <Package Name="VelopackSignTest" Manufacturer="Velopack" Version="1.0.0" UpgradeCode="2E6E0D0B-7F0E-4E0A-9C1B-5A4D2E7B1C11"
                       Scope="perUser" Compressed="yes">
                <MediaTemplate EmbedCab="yes" />
                <StandardDirectory Id="LocalAppDataFolder">
                  <Directory Id="INSTALLFOLDER" Name="VelopackSignTest">
                    <Component Id="C1" Guid="6C1B7E44-2F4C-4F43-9B4E-6A2B0E8E2A71">
                      <RegistryValue Root="HKCU" Key="Software\VelopackSignTest" Name="x" Value="1" Type="string" KeyPath="yes" />
                      <RemoveFolder Id="RmInstall" On="uninstall" />
                    </Component>
                  </Directory>
                </StandardDirectory>
                <Feature Id="Main"><ComponentRef Id="C1" /></Feature>
              </Package>
            </Wix>
            """);

        var path = Path.Combine(dir, name);
        var psi = new System.Diagnostics.ProcessStartInfo(HelperFile.WixPath) {
            ArgumentList = { "build", "-arch", "x64", "-outputType", "Package", "-pdbType", "none", "-out", path, wxs },
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = System.Diagnostics.Process.Start(psi)!;
        string output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
        process.WaitForExit();
        Assert.True(process.ExitCode == 0 && File.Exists(path), "WiX failed: " + output);
        return path;
    }
}
