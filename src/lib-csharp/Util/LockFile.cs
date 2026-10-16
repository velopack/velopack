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
    /// <summary> The outcome of a single <see cref="LockFile.TryLockOnce"/> attempt. </summary>
    internal enum LockAttempt
    {
        /// <summary> This instance now holds the lock. </summary>
        Acquired,
        /// <summary> Another process (or another instance in this process, on Windows) holds the lock. </summary>
        Held,
        /// <summary> The lock could not be taken for another reason (eg. the file cannot be created). </summary>
        Failed,
    }

    /// <summary>
    /// An exclusive, cross-process lock on a file, using the same kernel primitive as the Rust library so both exclude
    /// each other: a byte-range lock (<c>LockFileEx</c>) on Windows, <c>lockf</c> on Unix. The file is opened with full
    /// sharing on Windows, so a transient third-party open (indexer, antivirus) never looks like contention. The file
    /// itself is never deleted.
    /// </summary>
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
                        if (TryLockOnceInternal(out var error) != LockAttempt.Acquired) {
                            throw error ?? new IOException("The lock is held by another process.");
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

        /// <summary>
        /// Makes one attempt to take the lock, without retrying. <see cref="LockAttempt.Held"/> is reported only for
        /// genuine contention (<c>ERROR_LOCK_VIOLATION</c> on Windows, <c>EAGAIN</c>/<c>EACCES</c> on Unix).
        /// </summary>
        public LockAttempt TryLockOnce()
        {
            if (IsLocked) {
                return LockAttempt.Acquired;
            }

            _semaphore.Wait();
            try {
                var result = TryLockOnceInternal(out _);
                IsLocked = result == LockAttempt.Acquired;
                return result;
            } finally {
                _semaphore.Release();
            }
        }

        private LockAttempt TryLockOnceInternal(out Exception? error)
        {
            error = null;
            try {
                if (VelopackRuntimeInfo.IsWindows) {
                    WindowsExclusiveLock();
                } else if (VelopackRuntimeInfo.IsLinux || VelopackRuntimeInfo.IsOSX) {
                    UnixExclusiveLock();
                } else {
                    throw new PlatformNotSupportedException();
                }

                return LockAttempt.Acquired;
            } catch (LockHeldException ex) {
                error = ex;
                return LockAttempt.Held;
            } catch (Exception ex) {
                error = ex;
                return LockAttempt.Failed;
            }
        }

        private sealed class LockHeldException : IOException
        {
            public LockHeldException(string message, Exception inner) : base(message, inner) { }
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
                _fileDescriptor = -1;
            }

            // NUL-terminated UTF-8 path
            var fileBytes = Encoding.UTF8.GetBytes(_filePath).Concat(new byte[] { 0 }).ToArray();

            const int O_RDWR = 0x2;
            const int EINTR = 4;
            const int ENOENT = 2;
            const int EAGAIN_LINUX = 11;
            const int EAGAIN_MAC = 35;
            const int EACCES = 13;
            var O_CLOEXEC = VelopackRuntimeInfo.IsOSX ? 0x01000000 : 0x80000;

            // open(2) takes the creation mode as a variadic argument, which P/Invoke cannot pass reliably (Apple
            // arm64 puts variadic arguments on the stack), so a missing file is created with creat(2) and re-opened
            // read-write with O_CLOEXEC, so the lock fd never leaks into child processes.
            int fd;
            do { fd = open(fileBytes, O_RDWR | O_CLOEXEC); } while (fd == -1 && Marshal.GetLastWin32Error() == EINTR);
            if (fd == -1 && Marshal.GetLastWin32Error() == ENOENT) {
                int created;
                do { created = creat(fileBytes, Convert.ToUInt16("666", 8)); } while (created == -1 && Marshal.GetLastWin32Error() == EINTR);
                if (created != -1) {
                    close(created);
                }

                do { fd = open(fileBytes, O_RDWR | O_CLOEXEC); } while (fd == -1 && Marshal.GetLastWin32Error() == EINTR);
            }

            if (fd == -1) {
                int errno = Marshal.GetLastWin32Error();
                throw new IOException($"open failed, errno: {errno}", new Win32Exception(errno));
            }

            int ret;
            do { ret = lockf(fd, 2 /* F_TLOCK */, 0); } while (ret == -1 && Marshal.GetLastWin32Error() == EINTR);

            if (ret != 0) {
                int errno = Marshal.GetLastWin32Error();
                close(fd);
                var inner = new Win32Exception(errno);
                if (errno == EAGAIN_LINUX || errno == EAGAIN_MAC || errno == EACCES) {
                    throw new LockHeldException($"lockf failed, errno: {errno}", inner);
                }

                throw new IOException($"lockf failed, errno: {errno}", inner);
            }

            _fileDescriptor = fd;
        }

        [SupportedOSPlatform("windows")]
        private void WindowsExclusiveLock()
        {
            const int ERROR_LOCK_VIOLATION = 33;
            _fileStream?.Dispose();
            _fileStream = null;
            var stream = new FileStream(
                _filePath,
                FileMode.OpenOrCreate,
                FileAccess.ReadWrite,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 1,
                FileOptions.None);
            try {
                stream.Lock(0, 1);
            } catch (IOException ex) {
                stream.Dispose();
                if ((ex.HResult & 0xFFFF) == ERROR_LOCK_VIOLATION) {
                    throw new LockHeldException("The lock is held by another process.", ex);
                }

                throw;
            } catch {
                stream.Dispose();
                throw;
            }

            _fileStream = stream;
        }

        private void DisposeInternal()
        {
            Interlocked.Exchange(ref this._fileStream, null)?.Dispose();

            if (VelopackRuntimeInfo.IsLinux || VelopackRuntimeInfo.IsOSX) {
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
