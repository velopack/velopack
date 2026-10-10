#nullable enable
using System.Runtime.Versioning;
using System.Security.Cryptography.X509Certificates;
using Azure.CodeSigning;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>Supplies the signing key, and a fresh one if the remote certificate is rotated while signing.</summary>
public interface IAuthenticodeKeyProvider
{
    /// <summary>The current signing key.</summary>
    AuthenticodeSigningKey GetKey();

    /// <summary>
    /// Called when signing with <paramref name="staleKey"/> failed because the remote certificate no longer matches.
    /// Returns a different key to retry with, or null if there is none.
    /// </summary>
    AuthenticodeSigningKey? RefreshKey(AuthenticodeSigningKey staleKey);
}

/// <summary>A key provider with a single fixed key.</summary>
public sealed class StaticAuthenticodeKeyProvider(AuthenticodeSigningKey key) : IAuthenticodeKeyProvider
{
    public AuthenticodeSigningKey GetKey() => key;

    public AuthenticodeSigningKey? RefreshKey(AuthenticodeSigningKey staleKey) => null;
}

/// <summary>
/// Signs files with Azure Trusted Signing on any OS: PE images with the managed Authenticode signer, and (on Windows
/// only) MSI files with AzureSign.Core. One instance is meant to be reused for a whole pack: the credential, client and
/// certificate chain are created on first use and shared by every later call, so the user authenticates (and the chain
/// is downloaded) only once.
/// </summary>
public sealed class AzureTrustedSigner : IDisposable
{
    private readonly ILogger _log;
    private readonly Func<AzureTrustedSigningMetadata, CertificateProfileClient> _clientFactory;
    private readonly Rfc3161Timestamper? _timestamper;
    private readonly object _sessionLock = new();
    private AzureSession? _session;

    public AzureTrustedSigner(ILogger log)
        : this(
            log,
            m => AzureTrustedSigningClient.CreateClient(m, m.CreateCredential()),
            new Rfc3161Timestamper(Rfc3161Timestamper.SharedHttpClient, new Uri(Rfc3161Timestamper.AzureTimestampUrl), log))
    {
    }

    /// <summary>Creates a signer over a custom (e.g. mocked) Azure client and timestamp authority.</summary>
    /// <param name="log">Logger.</param>
    /// <param name="clientFactory">Creates the Azure client for the metadata file (called once per instance).</param>
    /// <param name="timestamper">Timestamps signatures (MSI files use its URL); null to not timestamp.</param>
    public AzureTrustedSigner(ILogger log, Func<AzureTrustedSigningMetadata, CertificateProfileClient> clientFactory,
        Rfc3161Timestamper? timestamper)
    {
        _log = log ?? throw new ArgumentNullException(nameof(log));
        _clientFactory = clientFactory ?? throw new ArgumentNullException(nameof(clientFactory));
        _timestamper = timestamper;
    }

    /// <summary>
    /// Signs <paramref name="files"/> with the certificate profile described by the metadata.json at
    /// <paramref name="metadataPath"/>, skipping files that are already signed.
    /// </summary>
    public async Task SignFilesAsync(string[] files, string metadataPath, string? programName, int parallelism, Action<int> progress)
    {
        var toSign = FilterFilesToSign(files, null);
        if (toSign.Length == 0) {
            progress(100);
            return;
        }

        var session = GetSession(metadataPath);
        _log.Info($"Signing {toSign.Length} file(s) with Azure Trusted Signing, with a parallelism of {parallelism}.");
        await SignFilesCoreAsync(
                toSign,
                session.KeyProvider,
                _timestamper,
                _timestamper?.Url.ToString(),
                programName,
                parallelism,
                progress)
            .ConfigureAwait(false);
    }

    private AzureSession GetSession(string metadataPath)
    {
        string fullPath = Path.GetFullPath(metadataPath);
        lock (_sessionLock) {
            if (_session != null) {
                if (!String.Equals(_session.MetadataPath, fullPath, StringComparison.OrdinalIgnoreCase)) {
                    throw new InvalidOperationException("An AzureTrustedSigner can only be used with one metadata file.");
                }

                return _session;
            }

            var metadata = AzureTrustedSigningMetadata.Load(metadataPath);
            _log.Info(
                $"Using Azure Trusted Signing (endpoint: {metadata.Endpoint}, account: {metadata.CodeSigningAccountName}, " +
                $"profile: {metadata.CertificateProfileName}).");

            var client = new AzureTrustedSigningClient(metadata, _clientFactory(metadata), _log);
            var keyProvider = new AzureKeyProvider(client);
            try {
                var key = keyProvider.GetKey();
                _log.Info(
                    $"Signing certificate: {key.Certificate.Subject} ({key.Certificate.Thumbprint}), valid until {key.Certificate.NotAfter:u}.");
            } catch {
                keyProvider.Dispose();
                throw;
            }

            return _session = new AzureSession(fullPath, keyProvider);
        }
    }

    /// <summary>Releases the cached Azure session (remote keys and certificates).</summary>
    public void Dispose()
    {
        lock (_sessionLock) {
            _session?.KeyProvider.Dispose();
            _session = null;
        }
    }

    /// <summary>
    /// Signs <paramref name="files"/> with the key from <paramref name="keyProvider"/> instead of an Azure certificate profile
    /// (used by tests, with an in-memory key).
    /// </summary>
    /// <param name="files">Files to sign (PE images, or .msi on Windows).</param>
    /// <param name="keyProvider">Supplies the signing key.</param>
    /// <param name="timestamper">Timestamps PE signatures; null to not timestamp.</param>
    /// <param name="programName">Signature description (SpcSpOpusInfo programName).</param>
    /// <param name="parallelism">Maximum number of files signed concurrently.</param>
    /// <param name="progress">Receives the percentage of files signed.</param>
    /// <param name="shouldSign">Overrides which files are signed; defaults to skipping already signed files.</param>
    /// <param name="msiSignerFactory">Creates the MSI signer; defaults to <see cref="MsiAzureSigner"/> using the timestamper's URL.</param>
    public Task SignFilesAsync(string[] files, IAuthenticodeKeyProvider keyProvider, Rfc3161Timestamper? timestamper, string? programName,
        int parallelism, Action<int> progress, Func<string, bool>? shouldSign = null,
        Func<AuthenticodeSigningKey, IMsiSigner>? msiSignerFactory = null)
    {
        var toSign = FilterFilesToSign(files, shouldSign);
        if (toSign.Length == 0) {
            progress(100);
            return Task.CompletedTask;
        }

        string? msiTimestampUrl = timestamper?.Url.ToString();
        return SignFilesCoreAsync(toSign, keyProvider, timestamper, msiTimestampUrl, programName, parallelism, progress, msiSignerFactory);
    }

    /// <summary>
    /// The skip check used when no Windows trust evaluation is available: a file that already has an Authenticode
    /// certificate table is left alone.
    /// </summary>
    public static bool ShouldSignByCertificateTable(ILogger log, string filePath)
    {
        if (!File.Exists(filePath)) {
            log.Warn($"Cannot sign '{filePath}', file does not exist.");
            return false;
        }

        if (PeAuthenticode.HasCertificateTable(filePath)) {
            log.Debug($"'{filePath}' already has an Authenticode signature, skipping...");
            return false;
        }

        return true;
    }

    private string[] FilterFilesToSign(string[] files, Func<string, bool>? shouldSign)
    {
        shouldSign ??= OperatingSystem.IsWindows()
            ? f => CodeSign.ShouldSign(_log, f)
            : f => ShouldSignByCertificateTable(_log, f);

        var toSign = files.Where(f => shouldSign(f)).Select(Path.GetFullPath).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (toSign.Length != files.Length) {
            _log.Info($"{toSign.Length} file(s) will be signed, {files.Length - toSign.Length} will be skipped.");
        }

        return toSign;
    }

    private async Task SignFilesCoreAsync(string[] files, IAuthenticodeKeyProvider keyProvider, Rfc3161Timestamper? timestamper,
        string? msiTimestampUrl, string? programName, int parallelism, Action<int> progress,
        Func<AuthenticodeSigningKey, IMsiSigner>? msiSignerFactory = null)
    {
        var msiFiles = files.Where(f => Path.GetExtension(f).Equals(".msi", StringComparison.OrdinalIgnoreCase)).ToArray();
        var peFiles = files.Except(msiFiles).ToArray();
        foreach (var file in peFiles) {
            if (!PathUtil.FileIsLikelyPEImage(file)) {
                throw new UserInfoException($"Azure Trusted Signing does not support '{file}'.");
            }
        }

        if (msiFiles.Length > 0 && !OperatingSystem.IsWindows()) {
            throw new UserInfoException($"Signing MSI files is only supported on Windows ('{msiFiles[0]}').");
        }

        int total = files.Length;
        int done = 0;
        void ReportFileSigned()
        {
            int count = Interlocked.Increment(ref done);
            _log.Info($"Code-signed {count}/{total} files");
            progress(count * 100 / total);
        }

        if (peFiles.Length > 0) {
            var signer = new PeAuthenticodeSigner(_log);
            Exception? firstFailure = null;
            string? failedFile = null;
            await Task.Run(() => {
                var options = new ParallelOptions { MaxDegreeOfParallelism = Math.Max(1, parallelism) };
                Parallel.ForEach(peFiles, options, (file, state) => {
                    if (state.ShouldExitCurrentIteration) return;
                    try {
                        SignWithKeyRefresh(keyProvider, key => signer.SignFile(file, key, timestamper, programName));
                        ReportFileSigned();
                    } catch (Exception ex) {
                        if (Interlocked.CompareExchange(ref firstFailure, ex, null) == null) {
                            failedFile = file;
                        }

                        state.Stop();
                    }
                });
            }).ConfigureAwait(false);

            if (firstFailure != null) {
                throw WrapFailure(failedFile!, firstFailure);
            }
        }

        if (msiFiles.Length > 0 && OperatingSystem.IsWindows()) {
            SignMsiFiles(msiFiles, keyProvider, msiTimestampUrl, programName, msiSignerFactory, ReportFileSigned);
        }
    }

    [SupportedOSPlatform("windows")]
    private void SignMsiFiles(string[] msiFiles, IAuthenticodeKeyProvider keyProvider, string? msiTimestampUrl, string? programName,
        Func<AuthenticodeSigningKey, IMsiSigner>? msiSignerFactory, Action reportFileSigned)
    {
        // the MSI signer is bound to one key, so it is rebuilt if the key is refreshed after a certificate rotation
        IMsiSigner? msiSigner = null;
        AuthenticodeSigningKey? msiSignerKey = null;
        IMsiSigner GetMsiSigner(AuthenticodeSigningKey key)
        {
            if (msiSigner == null || !ReferenceEquals(msiSignerKey, key)) {
                msiSigner?.Dispose();
                msiSigner = null;
                msiSigner = msiSignerFactory?.Invoke(key) ?? new MsiAzureSigner(key, _log, msiTimestampUrl);
                msiSignerKey = key;
            }

            return msiSigner;
        }

        try {
            foreach (var file in msiFiles) {
                try {
                    SignWithKeyRefresh(keyProvider, key => GetMsiSigner(key).SignFile(file, programName));
                } catch (Exception ex) {
                    throw WrapFailure(file, ex);
                }

                reportFileSigned();
            }
        } finally {
            msiSigner?.Dispose();
        }
    }

    /// <summary>
    /// Signs with the current key. If the remote certificate was rotated after it was cached, the key is refreshed and
    /// the signing retried once.
    /// </summary>
    private static void SignWithKeyRefresh(IAuthenticodeKeyProvider keyProvider, Action<AuthenticodeSigningKey> sign)
    {
        var key = keyProvider.GetKey();
        try {
            sign(key);
        } catch (Exception ex) when (IsCertificateMismatch(ex)) {
            AuthenticodeSigningKey? freshKey;
            try {
                freshKey = keyProvider.RefreshKey(key);
            } catch (Exception refreshEx) {
                // report both: the refresh failure alone would hide why a refresh was attempted
                throw new UserInfoException(
                    $"{ex.Message} Fetching the current certificate chain also failed: {refreshEx.Message}",
                    new AggregateException(ex, refreshEx));
            }

            if (freshKey == null || ReferenceEquals(freshKey, key)) throw;
            sign(freshKey);
        }
    }

    private static bool IsCertificateMismatch(Exception ex)
    {
        // the service reported a different signing certificate, or the signature does not verify with the cached one
        for (var e = ex; e != null; e = e.InnerException) {
            if (e is AzureSigningCertificateChangedException or SigningKeyMismatchException) return true;
        }

        return false;
    }

    private static Exception WrapFailure(string file, Exception ex)
    {
        // always name the file (with --signParallel many files are in flight), unless the message already does
        if (ex is UserInfoException && ex.Message.Contains(file, StringComparison.OrdinalIgnoreCase)) return ex;
        return new UserInfoException($"Failed to sign '{file}': {ex.Message}", ex);
    }

    private sealed record AzureSession(string MetadataPath, AzureKeyProvider KeyProvider);

    /// <summary>Key provider backed by the Azure certificate chain, rebuilt when the certificate rotates.</summary>
    private sealed class AzureKeyProvider(AzureTrustedSigningClient client) : IAuthenticodeKeyProvider, IDisposable
    {
        private readonly object _lock = new();
        private readonly List<AuthenticodeSigningKey> _keys = [];
        private AuthenticodeSigningKey? _current;

        public AuthenticodeSigningKey GetKey()
        {
            lock (_lock) {
                return _current ??= CreateKey(client.GetCertificateChain());
            }
        }

        public AuthenticodeSigningKey? RefreshKey(AuthenticodeSigningKey staleKey)
        {
            lock (_lock) {
                if (_current != null && !ReferenceEquals(_current, staleKey)) {
                    return _current; // another file already refreshed it
                }

                var chain = client.RefreshCertificateChain(staleKey.Certificate.Thumbprint);
                if (chain.Leaf.Thumbprint == staleKey.Certificate.Thumbprint) {
                    return null;
                }

                return _current = CreateKey(chain);
            }
        }

        private AuthenticodeSigningKey CreateKey(AzureCertificateChain chain)
        {
            var rsa = new RemoteRsa(client, chain.Leaf.GetRSAPublicKey()!);
            var key = new AuthenticodeSigningKey(rsa, chain.Leaf, chain.Intermediates);
            _keys.Add(key);
            return key;
        }

        public void Dispose()
        {
            lock (_lock) {
                foreach (var key in _keys) {
                    key.PrivateKey.Dispose();
                }

                _keys.Clear();
            }
        }
    }
}

/// <summary>Signs MSI files (seam for tests).</summary>
public interface IMsiSigner : IDisposable
{
    void SignFile(string msiPath, string? description);
}
