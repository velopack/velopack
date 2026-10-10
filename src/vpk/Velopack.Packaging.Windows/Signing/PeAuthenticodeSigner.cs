#nullable enable
using Microsoft.Extensions.Logging;
using Velopack.Core;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// Authenticode signs PE images (.exe / .dll / .node) in place, on any OS.
/// </summary>
public sealed class PeAuthenticodeSigner
{
    private readonly ILogger _log;

    public PeAuthenticodeSigner(ILogger log)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
    }

    /// <summary>
    /// Signs <paramref name="path"/>, replacing any existing signature. All remote work (signing, timestamping) is
    /// done before the file is modified, so a failure there leaves the file untouched.
    /// </summary>
    public void SignFile(string path, AuthenticodeSigningKey key, Rfc3161Timestamper? timestamper, string? programName)
    {
        using var stream = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read, 1 << 16);
        var layout = PeAuthenticodeLayout.Read(stream, path);
        if (layout.HasCertificateTable) {
            _log.Debug($"Replacing existing signature on '{path}'.");
        }

        byte[] digest = PeAuthenticode.ComputeDigest(stream, layout, AuthenticodeCmsBuilder.FileDigestAlgorithm);
        byte[] signature = new AuthenticodeCmsBuilder(key, timestamper).CreateSignature(digest, programName);
        PeAuthenticode.EmbedSignature(stream, layout, signature);
    }
}
