using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using Microsoft.Win32.SafeHandles;

namespace Velopack.Ipc
{
    /// <summary>
    /// The few native calls the IPC layer needs that have no managed equivalent. Plain P/Invoke (no <c>unsafe</c>), the
    /// same source on every target framework; all Windows APIs are Vista+ and load on Windows 7.
    /// </summary>
    internal static class IpcNative
    {
        private const string Kernel32 = "kernel32.dll";

        private const uint FILE_SHARE_ALL = 0x1 | 0x2 | 0x4;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;

        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern SafeFileHandle CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern uint GetFinalPathNameByHandleW(SafeFileHandle hFile, [Out] char[] lpszFilePath, uint cchFilePath, uint dwFlags);

        private const uint PIPE_ACCESS_DUPLEX = 0x3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
        private const uint PIPE_REJECT_REMOTE_CLIENTS = 0x8;
        private const uint PIPE_UNLIMITED_INSTANCES = 255;
        private const uint PIPE_BUFFER_SIZE = 65536;

        [StructLayout(LayoutKind.Sequential)]
        private struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        private static extern SafePipeHandle CreateNamedPipeW(string lpName, uint dwOpenMode, uint dwPipeMode, uint nMaxInstances,
            uint nOutBufferSize, uint nInBufferSize, uint nDefaultTimeOut, ref SECURITY_ATTRIBUTES lpSecurityAttributes);

        [DllImport(Kernel32, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe, out uint serverProcessId);

        [DllImport("libc", ExactSpelling = true)]
        public static extern uint getuid();

        [DllImport("libc", SetLastError = true, ExactSpelling = true)]
        public static extern int chmod(string path, uint mode);

        [DllImport("libc", ExactSpelling = true)]
        private static extern IntPtr realpath(byte[] path, IntPtr resolved);

        [DllImport("libc", ExactSpelling = true)]
        private static extern void free(IntPtr ptr);

        [DllImport("libc", ExactSpelling = true)]
        private static extern UIntPtr confstr(int name, byte[] buffer, UIntPtr length);

        private const int _CS_DARWIN_USER_TEMP_DIR = 65537;

        /// <summary>
        /// One duplex, byte-mode, overlapped server instance of <c>\\.\pipe\{pipeName}</c> with the given security
        /// descriptor, wrapped in a <see cref="NamedPipeServerStream"/>. No managed API sets <c>PIPE_REJECT_REMOTE_CLIENTS</c>
        /// (and <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c> only exists on net8+), so the instance is created here.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static NamedPipeServerStream CreatePipeServer(string pipeName, byte[] securityDescriptor, bool firstInstance)
        {
            var pin = GCHandle.Alloc(securityDescriptor, GCHandleType.Pinned);
            try {
                var attributes = new SECURITY_ATTRIBUTES {
                    nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                    lpSecurityDescriptor = pin.AddrOfPinnedObject(),
                };
                var openMode = PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | (firstInstance ? FILE_FLAG_FIRST_PIPE_INSTANCE : 0);
                var handle = CreateNamedPipeW(@"\\.\pipe\" + pipeName, openMode, PIPE_REJECT_REMOTE_CLIENTS, PIPE_UNLIMITED_INSTANCES,
                    PIPE_BUFFER_SIZE, PIPE_BUFFER_SIZE, 0, ref attributes);
                if (handle.IsInvalid) {
                    var error = Marshal.GetLastWin32Error();
                    handle.Dispose();
                    throw new Win32Exception(error);
                }

                return new NamedPipeServerStream(PipeDirection.InOut, true, false, handle);
            } finally {
                pin.Free();
            }
        }

        /// <summary>
        /// The final (normalized) path of an existing directory, as the file system reports it (<c>\\?\C:\...</c>,
        /// resolving 8.3 names, junctions/symlinks, mapped drives and on-disk casing), or the input on any failure.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static string GetFinalPath(string path)
        {
            using var handle = CreateFileW(path, 0, FILE_SHARE_ALL, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_BACKUP_SEMANTICS, IntPtr.Zero);
            if (handle.IsInvalid) {
                return path;
            }

            var buffer = new char[1024];
            var length = GetFinalPathNameByHandleW(handle, buffer, (uint) buffer.Length, 0);
            if (length > buffer.Length) {
                buffer = new char[length];
                length = GetFinalPathNameByHandleW(handle, buffer, (uint) buffer.Length, 0);
            }

            return length == 0 || length > buffer.Length ? path : new string(buffer, 0, (int) length);
        }

        /// <summary> <c>confstr(_CS_DARWIN_USER_TEMP_DIR)</c>, the per-user temporary directory on macOS, or null. </summary>
        [SupportedOSPlatform("macos")]
        public static string? DarwinUserTempDir()
        {
            var buffer = new byte[1024];
            var needed = confstr(_CS_DARWIN_USER_TEMP_DIR, buffer, (UIntPtr) buffer.Length).ToUInt64();
            if (needed <= 1 || needed > (ulong) buffer.Length) {
                return null;
            }

            return Encoding.UTF8.GetString(buffer, 0, (int) needed - 1);
        }

        /// <summary> <c>realpath(3)</c> of the path, or the input when it cannot be resolved. </summary>
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static string RealPath(string path)
        {
            var resolved = realpath(Encoding.UTF8.GetBytes(path + "\0"), IntPtr.Zero);
            if (resolved == IntPtr.Zero) {
                return path;
            }

            try {
                var length = 0;
                while (Marshal.ReadByte(resolved, length) != 0) {
                    length++;
                }

                var bytes = new byte[length];
                Marshal.Copy(resolved, bytes, 0, length);
                return Encoding.UTF8.GetString(bytes);
            } finally {
                free(resolved);
            }
        }
    }
}
