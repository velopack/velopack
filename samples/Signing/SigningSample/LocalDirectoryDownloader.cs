using System.IO;
using Velopack.Sources;

namespace VeloWpfSample
{
    class LocalDirectoryDownloader : IFileDownloader
    {
        private string _basePath;

        public LocalDirectoryDownloader(string basePath)
        {
            _basePath = basePath;
        }

        public Task<byte[]> DownloadBytes(string url, string authorization = null, string accept = null)
        {
            return File.ReadAllBytesAsync(GetFilePath(url));
        }

        public Task DownloadFile(string url, string targetFile, Action<int> progress, string authorization = null, string accept = null, CancellationToken cancelToken = default)
        {
            File.Copy(GetFilePath(url), targetFile);
            return Task.CompletedTask;
        }

        public Task<string> DownloadString(string url, string authorization = null, string accept = null)
        {
            return File.ReadAllTextAsync(GetFilePath(url));
        }

        string GetFilePath(string url)
        {
            return Path.Combine(_basePath, new Uri(url).LocalPath.TrimStart('/'));
        }
    }
}