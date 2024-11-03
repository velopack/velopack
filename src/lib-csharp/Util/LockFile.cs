using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Win32.SafeHandles;

namespace Velopack.Util
{
    internal class LockFile : IDisposable
    {
        private readonly string _filePath;
        private FileStream? _fileStream;
        private bool _locked;

        public LockFile(string path)
        {
            _filePath = path;
        }

        public async Task LockAsync()
        {
            if (_locked) {
                return;
            }

            try {
                await IoUtil.RetryAsync(
                    () => {
                        Dispose();
                        _fileStream = new FileStream(
                            _filePath,
                            FileMode.Create,
                            FileAccess.ReadWrite,
                            FileShare.None,
                            bufferSize: 1,
                            FileOptions.DeleteOnClose);

                        SafeFileHandle safeFileHandle = _fileStream.SafeFileHandle!;
                        if (VelopackRuntimeInfo.IsLinux || VelopackRuntimeInfo.IsOSX) {
                            int fd = safeFileHandle.DangerousGetHandle().ToInt32();
                            UnixExclusiveLock(fd);
                        } else if (VelopackRuntimeInfo.IsWindows) {
                            WindowsExclusiveLock(safeFileHandle);
                        }

                        _locked = true;
                        return Task.CompletedTask;
                    }).ConfigureAwait(false);
            } catch (Exception ex) {
                Dispose();
                throw new IOException("Failed to acquire exclusive lock file. Is another operation currently running?", ex);
            }
        }


        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [DllImport("libc", SetLastError = true)]
        private static extern int flock(int fd, int operation);

        private const int LOCK_SH = 1; // Shared lock
        private const int LOCK_EX = 2; // Exclusive lock
        private const int LOCK_NB = 4; // Non-blocking
        private const int LOCK_UN = 8; // Unlock

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private void UnixExclusiveLock(int fd)
        {
            int ret = flock(fd, LOCK_EX | LOCK_NB);
            if (ret != 0) {
                throw new IOException("flock returned error: " + ret);
            }
        }

        [SupportedOSPlatform("windows")]
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool LockFileEx(SafeFileHandle hFile, uint dwFlags, uint dwReserved, uint nNumberOfBytesToLockLow, uint nNumberOfBytesToLockHigh,
            [In] ref NativeOverlapped lpOverlapped);

        private const uint LOCKFILE_EXCLUSIVE_LOCK = 0x00000002;
        private const uint LOCKFILE_FAIL_IMMEDIATELY = 0x00000001;

        [SupportedOSPlatform("windows")]
        private void WindowsExclusiveLock(SafeFileHandle safeFileHandle)
        {
            NativeOverlapped overlapped = default;
            bool ret = LockFileEx(safeFileHandle, LOCKFILE_EXCLUSIVE_LOCK | LOCKFILE_FAIL_IMMEDIATELY, 0, 1, 0, ref overlapped);
            if (!ret) {
                throw new Win32Exception();
            }
        }

        public void Dispose()
        {
            Interlocked.Exchange(ref this._fileStream, null)?.Dispose();
        }
    }
}