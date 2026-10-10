#nullable enable
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// The key and certificates used to produce an Authenticode signature.
/// </summary>
/// <param name="PrivateKey">The signing key; usually a <see cref="RemoteRsa"/> that never exposes key material.</param>
/// <param name="Certificate">The leaf (signing) certificate, matching <paramref name="PrivateKey"/>.</param>
/// <param name="Intermediates">Intermediate CA certificates to embed in the signature (self-signed roots excluded).</param>
public sealed record AuthenticodeSigningKey(RSA PrivateKey, X509Certificate2 Certificate, X509Certificate2Collection Intermediates);
