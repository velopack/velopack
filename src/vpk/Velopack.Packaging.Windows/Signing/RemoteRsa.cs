#nullable enable
// Pattern adapted from dotnet/sign's RSAArtifactSigning
// (https://github.com/dotnet/sign, MIT License, Copyright (c) .NET Foundation and Contributors).

using System.Security.Cryptography;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// Thrown when a remote signature does not verify with the public key of the signing certificate: the remote key
/// is not the one the certificate was issued for (usually because the remote certificate was rotated).
/// </summary>
public sealed class SigningKeyMismatchException()
    : CryptographicException("The remote signature does not verify with the signing certificate's public key.");

/// <summary>
/// An <see cref="RSA"/> whose private key operations are delegated to an <see cref="IRemoteDigestSigner"/>, so that
/// the regular .NET signing APIs (SignedCms, AzureSign.Core) can be used with a key held by a remote service.
/// </summary>
public sealed class RemoteRsa : RSA
{
    private readonly IRemoteDigestSigner _signer;
    private readonly RSA _publicKey;

    /// <param name="signer">Performs the actual signing operation.</param>
    /// <param name="publicKey">The public key of the signing certificate. Owned (and disposed) by this instance.</param>
    public RemoteRsa(IRemoteDigestSigner signer, RSA publicKey)
    {
        _signer = signer ?? throw new ArgumentNullException(nameof(signer));
        _publicKey = publicKey ?? throw new ArgumentNullException(nameof(publicKey));
        // SignedCms sizes the signature buffer from KeySize, so this must match the real key.
        KeySizeValue = publicKey.KeySize;
        LegalKeySizesValue = [new KeySizes(publicKey.KeySize, publicKey.KeySize, 0)];
    }

    public override byte[] SignHash(byte[] hash, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        if (padding != RSASignaturePadding.Pkcs1) {
            throw new CryptographicException("Only PKCS#1 v1.5 signature padding is supported by the remote signer.");
        }

        byte[] signature = _signer.SignDigest(hash, hashAlgorithm);
        int expectedLength = (KeySize + 7) / 8;
        if (signature.Length > expectedLength) {
            throw new CryptographicException(
                $"The remote signer returned a {signature.Length} byte signature, but the key is only {expectedLength} bytes.");
        }

        if (signature.Length < expectedLength) {
            // a big-endian integer can legitimately be shorter than the modulus; restore the leading zeros.
            var padded = new byte[expectedLength];
            signature.CopyTo(padded, expectedLength - signature.Length);
            signature = padded;
        }

        // checked here (SignedCms checks it too, but with an untyped exception) so callers can tell a rotated remote
        // certificate apart from other failures, and refresh the certificate only for this one
        if (!_publicKey.VerifyHash(hash, signature, hashAlgorithm, padding)) {
            throw new SigningKeyMismatchException();
        }

        return signature;
    }

    public override bool VerifyHash(byte[] hash, byte[] signature, HashAlgorithmName hashAlgorithm, RSASignaturePadding padding)
    {
        return _publicKey.VerifyHash(hash, signature, hashAlgorithm, padding);
    }

    public override RSAParameters ExportParameters(bool includePrivateParameters)
    {
        if (includePrivateParameters) {
            throw new CryptographicException("The private key of a remote signing key cannot be exported.");
        }

        return _publicKey.ExportParameters(false);
    }

    public override void ImportParameters(RSAParameters parameters)
    {
        throw new NotSupportedException("A remote signing key cannot import key material.");
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            _publicKey.Dispose();
        }

        base.Dispose(disposing);
    }
}
