#nullable enable
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using AzureSign.Core;
using Microsoft.Extensions.Logging;
using Microsoft.Security.Extensions;
using Velopack.Core;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// Signs .msi files through the Windows SIP (mssign32) using AzureSign.Core, with the same remote key as PE files.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class MsiAzureSigner : IMsiSigner
{
    private readonly ILogger _log;
    private readonly CallbackSafeRsa _rsa;
    private readonly AuthenticodeKeyVaultSigner _signer;
    private readonly X509Certificate2 _certificate;
    private readonly bool _timestamped;

    /// <param name="key">The signing key and certificates.</param>
    /// <param name="log">Logger.</param>
    /// <param name="timestampUrl">RFC 3161 timestamp authority, or null to not timestamp.</param>
    public MsiAzureSigner(AuthenticodeSigningKey key, ILogger log, string? timestampUrl = Rfc3161Timestamper.AzureTimestampUrl)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _certificate = key.Certificate;
        _timestamped = timestampUrl != null;
        _rsa = new CallbackSafeRsa(key.PrivateKey);
        var timestamp = timestampUrl == null
            ? TimeStampConfiguration.None
            : new TimeStampConfiguration(timestampUrl, HashAlgorithmName.SHA256, TimeStampType.RFC3161);
        try {
            _signer = new AuthenticodeKeyVaultSigner(_rsa, key.Certificate, HashAlgorithmName.SHA256, timestamp, key.Intermediates);
        } catch (InvalidOperationException ex) {
            _rsa.Dispose();
            throw new UserInfoException($"Unable to build a certificate chain for '{key.Certificate.Subject}' to sign MSI files: {ex.Message}", ex);
        }
    }

    /// <summary>Signs <paramref name="msiPath"/> in place, replacing any existing signature. Not thread-safe.</summary>
    public void SignFile(string msiPath, string? description)
    {
        _rsa.CapturedException = null;
        int hr = _signer.SignFile(msiPath, description ?? "", "", pageHashing: null, logger: _log, appendSignature: false);

        if (_rsa.CapturedException is { } captured) {
            if (captured is UserInfoException) {
                throw captured;
            }

            throw new UserInfoException($"Failed to sign '{msiPath}': {captured.Message}", captured);
        }

        if (hr != 0) {
            throw new UserInfoException($"Failed to sign '{msiPath}' (HRESULT 0x{hr:X8}): {Marshal.GetExceptionForHR(hr)?.Message}");
        }

        VerifySignature(msiPath);
    }

    /// <summary>
    /// Checks the new signature is intact, was made with our certificate, and carries the timestamp if one was requested.
    /// Trust is deliberately not required: Private Trust and test certificate profiles chain to roots Windows does not
    /// trust, and the old signtool path never checked it either.
    /// </summary>
    private void VerifySignature(string msiPath)
    {
        var info = CodeSign.GetSignatureInfo(msiPath);
        var signer = info.SigningCertificate;

        // Microsoft.Security.Extensions reports every untrusted chain as Unsigned: a self-signed root as UntrustedRoot, and
        // a chain that does not reach a root at all (CERT_E_CHAINING; roots are never embedded) as Unknown. A bad or
        // mismatched signature is Unsigned/None or Invalid, and has no SigningCertificate if the signer cannot be found.
        bool intact = info.State == SignatureState.SignedAndTrusted
                      || (info.State == SignatureState.Unsigned
                          && info.StateReason is SignatureStateReason.UntrustedRoot or SignatureStateReason.Unknown);
        if (!intact || signer == null) {
            throw new UserInfoException(
                $"'{msiPath}' was signed, but the signature does not verify ({info.State}, {info.StateReason}).");
        }

        if (info.State != SignatureState.SignedAndTrusted) {
            _log.Debug($"'{msiPath}' is signed, but its certificate chain is not trusted on this machine ({info.StateReason}).");
        }

        if (!signer.RawData.AsSpan().SequenceEqual(_certificate.RawData)) {
            throw new UserInfoException(
                $"'{msiPath}' was signed with '{signer.Subject}' ({signer.Thumbprint}) instead of the expected certificate " +
                $"({_certificate.Thumbprint}).");
        }

        // a corrupt timestamp is also reported as Unsigned/Unknown, but then Windows does not report the timestamp signer
        if (_timestamped && info.TimestampCertificate == null) {
            throw new UserInfoException($"'{msiPath}' was signed, but Windows does not recognize its timestamp.");
        }
    }

    public void Dispose()
    {
        _signer.Dispose();
        _rsa.Dispose();
    }

    /// <summary>
    /// AzureSign.Core calls RSA.SignHash from a native mssign32 callback without a try/catch, so a managed exception
    /// must never escape it. The exception is captured instead, and rethrown once SignFile returns.
    /// </summary>
    private sealed class CallbackSafeRsa : RSA
    {
        private readonly RSA _inner;

        public Exception? CapturedException { get; set; }

        public CallbackSafeRsa(RSA inner)
        {
            _inner = inner;
            KeySizeValue = inner.KeySize;
            LegalKeySizesValue = [new KeySizes(inner.KeySize, inner.KeySize, 0)];
        }

        public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
        {
            try {
                return _inner.SignHash(hash, hashAlgorithm, padding);
            } catch (Exception ex) {
                CapturedException = ex;
                return new byte[(KeySize + 7) / 8];
            }
        }

        public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
            => _inner.VerifyHash(hash, signature, hashAlgorithm, padding);

        public override RSAParameters ExportParameters(bool includePrivateParameters) => _inner.ExportParameters(includePrivateParameters);

        public override void ImportParameters(RSAParameters parameters) => throw new NotSupportedException();
    }
}
