using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Velopack.Util
{
    internal static partial class SymbolicLink
    {
        private const string Kernel32 = "kernel32.dll";

        private const uint FSCTL_GET_REPARSE_POINT = 0x000900A8;
        private const uint IO_REPARSE_TAG_SYMLINK = 0xA000000C;
        private const uint IO_REPARSE_TAG_MOUNT_POINT = 0xA0000003;
        private const int ERROR_NOT_A_REPARSE_POINT = 4390;
        private const int ERROR_INVALID_PARAMETER = 87;
        private const int ERROR_PRIVILEGE_NOT_HELD = 1314;
        private const int HRESULT_PRIVILEGE_NOT_HELD = unchecked((int) 0x80070522); // HRESULT_FROM_WIN32(ERROR_PRIVILEGE_NOT_HELD)
        private const int MAXIMUM_REPARSE_DATA_BUFFER_SIZE = 16 * 1024;
        private const uint SYMLINK_FLAG_RELATIVE = 1;
        private const uint SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE = 0x2;

        private const uint GENERIC_READ = 0x80000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        private const uint FILE_FLAG_OPEN_REPARSE_POINT = 0x00200000;

        [StructLayout(LayoutKind.Sequential)]
        private struct ReparseHeader
        {
            public uint ReparseTag;
            public ushort ReparseDataLength;
            public ushort Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct SymbolicData
        {
            public ushort SubstituteNameOffset;
            public ushort SubstituteNameLength;
            public ushort PrintNameOffset;
            public ushort PrintNameLength;
            public uint Flags;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct JunctionData
        {
            public ushort SubstituteNameOffset;
            public ushort SubstituteNameLength;
            public ushort PrintNameOffset;
            public ushort PrintNameLength;
        }

        [SupportedOSPlatform("windows")]
        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateSymbolicLinkW")]
        [return: MarshalAs(UnmanagedType.I1)]
        private static extern bool PInvokeWindowsCreateSymlink(
            [In]
            string lpSymlinkFileName,
            [In]
            string lpTargetFileName,
            [In]
            uint dwFlags);

        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern SafeFileHandle CreateFile(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Auto)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeviceIoControl(
            SafeFileHandle hDevice,
            uint dwIoControlCode,
            IntPtr lpInBuffer,
            int nInBufferSize,
            IntPtr lpOutBuffer,
            int nOutBufferSize,
            out int lpBytesReturned,
            IntPtr lpOverlapped);

        [SupportedOSPlatform("windows")]
        private static string WindowsReadLink(string symlinkPath)
        {
            using (var hReparsePoint = CreateFile(
                       symlinkPath,
                       GENERIC_READ,
                       FILE_SHARE_READ | FILE_SHARE_WRITE,
                       IntPtr.Zero,
                       OPEN_EXISTING,
                       FILE_FLAG_BACKUP_SEMANTICS | FILE_FLAG_OPEN_REPARSE_POINT,
                       IntPtr.Zero)) {
                if (hReparsePoint.IsInvalid) {
                    throw new Win32Exception(Marshal.GetLastWin32Error());
                }

                var buffer = Marshal.AllocHGlobal(MAXIMUM_REPARSE_DATA_BUFFER_SIZE);
                try {
                    int bytesReturned;
                    var success = DeviceIoControl(
                        hReparsePoint,
                        FSCTL_GET_REPARSE_POINT,
                        IntPtr.Zero,
                        0,
                        buffer,
                        MAXIMUM_REPARSE_DATA_BUFFER_SIZE,
                        out bytesReturned,
                        IntPtr.Zero);

                    if (!success) {
                        int error = Marshal.GetLastWin32Error();
                        // The file or directory is not a reparse point.
                        if (error == ERROR_NOT_A_REPARSE_POINT) {
                            throw new InvalidOperationException($"Path is not a symbolic link: {symlinkPath}");
                        }

                        throw new Win32Exception(error);
                    }

                    var reparseHeader = Marshal.PtrToStructure<ReparseHeader>(buffer);
                    var reparseHeaderSize = Marshal.SizeOf<ReparseHeader>();

                    // We always use SubstituteName instead of PrintName,
                    // the latter is just the display name and can show something unrelated to the target.
                    if (reparseHeader.ReparseTag == IO_REPARSE_TAG_SYMLINK) {
                        var symbolicData = Marshal.PtrToStructure<SymbolicData>(buffer + reparseHeaderSize);
                        var offset = Marshal.SizeOf<SymbolicData>() + reparseHeaderSize;
                        var target = ReadStringFromBuffer(buffer, offset + symbolicData.SubstituteNameOffset, symbolicData.SubstituteNameLength);

                        bool isRelative = (symbolicData.Flags & SYMLINK_FLAG_RELATIVE) != 0;
                        if (!isRelative) {
                            // Absolute target is in NT format and we need to clean it up
                            return ParseNTPath(target);
                        }

                        // Return relative path as-is
                        return target;
                    } else if (reparseHeader.ReparseTag == IO_REPARSE_TAG_MOUNT_POINT) {
                        var junctionData = Marshal.PtrToStructure<JunctionData>(buffer + reparseHeaderSize);
                        var offset = Marshal.SizeOf<JunctionData>() + reparseHeaderSize;
                        var target = ReadStringFromBuffer(buffer, offset + junctionData.SubstituteNameOffset, junctionData.SubstituteNameLength);

                        // Mount points are always absolute and in NT format
                        return ParseNTPath(target);
                    }

                    throw new InvalidOperationException($"Unsupported reparse point type: 0x{reparseHeader.ReparseTag:X}");
                } finally {
                    Marshal.FreeHGlobal(buffer);
                }
            }
        }

        private static string ReadStringFromBuffer(IntPtr buffer, int offset, int byteCount)
        {
            var bytes = new byte[byteCount];
            Marshal.Copy(buffer + offset, bytes, 0, byteCount);
            return Encoding.Unicode.GetString(bytes);
        }

        private static string ParseNTPath(string path)
        {
            // NT paths come in different forms:
            // \??\C:\foo - DOS device path
            // \DosDevices\C:\foo - DOS device path
            // \Global??\C:\foo - DOS device path  
            // \??\UNC\server\share - UNC path

            const string NTPathPrefix = "\\??\\";
            const string UNCNTPathPrefix = "\\??\\UNC\\";
            const string UNCPathPrefix = "\\\\";

            if (path.StartsWith(UNCNTPathPrefix, StringComparison.OrdinalIgnoreCase)) {
                // Convert \??\UNC\server\share to \\server\share
                return UNCPathPrefix + path.Substring(UNCNTPathPrefix.Length);
            }

            string[] dosDevicePrefixes = { NTPathPrefix, "\\DosDevices\\", "\\Global??\\" };

            foreach (var prefix in dosDevicePrefixes) {
                if (path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) {
                    path = path.Substring(prefix.Length);
                    break;
                }
            }

            // Remove trailing backslash except for root paths like C:\
            if (path.Length > 3 && path.EndsWith("\\")) {
                path = path.TrimEnd('\\');
            }

            return path;
        }

        /// <summary>
        /// Explains how to allow symlink creation on Windows, which fails with ERROR_PRIVILEGE_NOT_HELD unless the process
        /// holds SeCreateSymbolicLinkPrivilege.
        /// </summary>
        private const string WindowsPrivilegeNotHeldMessage =
            "Windows only allows creating symlinks with Developer Mode enabled (Settings > For developers), " +
            "from an elevated prompt, or with the 'Create symbolic links' user right.";

        private static UnauthorizedAccessException CreatePrivilegeNotHeldException(string linkPath, Exception innerException)
        {
            return new UnauthorizedAccessException($"Could not create symlink '{linkPath}'. {WindowsPrivilegeNotHeldMessage}", innerException);
        }

        [SupportedOSPlatform("windows")]
        private static void WindowsCreateSymlink(string target, string linkPath, SymbolicLinkFlag mode)
        {
            // Developer Mode only lets an unelevated process create symlinks when it passes ALLOW_UNPRIVILEGED_CREATE, as .NET's
            // own File.CreateSymbolicLink does. Windows older than 10 1703 rejects the flag, so retry without it there.
            var created = PInvokeWindowsCreateSymlink(linkPath, target, (uint) mode | SYMBOLIC_LINK_FLAG_ALLOW_UNPRIVILEGED_CREATE);
            if (!created && Marshal.GetLastWin32Error() == ERROR_INVALID_PARAMETER) {
                created = PInvokeWindowsCreateSymlink(linkPath, target, (uint) mode);
            }

            if (!created) {
                var errorCode = Marshal.GetLastWin32Error();
                if (errorCode == ERROR_PRIVILEGE_NOT_HELD) {
                    throw CreatePrivilegeNotHeldException(linkPath, new Win32Exception(errorCode));
                }

                throw new InvalidOperationException($"Error creating symlink: {errorCode}", new Win32Exception(errorCode));
            }
        }
    }
}