using System;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack.Sources;

namespace SigningSample;

internal class SignedDownloader : IFileDownloader
{
    private RSA _publicKey;
    private readonly IFileDownloader _fileDownloader;
    private readonly ILogger logger;

    public SignedDownloader(RSA rsaKey, IFileDownloader fileDownloader, ILogger logger)
    {
        _publicKey = rsaKey;
        _fileDownloader = fileDownloader;
        this.logger = logger;
    }

    async Task<string> IFileDownloader.DownloadString(string url, string authorization, string accept)
    {
        var json = await _fileDownloader.DownloadString(url, authorization, accept)
            .ConfigureAwait(false);

        if (!string.IsNullOrEmpty(json)) {
            logger.LogInformation("Verifying signature...");
            if (!VelopackSignatureHelper.VerifySignature(json, _publicKey))
                throw new CryptographicException("Signature verification failed");

            logger.LogInformation("Signature verified");
        }

        return json;
    }

    Task IFileDownloader.DownloadFile(string url, string targetFile, Action<int> progress, string authorization, string accept, CancellationToken cancelToken)
        => _fileDownloader.DownloadFile(url, targetFile, progress, authorization, accept, cancelToken);

    Task<byte[]> IFileDownloader.DownloadBytes(string url, string authorization, string accept)
        => _fileDownloader.DownloadBytes(url, authorization, accept);
}
