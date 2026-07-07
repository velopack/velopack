using System;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Exceptions;

namespace Velopack.Util
{
    internal class LockFile : IDisposable
    {
        public bool IsLocked { get; private set; }

        private readonly SemaphoreSlim _semaphore = new(1, 1);
        private readonly string _filePath;
        private FileStream? _fileStream;
        private int _fileDescriptor = -1;

        public LockFile(string path)
        {
            _filePath = path;
        }

        public async Task LockAsync()
        {
            if (IsLocked) {
                return;
            }

            try {
                await _semaphore.WaitAsync().ConfigureAwait(false);
                await IoUtil.RetryAsync(
                    async () => {
                        await Task.Delay(1).ConfigureAwait(false);
                        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                            WindowsExclusiveLock();
                        } else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                            UnixExclusiveLock();
                        }
                    }).ConfigureAwait(false);

                IsLocked = true;
            } catch (Exception ex) {
                DisposeInternal();
                throw new AcquireLockFailedException(ex);
            } finally {
                _semaphore.Release();
            }
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [DllImport("libc", SetLastError = true)]
        private static extern int open(byte[] pathname, int flags);

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [DllImport("libc", SetLastError = true)]
        private static extern int creat(byte[] pathname, uint mode);

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [DllImport("libc", SetLastError = true)]
        private static extern int lockf(int fd, int cmd, long len);

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [DllImport("libc", SetLastError = true)]
        private static extern int close(int fd);

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private void UnixExclusiveLock()
        {
            if (_fileDescriptor > 0) {
                close(_fileDescriptor);
            }

            var fileBytes = Encoding.UTF8.GetBytes(_filePath).ToArray();

            const int O_RDWR = 0x2;
            const int O_CLOEXEC = 0x01000000;
            const int EINTR = 4;

            var filePermissionOctal = Convert.ToUInt16("666", 8);

            int fd;
            do { fd = open(fileBytes, O_RDWR | O_CLOEXEC); } while (fd == -1 && Marshal.GetLastWin32Error() == EINTR);

            // if we cant open the file, try to create it...
            if (fd == -1) {
                do { fd = creat(fileBytes, filePermissionOctal); } while (fd == -1 && Marshal.GetLastWin32Error() == EINTR);
            }

            if (fd == -1) {
                int errno = Marshal.GetLastWin32Error();
                close(fd);
                throw new IOException($"creat failed, errno: {errno}", new Win32Exception(errno));
            }

            int ret;
            do { ret = lockf(fd, 2 /* F_TLOCK */, 0); } while (ret == -1 && Marshal.GetLastWin32Error() == EINTR);

            if (ret != 0) {
                int errno = Marshal.GetLastWin32Error();
                close(fd);
                throw new IOException($"lockf failed, errno: {errno}", new Win32Exception(errno));
            }

            _fileDescriptor = fd;
        }

        [SupportedOSPlatform("windows")]
        private void WindowsExclusiveLock()
        {
            _fileStream?.Dispose();
            _fileStream = new FileStream(
                _filePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.None,
                bufferSize: 1,
                FileOptions.None);
            _fileStream.Lock(0, 0);
        }

        private void DisposeInternal()
        {
            Interlocked.Exchange(ref this._fileStream, null)?.Dispose();

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux) || RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) {
                if (_fileDescriptor > 0) {
                    close(_fileDescriptor);
                }
            }

            IsLocked = false;
            _fileDescriptor = -1;
        }

        public void Dispose()
        {
            try {
                _semaphore.Wait();
                DisposeInternal();
            } finally {
                _semaphore.Release();
            }
        }
    }
}