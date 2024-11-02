using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Velopack.Util
{
    internal class FileLock : IDisposable
    {
        private readonly string _filePath;
        private FileStream? _fileStream;
        private bool _locked;

        public FileLock(string path)
        {
            _filePath = path;
        }

        public async Task LockAsync()
        {
            if (_locked) {
                return;
            }

            await IoUtil.RetryAsync(
                () => {
                    _fileStream = new FileStream(_filePath, FileMode.Create, FileAccess.Read, FileShare.None, bufferSize: 1, FileOptions.DeleteOnClose);
                    _locked = true;
                    return Task.CompletedTask;
                });
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref this._fileStream, null)?.Dispose();
        }
    }
}