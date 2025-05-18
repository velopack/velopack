using System.Text;
using Velopack.Sources;

namespace Velopack.Tests;

public class FakeDownloader : IFileDownloader
{
    public string LastUrl { get; private set; }
    public string LastLocalFile { get; private set; }
    public IDictionary<string, string> LastHeaders { get; private set; } = new Dictionary<string, string>();
    public byte[] MockedResponseBytes { get; set; } = [];
    public bool WriteMockLocalFile { get; set; } = false;

    public Task<byte[]> DownloadBytes(string url, IDictionary<string, string> headers, double timeout = 30)
    {
        LastUrl = url;
        LastHeaders = headers;
        return Task.FromResult(MockedResponseBytes);
    }

    public async Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string> headers, double timeout = 30, CancellationToken token = default)
    {
        LastLocalFile = targetFile;
        var resp = await DownloadBytes(url, headers);
        progress?.Invoke(25);
        progress?.Invoke(50);
        progress?.Invoke(75);
        progress?.Invoke(100);
        if (WriteMockLocalFile)
            File.WriteAllBytes(targetFile, resp);
    }

    public async Task<string> DownloadString(string url, IDictionary<string, string> headers, double timeout = 30)
    {
        return Encoding.UTF8.GetString(await DownloadBytes(url, headers));
    }
}
