#nullable enable
using System.Formats.Asn1;
using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using System.Security.Cryptography.X509Certificates;

namespace Velopack.Packaging.Tests.Signing;

/// <summary>
/// An in-process RFC 3161 timestamp authority, served through an <see cref="HttpMessageHandler"/>.
/// </summary>
public sealed class FakeTimestampAuthority(X509Certificate2 tsaCertificate) : HttpMessageHandler
{
    private const string TstInfoOid = "1.2.840.113549.1.9.16.1.4";
    private const string SigningCertificateV2Oid = "1.2.840.113549.1.9.16.2.47";

    private int _requestCount;

    /// <summary>The number of requests that are answered with HTTP 503 before succeeding.</summary>
    public int FailFirstRequests { get; set; }

    /// <summary>When set, answers with this PKIStatus (e.g. 2 = rejection) and no token.</summary>
    public int? RejectWithStatus { get; set; }

    public List<Rfc3161TimestampRequest> Requests { get; } = [];

    public int RequestCount => _requestCount;

    public static Uri Url { get; } = new("http://fake-tsa.velopack.test/");

    protected override HttpResponseMessage Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        int count = Interlocked.Increment(ref _requestCount);
        if (count <= FailFirstRequests) {
            return new HttpResponseMessage(HttpStatusCode.ServiceUnavailable) { Content = new StringContent("try again later") };
        }

        if (request.Content?.Headers.ContentType?.MediaType != "application/timestamp-query") {
            return new HttpResponseMessage(HttpStatusCode.UnsupportedMediaType);
        }

        byte[] body = request.Content.ReadAsByteArrayAsync(cancellationToken).GetAwaiter().GetResult();
        if (!Rfc3161TimestampRequest.TryDecode(body, out var tsq, out _)) {
            return new HttpResponseMessage(HttpStatusCode.BadRequest);
        }

        lock (Requests) {
            Requests.Add(tsq);
        }

        var writer = new AsnWriter(AsnEncodingRules.DER);
        using (writer.PushSequence()) {
            using (writer.PushSequence()) {
                writer.WriteInteger(RejectWithStatus ?? 0);
            }

            if (RejectWithStatus == null) {
                writer.WriteEncodedValue(CreateToken(tsq));
            }
        }

        var content = new ByteArrayContent(writer.Encode());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-reply");
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = content };
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return Task.FromResult(Send(request, cancellationToken));
    }

    /// <summary>Creates a TimeStampToken (a SignedData ContentInfo) answering <paramref name="tsq"/>.</summary>
    /// <param name="tsq">The request.</param>
    /// <param name="includeSigningCertificate">
    /// Include the signing-certificate-v2 attribute; without it the token is still verifiable, but
    /// <see cref="Rfc3161TimestampToken.TryDecode"/> rejects it.
    /// </param>
    public byte[] CreateToken(Rfc3161TimestampRequest tsq, bool includeSigningCertificate = true)
    {
        // a positive, minimally encoded INTEGER: a random leading 0x00/0xFF byte would be a redundant sign byte and fail to encode
        var serialNumber = RandomNumberGenerator.GetBytes(8);
        serialNumber[0] = (byte) ((serialNumber[0] & 0x7F) | 0x01);
        var tstInfo = new Rfc3161TimestampTokenInfo(
            new Oid("1.2.3.4.1"),
            tsq.HashAlgorithmId,
            tsq.GetMessageHash(),
            serialNumber,
            DateTimeOffset.UtcNow,
            nonce: tsq.GetNonce());

        var cms = new SignedCms(new ContentInfo(new Oid(TstInfoOid), tstInfo.Encode()), detached: false);
        var signer = new CmsSigner(SubjectIdentifierType.IssuerAndSerialNumber, tsaCertificate) {
            DigestAlgorithm = new Oid("2.16.840.1.101.3.4.2.1"),
            IncludeOption = tsq.RequestSignerCertificate ? X509IncludeOption.EndCertOnly : X509IncludeOption.None,
        };

        // SigningCertificateV2 ::= SEQUENCE { certs SEQUENCE OF ESSCertIDv2 { certHash OCTET STRING } }
        var attr = new AsnWriter(AsnEncodingRules.DER);
        using (attr.PushSequence())
        using (attr.PushSequence())
        using (attr.PushSequence()) {
            attr.WriteOctetString(SHA256.HashData(tsaCertificate.RawData));
        }

        if (includeSigningCertificate) {
            signer.SignedAttributes.Add(new AsnEncodedData(SigningCertificateV2Oid, attr.Encode()));
        }

        cms.ComputeSignature(signer, silent: true);
        return cms.Encode();
    }
}
