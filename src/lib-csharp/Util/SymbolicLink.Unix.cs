using System;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Velopack.Util
{
    internal static partial class SymbolicLink
    {
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [DllImport("libc", SetLastError = true)]
        private static extern nint readlink(string path, byte[] buffer, ulong bufferSize);

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [DllImport("libc", SetLastError = true)]
        private static extern int symlink(string target, string linkPath);

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static string UnixReadLink(string symlinkPath)
        {
            const int bufferSize = 1024;
            const int EINTR = 4;
            byte[] buffer = new byte[bufferSize];
            nint bytesWritten;

            do {
                bytesWritten = readlink(symlinkPath, buffer, bufferSize);
            } while (bytesWritten == -1 && Marshal.GetLastWin32Error() == EINTR);

            if (bytesWritten < 1) {
                throw new InvalidOperationException($"Error resolving symlink: {Marshal.GetLastWin32Error()}");
            }

            return Encoding.UTF8.GetString(buffer, 0, (int) bytesWritten);
        }

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static void UnixCreateSymlink(string target, string linkPath)
        {
            const int EINTR = 4;
            int result;

            do {
                result = symlink(target, linkPath);
            } while (result == -1 && Marshal.GetLastWin32Error() == EINTR);

            if (result == -1) {
                throw new InvalidOperationException($"Error creating symlink: {Marshal.GetLastWin32Error()}");
            }
        }
    }
}