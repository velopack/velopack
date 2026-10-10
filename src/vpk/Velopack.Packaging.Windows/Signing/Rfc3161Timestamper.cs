#nullable enable
using System.Formats.Asn1;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Security.Cryptography.Pkcs;
using Microsoft.Extensions.Logging;
using Velopack.Core;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>Thrown when every attempt to get an RFC 3161 timestamp failed.</summary>
public sealed class TimestampException(string message, bool isConnectivityFailure, Exception innerException)
    : UserInfoException(message, innerException)
{
    /// <summary>
    /// True when every attempt failed to get an answer from the timestamp authority (network error, timeout, HTTP
    /// 408/429/5xx), as opposed to the authority rejecting the request or returning an invalid response.
    /// </summary>
    public bool IsConnectivityFailure { get; } = isConnectivityFailure;
}

/// <summary>
/// A minimal RFC 3161 timestamp client used to counter-sign Authenticode signatures.
/// </summary>
public sealed class Rfc3161Timestamper
{
    /// <summary>The timestamp authority Microsoft recommends for Azure Trusted Signing.</summary>
    public const string AzureTimestampUrl = "http://timestamp.acs.microsoft.com";

    private static readonly TimeSpan[] DefaultRetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];

    private static readonly Lazy<HttpClient> SharedHttpClientLazy = new(() => new HttpClient { Timeout = TimeSpan.FromSeconds(30) });

    /// <summary>A process wide HttpClient for timestamp requests (30 second timeout).</summary>
    public static HttpClient SharedHttpClient => SharedHttpClientLazy.Value;

    private readonly HttpClient _httpClient;
    private readonly IReadOnlyList<TimeSpan> _retryDelays;
    private readonly ILogger? _log;

    /// <summary>The timestamp authority URL.</summary>
    public Uri Url { get; }

    /// <param name="httpClient">Client used to send requests. Must support synchronous sends.</param>
    /// <param name="url">The timestamp authority.</param>
    /// <param name="log">Optional logger for retry messages.</param>
    /// <param name="retryDelays">Delays between attempts; the number of attempts is one more than the number of delays.</param>
    public Rfc3161Timestamper(HttpClient httpClient, Uri url, ILogger? log = null, IReadOnlyList<TimeSpan>? retryDelays = null)
    {
        _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        Url = url ?? throw new ArgumentNullException(nameof(url));
        _log = log;
        _retryDelays = retryDelays ?? DefaultRetryDelays;
    }

    /// <summary>
    /// Requests an RFC 3161 timestamp over the signature value of <paramref name="signerInfo"/>, and returns the DER
    /// encoded TimeStampToken (a ContentInfo), ready to be added as an unsigned attribute.
    /// </summary>
    public byte[] RequestToken(SignerInfo signerInfo)
    {
        var errors = new List<Exception>();
        int attempts = _retryDelays.Count + 1;
        for (int attempt = 1; attempt <= attempts; attempt++) {
            try {
                return RequestTokenOnce(signerInfo);
            } catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or CryptographicException
                                             or TimestampFailedException) {
                errors.Add(ex);
                if (attempt < attempts) {
                    var delay = _retryDelays[attempt - 1];
                    _log?.LogWarning($"Timestamp request to {Url} failed ({ex.Message}), retrying in {delay.TotalSeconds:0.#}s...");
                    Thread.Sleep(delay);
                }
            }
        }

        bool connectivity = errors.All(IsConnectivityFailure);
        throw new TimestampException(
            $"Timestamping failed against {Url} after {attempts} attempts: {String.Join("; ", errors.Select(e => e.Message).Distinct())}",
            connectivity,
            new AggregateException(errors));
    }

    private static bool IsConnectivityFailure(Exception ex)
    {
        return ex is HttpRequestException or TaskCanceledException
            || ex is TimestampFailedException { StatusCode: 408 or 429 or >= 500 };
    }

    private byte[] RequestTokenOnce(SignerInfo signerInfo)
    {
        byte[] nonce = RandomNumberGenerator.GetBytes(8);
        nonce[0] = (byte) (1 + nonce[0] % 0x7F); // positive, and no leading zero for the TSA to normalize away

        var request = Rfc3161TimestampRequest.CreateFromSignerInfo(
            signerInfo,
            HashAlgorithmName.SHA256,
            requestedPolicyId: null,
            nonce: nonce,
            requestSignerCertificates: true);

        using var content = new ByteArrayContent(request.Encode());
        content.Headers.ContentType = new MediaTypeHeaderValue("application/timestamp-query");
        using var message = new HttpRequestMessage(HttpMethod.Post, Url) { Content = content };
        message.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/timestamp-reply"));

        using var response = _httpClient.Send(message);
        byte[] body;
        using (var bodyStream = response.Content.ReadAsStream())
        using (var ms = new MemoryStream()) {
            bodyStream.CopyTo(ms);
            body = ms.ToArray();
        }

        if (!response.IsSuccessStatusCode) {
            string text = System.Text.Encoding.UTF8.GetString(body, 0, Math.Min(body.Length, 200));
            throw new TimestampFailedException(
                (int) response.StatusCode,
                $"HTTP {(int) response.StatusCode} {response.ReasonPhrase} {text}".Trim());
        }

        // validates the status, message imprint, nonce and signer certificate of the response
        Rfc3161TimestampToken token = request.ProcessResponse(body, out _);

        // Embed the token exactly as the TSA sent it. TimeStampResp ::= SEQUENCE { status, timeStampToken }
        var timeStampResp = new AsnReader(body, AsnEncodingRules.BER).ReadSequence();
        timeStampResp.ReadEncodedValue();
        byte[] rawToken = timeStampResp.ReadEncodedValue().ToArray();
        if (AsnDecoder.TryReadEncodedValue(rawToken, AsnEncodingRules.DER, out _, out _, out _, out int consumed)
            && consumed == rawToken.Length) {
            return rawToken;
        }

        // the signature must be DER; a BER token is re-encoded (its signed content is preserved byte for byte)
        return token.AsSignedCms().Encode();
    }

    private sealed class TimestampFailedException(int statusCode, string message) : Exception(message)
    {
        public int StatusCode { get; } = statusCode;
    }
}
