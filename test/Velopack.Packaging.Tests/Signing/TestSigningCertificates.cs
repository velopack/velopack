#nullable enable
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Velopack.Packaging.Windows.Signing;

namespace Velopack.Packaging.Tests.Signing;

/// <summary>
/// An in-memory root -> intermediate -> leaf code signing chain (plus a timestamping certificate), standing in for
/// an Azure Trusted Signing certificate profile.
/// </summary>
public sealed class TestSigningCertificates : IDisposable
{
    private const string CodeSigningEku = "1.3.6.1.5.5.7.3.3";
    private const string TimeStampingEku = "1.3.6.1.5.5.7.3.8";

    private static readonly Lazy<TestSigningCertificates> SharedLazy = new(() => new TestSigningCertificates());

    /// <summary>A process wide instance (RSA key generation is slow).</summary>
    public static TestSigningCertificates Shared => SharedLazy.Value;

    public X509Certificate2 Root { get; }
    public X509Certificate2 Intermediate { get; }

    /// <summary>The leaf certificate, WITHOUT a private key (like the Azure certificate chain).</summary>
    public X509Certificate2 Leaf { get; }

    /// <summary>The leaf certificate with its private key.</summary>
    public X509Certificate2 LeafWithKey { get; }

    public RSA LeafKey { get; }

    /// <summary>A timestamping certificate (with private key) issued by <see cref="Root"/>.</summary>
    public X509Certificate2 TimestampAuthority { get; }

    public TestSigningCertificates()
    {
        var notBefore = DateTimeOffset.UtcNow.AddDays(-1);
        var notAfter = DateTimeOffset.UtcNow.AddYears(1);

        using var rootKey = RSA.Create(3072);
        var rootRequest = NewRequest("CN=Velopack Test Root, O=Velopack Tests", rootKey);
        rootRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        rootRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        rootRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(rootRequest.PublicKey, false));
        Root = rootRequest.CreateSelfSigned(notBefore, notAfter);

        using var intermediateKey = RSA.Create(3072);
        var intermediateRequest = NewRequest("CN=Velopack Test Intermediate, O=Velopack Tests", intermediateKey);
        intermediateRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(true, false, 0, true));
        intermediateRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.KeyCertSign | X509KeyUsageFlags.CrlSign, true));
        intermediateRequest.CertificateExtensions.Add(new X509SubjectKeyIdentifierExtension(intermediateRequest.PublicKey, false));
        intermediateRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Root, true, false));
        using (var issued = intermediateRequest.Create(Root, notBefore, notAfter, RandomSerial())) {
            Intermediate = issued.CopyWithPrivateKey(intermediateKey);
        }

        LeafKey = RSA.Create(3072);
        var leafRequest = NewRequest("CN=Velopack Test Signer, O=Velopack Tests", LeafKey);
        leafRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        leafRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        leafRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(CodeSigningEku)], false));
        leafRequest.CertificateExtensions.Add(X509AuthorityKeyIdentifierExtension.CreateFromCertificate(Intermediate, true, false));
        Leaf = leafRequest.Create(Intermediate, notBefore, notAfter.AddDays(-1), RandomSerial());
        LeafWithKey = Leaf.CopyWithPrivateKey(LeafKey);

        using var tsaKey = RSA.Create(2048);
        var tsaRequest = NewRequest("CN=Velopack Test TSA, O=Velopack Tests", tsaKey);
        tsaRequest.CertificateExtensions.Add(new X509BasicConstraintsExtension(false, false, 0, true));
        tsaRequest.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        tsaRequest.CertificateExtensions.Add(new X509EnhancedKeyUsageExtension([new Oid(TimeStampingEku)], true));
        using (var issued = tsaRequest.Create(Root, notBefore, notAfter.AddDays(-1), RandomSerial())) {
            TimestampAuthority = issued.CopyWithPrivateKey(tsaKey);
        }
    }

    private static CertificateRequest NewRequest(string subject, RSA key)
    {
        return new CertificateRequest(subject, key, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
    }

    /// <summary>Creates a key exactly like production: a <see cref="RemoteRsa"/> over a digest signer.</summary>
    public AuthenticodeSigningKey CreateKey(IRemoteDigestSigner? signer = null)
    {
        var rsa = new RemoteRsa(signer ?? new InMemoryDigestSigner(LeafKey), Leaf.GetRSAPublicKey()!);
        return new AuthenticodeSigningKey(rsa, Leaf, [Intermediate]);
    }

    /// <summary>The chain as Azure returns it: a PKCS#7 certs-only blob, in the given order.</summary>
    public byte[] ExportChainPkcs7(params X509Certificate2[] certificates)
    {
        var collection = new X509Certificate2Collection();
        foreach (var c in certificates) {
            collection.Add(X509CertificateLoader.LoadCertificate(c.RawData));
        }

        return collection.Export(X509ContentType.Pkcs7)!;
    }

    private static byte[] RandomSerial()
    {
        var serial = RandomNumberGenerator.GetBytes(16);
        serial[0] &= 0x7F;
        serial[0] |= 0x01;
        return serial;
    }

    public void Dispose()
    {
        Root.Dispose();
        Intermediate.Dispose();
        Leaf.Dispose();
        LeafWithKey.Dispose();
        LeafKey.Dispose();
        TimestampAuthority.Dispose();
    }
}

/// <summary>An <see cref="IRemoteDigestSigner"/> backed by a local RSA key, tracking calls and concurrency.</summary>
public sealed class InMemoryDigestSigner(RSA key, TimeSpan? delay = null) : IRemoteDigestSigner
{
    private int _current;
    private int _maxConcurrency;
    private int _calls;

    public int MaxConcurrency => _maxConcurrency;
    public int Calls => _calls;

    /// <summary>Optional hook to throw from (or alter) a signing call.</summary>
    public Func<byte[], byte[]>? Intercept { get; set; }

    /// <summary>
    /// When set, the first N calls block (up to 30s) until N calls are in flight at once, so a test can prove that N
    /// concurrent signs happen without depending on thread pool timing.
    /// </summary>
    public int WaitForConcurrentCalls { get; init; }

    public byte[] SignDigest(byte[] digest, HashAlgorithmName hashAlgorithm)
    {
        int call = Interlocked.Increment(ref _calls);
        int now = Interlocked.Increment(ref _current);
        int seen;
        while (now > (seen = Volatile.Read(ref _maxConcurrency)) && Interlocked.CompareExchange(ref _maxConcurrency, now, seen) != seen) {
        }

        try {
            if (call <= WaitForConcurrentCalls) {
                SpinWait.SpinUntil(() => Volatile.Read(ref _maxConcurrency) >= WaitForConcurrentCalls, TimeSpan.FromSeconds(30));
            }

            if (delay is { } d) Thread.Sleep(d);
            byte[] signature = key.SignHash(digest, hashAlgorithm, RSASignaturePadding.Pkcs1);
            return Intercept?.Invoke(signature) ?? signature;
        } finally {
            Interlocked.Decrement(ref _current);
        }
    }
}
