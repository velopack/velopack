using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Velopack.Util
{
    internal static class SymbolicLink
    {
        /// <summary>
        /// Creates a symlink from the specified directory to the specified target directory.
        /// </summary>
        /// <param name="linkPath">The symlink path</param>
        /// <param name="targetPath">The target directory or file</param>
        /// <param name="overwrite">If true overwrites an existing reparse point or empty directory</param>
        /// <param name="relative">If true, stores a relative path from the link to the target, rather than an absolute path.</param>
        public static void Create(string linkPath, string targetPath, bool overwrite = true, bool relative = false)
        {
            linkPath = Path.GetFullPath(linkPath);
            targetPath = Path.GetFullPath(targetPath);

            if (!Directory.Exists(targetPath) && !File.Exists(targetPath)) {
                throw new IOException("Target path does not exist.");
            }

            if (Directory.Exists(linkPath) || File.Exists(linkPath)) {
                if (overwrite) {
                    IoUtil.DeleteFileOrDirectoryHard(linkPath);
                } else {
                    throw new IOException("Junction / symlink path already exists and overwrite parameter is false.");
                }
            }

            var finalTarget = relative
                ? PathUtil.MakePathRelativeTo(Path.GetDirectoryName(linkPath)!, targetPath)
                : targetPath;

            if (Directory.Exists(targetPath)) {
#if NETSTANDARD
                if (VelopackRuntimeInfo.IsWindows) {
                    if (!CreateSymbolicLink(linkPath, finalTarget, SYMBOLIC_LINK_FLAG_DIRECTORY | SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE))
                        ThrowLastWin32Error("Unable to create junction point / symlink.");
                } else {
                    var linkInfo = new Mono.Unix.UnixSymbolicLinkInfo(linkPath);
                    linkInfo.CreateSymbolicLinkTo(targetPath);
                }
#elif NETFRAMEWORK
                if (!CreateSymbolicLink(linkPath, finalTarget, SYMBOLIC_LINK_FLAG_DIRECTORY | SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE))
                    ThrowLastWin32Error("Unable to create junction point / symlink.");
#else
                Directory.CreateSymbolicLink(linkPath, finalTarget);
#endif
            } else if (File.Exists(targetPath)) {
#if NETSTANDARD
                if (VelopackRuntimeInfo.IsWindows) {
                    if (!CreateSymbolicLink(linkPath, finalTarget, SYMBOLIC_LINK_FLAG_FILE | SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE))
                        ThrowLastWin32Error("Unable to create junction point / symlink.");
                } else {
                    var fileInfo = new Mono.Unix.UnixFileInfo(targetPath);
                    fileInfo.CreateSymbolicLink(linkPath);
                }
#elif NETFRAMEWORK
                if (!CreateSymbolicLink(linkPath, finalTarget, SYMBOLIC_LINK_FLAG_FILE | SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE))
                    ThrowLastWin32Error("Unable to create junction point / symlink.");
#else
                File.CreateSymbolicLink(linkPath, finalTarget);
#endif
            } else {
                throw new IOException("Target path does not exist.");
            }
        }

        /// <summary>
        /// Returns true if the specified path exists and is a junction point or symlink.
        /// If the path exists but is not a junction point or symlink, returns false.
        /// </summary>
        public static bool Exists(string linkPath)
        {
            return TryGetLinkFsi(linkPath, out var _);
        }

        /// <summary>
        /// Does nothing if the path does not exist. If the path exists but is not 
        /// a junction / symlink, throws an IOException.
        /// </summary>
        public static void Delete(string linkPath)
        {
            var isLink = TryGetLinkFsi(linkPath, out var fsi);
            if (fsi != null && !isLink) {
                throw new IOException("Path is not a junction point / symlink.");
            } else {
                fsi?.Delete();
            }
        }

        /// <summary>
        /// Get the target of a junction point or symlink.
        /// </summary>
        /// <param name="linkPath">The location of the symlink or junction point</param>
        /// <param name="relative">If true, the returned target path will be relative to the linkPath. Otherwise, it will be an absolute path.</param>
        public static string GetTarget(string linkPath, bool relative = false)
        {
            var target = GetUnresolvedTarget(linkPath);
            if (relative) {
                if (Path.IsPathRooted(target)) {
                    return PathUtil.MakePathRelativeTo(Path.GetDirectoryName(linkPath)!, target);
                } else {
                    return target;
                }
            } else {
                if (Path.IsPathRooted(target)) {
                    return Path.GetFullPath(target);
                } else {
                    return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(linkPath)!, target));
                }
            }
        }

        private static string GetUnresolvedTarget(string linkPath)
        {
            if (TryGetLinkFsi(linkPath, out var fsi)) {
#if NETSTANDARD
                if (VelopackRuntimeInfo.IsWindows) {
                    return GetTargetWin32(linkPath);
                } else {
                    return Mono.Unix.UnixPath.ReadLink(linkPath);
                }
#elif NETFRAMEWORK
                return GetTargetWin32(linkPath);
#else
                return fsi.LinkTarget!;
#endif
            }

            throw new IOException("Path does not exist or is not a junction point / symlink.");
        }

        private static bool TryGetLinkFsi(string path, out FileSystemInfo fsi)
        {
            fsi = null!;
            if (Directory.Exists(path)) {
                fsi = new DirectoryInfo(path);
            } else if (File.Exists(path)) {
                fsi = new FileInfo(path);
            }

            return fsi != null && (fsi.Attributes & FileAttributes.ReparsePoint) != 0;
        }


#if NETFRAMEWORK || NETSTANDARD
        [Flags]
        private enum EFileAttributes : uint
        {
            Readonly = 0x00000001,
            Hidden = 0x00000002,
            System = 0x00000004,
            Directory = 0x00000010,
            Archive = 0x00000020,
            Device = 0x00000040,
            Normal = 0x00000080,
            Temporary = 0x00000100,
            SparseFile = 0x00000200,
            ReparsePoint = 0x00000400,
            Compressed = 0x00000800,
            Offline = 0x00001000,
            NotContentIndexed = 0x00002000,
            Encrypted = 0x00004000,
            Write_Through = 0x80000000,
            Overlapped = 0x40000000,
            NoBuffering = 0x20000000,
            RandomAccess = 0x10000000,
            SequentialScan = 0x08000000,
            DeleteOnClose = 0x04000000,
            BackupSemantics = 0x02000000,
            PosixSemantics = 0x01000000,
            OpenReparsePoint = 0x00200000,
            OpenNoRecall = 0x00100000,
            FirstPipeInstance = 0x00080000
        }

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            FileAccess dwDesiredAccess,
            FileShare dwShareMode,
            IntPtr lpSecurityAttributes,
            FileMode dwCreationDisposition,
            EFileAttributes dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool CreateSymbolicLink(string lpSymlinkFileName, string lpTargetFileName, int dwFlags);

        private const int SYMBOLIC_LINK_FLAG_FILE = 0x0;
        private const int SYMBOLIC_LINK_FLAG_DIRECTORY = 0x1;
        private const int SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE = 0x2;
        private const int INITIAL_REPARSE_DATA_BUFFER_SIZE = 1024;
        private const int FSCTL_GET_REPARSE_POINT = 0x000900a8;
        private const int ERROR_INSUFFICIENT_BUFFER = 0x7A;
        private const int ERROR_MORE_DATA = 0xEA;
        private const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;
        private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;

        [DllImport("Kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint GetFinalPathNameByHandle(IntPtr hFile, [MarshalAs(UnmanagedType.LPTStr)] StringBuilder lpszFilePath, uint cchFilePath,
            uint dwFlags);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle deviceHandle,
            uint ioControlCode,
            IntPtr inputBuffer,
            int inputBufferSize,
            byte[] outputBuffer,
            int outputBufferSize,
            out int bytesReturned,
            IntPtr overlapped);

        private static string GetTargetWin32(string linkPath)
        {
            // https://github.com/microsoft/BuildXL/blob/main/Public/Src/Utilities/Native/IO/Windows/FileSystem.Win.cs#L2711
            // http://blog.kalmbach-software.de/2008/02/28/howto-correctly-read-reparse-data-in-vista/
            // https://github.com/dotnet/runtime/blob/e5f0c361f5baea5e2b56e1776143d841b0cc6e6c/src/libraries/System.Private.CoreLib/src/System/IO/FileSystem.Windows.cs#L544
            SafeFileHandle handle = CreateFile(
                linkPath,
                dwDesiredAccess: 0,
                FileShare.ReadWrite | FileShare.Delete,
                lpSecurityAttributes: IntPtr.Zero,
                FileMode.Open,
                dwFlagsAndAttributes: EFileAttributes.BackupSemantics | EFileAttributes.OpenReparsePoint,
                hTemplateFile: IntPtr.Zero);

            if (Marshal.GetLastWin32Error() != 0)
                ThrowLastWin32Error("Unable to open reparse point.");

            int bufferSize = INITIAL_REPARSE_DATA_BUFFER_SIZE;
            int errorCode = ERROR_INSUFFICIENT_BUFFER;

            byte[] buffer = null!;
            while (errorCode == ERROR_MORE_DATA || errorCode == ERROR_INSUFFICIENT_BUFFER) {
                buffer = new byte[bufferSize];
                bool success = false;

                int bufferReturnedSize;
                success = DeviceIoControl(
                    handle,
                    FSCTL_GET_REPARSE_POINT,
                    IntPtr.Zero,
                    0,
                    buffer,
                    bufferSize,
                    out bufferReturnedSize,
                    IntPtr.Zero);

                bufferSize *= 2;
                errorCode = success ? 0 : Marshal.GetLastWin32Error();
            }

            if (errorCode != 0) {
                throw new Win32Exception(errorCode);
            }

            const uint PrintNameOffsetIndex = 12;
            const uint PrintNameLengthIndex = 14;
            const uint SubsNameOffsetIndex = 8;
            const uint SubsNameLengthIndex = 10;

            uint reparsePointTag = BitConverter.ToUInt32(buffer, 0);
            if (reparsePointTag != IO_REPARSE_TAG_SYMLINK && reparsePointTag != IO_REPARSE_TAG_MOUNT_POINT) {
                throw new NotSupportedException($"Reparse point tag {reparsePointTag:X} not supported");
            }

            uint pathBufferOffsetIndex = (uint) ((reparsePointTag == IO_REPARSE_TAG_SYMLINK) ? 20 : 16);

            int nameOffset = BitConverter.ToInt16(buffer, (int) PrintNameOffsetIndex);
            int nameLength = BitConverter.ToInt16(buffer, (int) PrintNameLengthIndex);
            string targetPath = Encoding.Unicode.GetString(buffer, (int) pathBufferOffsetIndex + nameOffset, nameLength);

            if (string.IsNullOrWhiteSpace(targetPath)) {
                nameOffset = BitConverter.ToInt16(buffer, (int) SubsNameOffsetIndex);
                nameLength = BitConverter.ToInt16(buffer, (int) SubsNameLengthIndex);
                targetPath = Encoding.Unicode.GetString(buffer, (int) pathBufferOffsetIndex + nameOffset, nameLength);
            }

            return targetPath;
        }

        private static void ThrowLastWin32Error(string message)
        {
            throw new IOException(message, Marshal.GetExceptionForHR(Marshal.GetHRForLastWin32Error()));
        }
#endif
    }
}