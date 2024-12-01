﻿using System.Text;

namespace Velopack.Tests;

public class FakeDownloader : Sources.IFileDownloader
{
    public string LastUrl { get; private set; }
    public string LastLocalFile { get; private set; }
    public string LastAuthHeader { get; private set; }
    public string LastAcceptHeader { get; private set; }
    public byte[] MockedResponseBytes { get; set; } = new byte[0];
    public bool WriteMockLocalFile { get; set; } = false;

    public Task<byte[]> DownloadBytes(string url, string auth, string acc, double timeout = 30)
    {
        LastUrl = url;
        LastAuthHeader = auth;
        LastAcceptHeader = acc;
        return Task.FromResult(MockedResponseBytes);
    }

    public async Task DownloadFile(string url, string targetFile, Action<int> progress, string auth, string acc, double timeout, CancellationToken token)
    {
        LastLocalFile = targetFile;
        var resp = await DownloadBytes(url, auth, acc);
        progress?.Invoke(25);
        progress?.Invoke(50);
        progress?.Invoke(75);
        progress?.Invoke(100);
        if (WriteMockLocalFile)
            File.WriteAllBytes(targetFile, resp);
    }

    public async Task<string> DownloadString(string url, string auth, string acc, double timeout = 30)
    {
        return Encoding.UTF8.GetString(await DownloadBytes(url, auth, acc));
    }
}
