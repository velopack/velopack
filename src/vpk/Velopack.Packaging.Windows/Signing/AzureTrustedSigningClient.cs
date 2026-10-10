#nullable enable
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;
using Azure;
using Azure.CodeSigning;
using Azure.CodeSigning.Models;
using Azure.Core;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Velopack.Core;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// Thrown when Azure Trusted Signing signed with a different certificate than the cached one (the profile's
/// short-lived certificate was rotated while signing).
/// </summary>
public sealed class AzureSigningCertificateChangedException(string staleThumbprint)
    : Exception($"The Azure Trusted Signing certificate changed while signing (cached certificate {staleThumbprint}).")
{
    public string StaleThumbprint { get; } = staleThumbprint;
}

/// <summary>The signing certificate and intermediates of an Azure Trusted Signing certificate profile.</summary>
public sealed record AzureCertificateChain(X509Certificate2 Leaf, X509Certificate2Collection Intermediates);

/// <summary>
/// Signs digests with an Azure Trusted Signing (Artifact Signing) certificate profile, following the pattern of
/// dotnet/sign's ArtifactSigningService: the certificate chain is fetched once and cached, and each signature is a
/// StartSign + WaitForCompletion round trip.
/// </summary>
public sealed class AzureTrustedSigningClient : IRemoteDigestSigner
{
    private static readonly TimeSpan[] DefaultRetryDelays = [TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(5)];
    private static readonly TimeSpan DefaultOperationTimeout = TimeSpan.FromMinutes(2);

    private readonly CertificateProfileClient _client;
    private readonly AzureTrustedSigningMetadata _metadata;
    private readonly ILogger _log;
    private readonly object _chainLock = new();
    private AzureCertificateChain? _chain;

    /// <summary>Delays between sign attempts on transient failures (one more attempt than delays).</summary>
    public IReadOnlyList<TimeSpan> RetryDelays { get; init; } = DefaultRetryDelays;

    /// <summary>Upper bound for a single StartSign + WaitForCompletion; the SDK would otherwise poll forever on some statuses.</summary>
    public TimeSpan OperationTimeout { get; init; } = DefaultOperationTimeout;

    public AzureTrustedSigningClient(AzureTrustedSigningMetadata metadata, TokenCredential credential, ILogger log)
        : this(metadata, CreateClient(metadata, credential), log)
    {
    }

    /// <summary>Creates a client over an existing (or mocked) <see cref="CertificateProfileClient"/>.</summary>
    public AzureTrustedSigningClient(AzureTrustedSigningMetadata metadata, CertificateProfileClient client, ILogger log)
    {
        _metadata = metadata ?? throw new ArgumentNullException(nameof(metadata));
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>Creates the Azure SDK client for the metadata endpoint.</summary>
    public static CertificateProfileClient CreateClient(AzureTrustedSigningMetadata metadata, TokenCredential credential)
    {
        var options = new CertificateProfileClientOptions();
        // parallel signing can be throttled (429); back off exponentially like dotnet/sign
        options.Retry.Mode = RetryMode.Exponential;
        options.Retry.MaxRetries = 5;
        options.Diagnostics.IsLoggingEnabled = false;
        options.Diagnostics.IsLoggingContentEnabled = false;
        options.Diagnostics.IsTelemetryEnabled = false;
        return new CertificateProfileClient(credential, metadata.EndpointUri, options);
    }

    /// <summary>Returns the certificate chain of the profile, fetching it on first use.</summary>
    public AzureCertificateChain GetCertificateChain()
    {
        lock (_chainLock) {
            return _chain ??= FetchCertificateChain();
        }
    }

    /// <summary>
    /// Re-fetches the certificate chain if the cached leaf still has <paramref name="staleThumbprint"/> (so concurrent
    /// callers that saw the same stale certificate only refresh once), and returns the current chain.
    /// </summary>
    public AzureCertificateChain RefreshCertificateChain(string staleThumbprint)
    {
        lock (_chainLock) {
            if (_chain == null || String.Equals(_chain.Leaf.Thumbprint, staleThumbprint, StringComparison.OrdinalIgnoreCase)) {
                _log.Info("Azure Trusted Signing certificate was rotated, fetching the new certificate chain...");
                _chain = FetchCertificateChain();
            }

            return _chain;
        }
    }

    private AzureCertificateChain FetchCertificateChain()
    {
        byte[] data;
        try {
            using var response = _client.GetSignCertificateChain(_metadata.CodeSigningAccountName, _metadata.CertificateProfileName).Value;
            using var ms = new MemoryStream();
            response.CopyTo(ms);
            data = ms.ToArray();
        } catch (Exception ex) when (TranslateException(ex, null) is { } translated) {
            throw translated;
        }

        var (leaf, intermediates) = CertificateChainParser.SplitChain(CertificateChainParser.Parse(data));
        return new AzureCertificateChain(leaf, intermediates);
    }

    /// <inheritdoc />
    public byte[] SignDigest(byte[] digest, HashAlgorithmName hashAlgorithm)
    {
        var algorithm = GetSignatureAlgorithm(digest, hashAlgorithm);
        var chain = GetCertificateChain();
        var errors = new List<string>();
        int attempts = RetryDelays.Count + 1;

        for (int attempt = 1; attempt <= attempts; attempt++) {
            string? operationId = null;
            try {
                using var cts = new CancellationTokenSource(OperationTimeout);
                var operation = _client.StartSign(
                    _metadata.CodeSigningAccountName,
                    _metadata.CertificateProfileName,
                    new SignRequest(algorithm, digest),
                    xCorrelationId: _metadata.CorrelationId,
                    clientVersion: $"Velopack/{VelopackRuntimeInfo.VelopackNugetVersion.ToNormalizedString()}",
                    certificateThumbprint: null,
                    cancellationToken: cts.Token);
                // note: Operation.Id is not implemented by the SDK (it throws), the id comes from the status response body
                operationId = TryGetOperationId(operation);

                SignStatus status;
                try {
                    status = operation.WaitForCompletion(TimeSpan.FromMilliseconds(500), cts.Token).Value;
                } catch (OperationCanceledException) when (cts.IsCancellationRequested) {
                    operationId = TryGetOperationId(operation) ?? operationId;
                    string id = operationId == null ? "" : $" {operationId}";
                    throw new TransientSignException($"operation{id} did not complete within {OperationTimeout.TotalSeconds:0}s");
                } catch (RequestFailedException rfe) when (rfe.Status is >= 200 and < 300) {
                    // the SDK throws (built from the 200 status poll) when the operation ends as Failed / Canceled. This is
                    // a decision by the service about this request, so like the Dlib and dotnet/sign it is not retried.
                    operationId = TryGetOperationId(operation) ?? operationId;
                    string id = operationId == null ? "" : $" {operationId}";
                    string code = String.IsNullOrEmpty(rfe.ErrorCode) ? "" : $" ({rfe.ErrorCode})";
                    throw new UserInfoException($"Azure Trusted Signing operation{id} failed{code}: {rfe.Message}", rfe);
                }

                operationId = status.OperationId.ToString();
                if (status.Status != Status.Succeeded || status.Signature == null || status.Signature.Length == 0) {
                    throw new TransientSignException($"operation {operationId} finished with status '{status.Status}'");
                }

                CheckSigningCertificate(chain, status.SigningCertificate);
                return status.Signature;
            } catch (Exception ex) when (ex is TransientSignException or OperationCanceledException || IsTransient(ex)) {
                string message = ex is RequestFailedException rfe ? $"HTTP {rfe.Status} {rfe.ErrorCode} {rfe.Message}".Trim() : ex.Message;
                errors.Add(message);
                if (attempt < attempts) {
                    var delay = RetryDelays[attempt - 1];
                    _log.Warn($"Azure Trusted Signing request failed ({message}), retrying in {delay.TotalSeconds:0.#}s...");
                    Thread.Sleep(delay);
                }
            } catch (Exception ex) when (TranslateException(ex, operationId) is { } translated) {
                throw translated;
            }
        }

        throw new UserInfoException($"Azure Trusted Signing failed after {attempts} attempts: {String.Join("; ", errors.Distinct())}");
    }

    private void CheckSigningCertificate(AzureCertificateChain chain, byte[]? signingCertificate)
    {
        if (signingCertificate == null || signingCertificate.Length == 0) return;

        X509Certificate2 used;
        try {
            var certificates = CertificateChainParser.Parse(signingCertificate);
            used = certificates.Count == 1 ? certificates[0] : CertificateChainParser.SplitChain(certificates).Leaf;
        } catch (Exception ex) {
            // CmsSigner verifies the signature against the cached certificate anyway, this is only an early warning
            _log.Debug($"Unable to parse the signing certificate returned by Azure Trusted Signing: {ex.Message}");
            return;
        }

        if (!used.PublicKey.EncodedKeyValue.RawData.AsSpan().SequenceEqual(chain.Leaf.PublicKey.EncodedKeyValue.RawData)) {
            throw new AzureSigningCertificateChangedException(chain.Leaf.Thumbprint);
        }
    }

    private static SignatureAlgorithm GetSignatureAlgorithm(byte[] digest, HashAlgorithmName hashAlgorithm)
    {
        (SignatureAlgorithm algorithm, int length) = hashAlgorithm.Name switch {
            "SHA256" => (SignatureAlgorithm.RS256, 32),
            "SHA384" => (SignatureAlgorithm.RS384, 48),
            "SHA512" => (SignatureAlgorithm.RS512, 64),
            _ => throw new NotSupportedException($"Azure Trusted Signing does not support the '{hashAlgorithm.Name}' hash algorithm."),
        };

        if (digest.Length != length) {
            throw new CryptographicException($"A {hashAlgorithm.Name} digest must be {length} bytes, but was {digest.Length} bytes.");
        }

        return algorithm;
    }

    /// <summary>
    /// Reads the operation id from the latest response body (a SignStatus JSON object). Returns null if it is not
    /// available; the id is only used to make error messages easier to report.
    /// </summary>
    private static string? TryGetOperationId(Operation operation)
    {
        try {
            using var json = JsonDocument.Parse(operation.GetRawResponse().Content);
            if (json.RootElement.ValueKind == JsonValueKind.Object && json.RootElement.TryGetProperty("operationId", out var id)) {
                return id.ValueKind == JsonValueKind.String ? id.GetString() : null;
            }
        } catch {
            // best effort
        }

        return null;
    }

    private static bool IsTransient(Exception ex)
    {
        return ex is RequestFailedException { Status: 0 or 408 or 429 or 500 or 502 or 503 or 504 };
    }

    private static Exception? TranslateException(Exception ex, string? operationId)
    {
        string operation = operationId == null ? "" : $" (operation {operationId})";
        switch (ex) {
        case AuthenticationFailedException or CredentialUnavailableException:
            return new UserInfoException(
                $"Failed to authenticate to Azure Trusted Signing: {ex.Message} Sign in (e.g. 'az login') or set " +
                "AccessToken / ExcludeCredentials in the metadata file.",
                ex);
        case RequestFailedException { Status: 401 or 403 } rfe:
            return new UserInfoException(
                $"Azure Trusted Signing denied the request (HTTP {rfe.Status} {rfe.ErrorCode}){operation}. Make sure the signing identity " +
                "has the 'Trusted Signing Certificate Profile Signer' role on the certificate profile.",
                ex);
        case RequestFailedException rfe:
            return new UserInfoException(
                $"Azure Trusted Signing request failed (HTTP {rfe.Status} {rfe.ErrorCode}){operation}: {rfe.Message}",
                ex);
        default:
            return null;
        }
    }

    private sealed class TransientSignException(string message) : Exception(message);
}
