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
        private void UnixExclusiveLock(int fd)
        {
            int ret;
            if (VelopackRuntimeInfo.IsLinux) {
                var lockOpt = new linux_flock {
                    l_type = F_WRLCK,
                    l_whence = SEEK_SET,
                    l_start = 0,
                    l_len = 0, // 0 means to lock the entire file
                    l_pid = 0,
                };
                ret = fcntl(fd, F_SETLK, ref lockOpt);
            } else if (VelopackRuntimeInfo.IsOSX) {
                var lockOpt = new osx_flock {
                    l_start = 0,
                    l_len = 0, // 0 means to lock the entire file
                    l_pid = 0,
                    l_type = F_WRLCK,
                    l_whence = SEEK_SET,
                };
                Console.WriteLine("hello");
                ret = fcntl(fd, F_SETLK, ref lockOpt);
            } else {
                throw new PlatformNotSupportedException();
            }
          
            Console.WriteLine(ret);
            if (ret == -1) {
                int errno = Marshal.GetLastWin32Error();
                throw new IOException($"fcntl F_SETLK failed, errno: {errno}", new Win32Exception(errno));
            }
        }

        [SupportedOSPlatform("linux")]
        [DllImport("libc", SetLastError = true)]
        private static extern int fcntl(int fd, int cmd, ref linux_flock linux_flock);
        
        [SupportedOSPlatform("macos")]
        [DllImport("libc", SetLastError = true)]
        private static extern int fcntl(int fd, int cmd, ref osx_flock linux_flock);

        [SupportedOSPlatform("linux")]
        [StructLayout(LayoutKind.Sequential)]
        private struct linux_flock
        {
            public short l_type; /* Type of lock: F_RDLCK, F_WRLCK, F_UNLCK */
            public short l_whence; /* How to interpret l_start: SEEK_SET, SEEK_CUR, SEEK_END */
            public long l_start; /* Starting offset for lock */
            public long l_len; /* Number of bytes to lock */
            public int l_pid; /* PID of the process blocking our lock (F_GETLK only) */
        }
        
        [SupportedOSPlatform("macos")]
        [StructLayout(LayoutKind.Sequential)]
        private struct osx_flock
        {
            public long l_start; /* Starting offset for lock */
            public long l_len; /* Number of bytes to lock */
            public int l_pid; /* PID of the process blocking our lock (F_GETLK only) */
            public short l_type; /* Type of lock: F_RDLCK, F_WRLCK, F_UNLCK */
            public short l_whence; /* How to interpret l_start: SEEK_SET, SEEK_CUR, SEEK_END */
        }

        private const int F_SETLK = 6; /* Non-blocking lock */
        private const short F_RDLCK = 0; /* Read lock */
        private const short F_WRLCK = 1; /* Write lock */
        private const short F_UNLCK = 2; /* Remove lock */
        private const short SEEK_SET = 0;

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