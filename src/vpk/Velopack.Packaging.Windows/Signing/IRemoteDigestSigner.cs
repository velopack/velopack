#nullable enable
using System.Security.Cryptography;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// Produces a raw RSA PKCS#1 v1.5 signature over a pre-computed digest, typically by calling a remote signing
/// service (Azure Trusted Signing) where the private key never leaves the service.
/// </summary>
public interface IRemoteDigestSigner
{
    /// <summary>Signs <paramref name="digest"/>, which was computed with <paramref name="hashAlgorithm"/>.</summary>
    byte[] SignDigest(byte[] digest, HashAlgorithmName hashAlgorithm);
}
