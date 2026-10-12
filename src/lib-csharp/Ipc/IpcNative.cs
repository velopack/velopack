using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace Velopack.Ipc
{
    /// <summary>
    /// The native calls behind the IPC layer. Everything here is plain P/Invoke (no <c>unsafe</c>), the same source on
    /// every target framework. All Windows APIs are Vista+ and load on Windows 7.
    /// </summary>
    internal static class IpcNative
    {
        private const string Kernel32 = "kernel32.dll";
        private const string Advapi32 = "advapi32.dll";

        public const uint GENERIC_READ = 0x80000000;
        public const uint GENERIC_WRITE = 0x40000000;
        public const uint FILE_SHARE_READ = 0x1;
        public const uint FILE_SHARE_WRITE = 0x2;
        public const uint FILE_SHARE_DELETE = 0x4;
        public const uint OPEN_EXISTING = 3;
        public const uint FILE_FLAG_BACKUP_SEMANTICS = 0x02000000;
        public const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        public const uint FILE_FLAG_FIRST_PIPE_INSTANCE = 0x00080000;
        public const uint SECURITY_SQOS_PRESENT = 0x00100000;
        public const uint SECURITY_IDENTIFICATION = 0x00010000;
        public const uint PIPE_ACCESS_DUPLEX = 0x3;
        public const uint PIPE_TYPE_BYTE = 0x0;
        public const uint PIPE_READMODE_BYTE = 0x0;
        public const uint PIPE_WAIT = 0x0;
        public const uint PIPE_REJECT_REMOTE_CLIENTS = 0x8;
        public const uint PIPE_UNLIMITED_INSTANCES = 255;
        public const uint PIPE_BUFFER_SIZE = 65536;
        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;
        public const uint TOKEN_QUERY = 0x8;
        public const int TokenUser = 1;
        public const int TokenIntegrityLevel = 25;
        public const uint SDDL_REVISION_1 = 1;

        public const int ERROR_FILE_NOT_FOUND = 2;
        public const int ERROR_ACCESS_DENIED = 5;
        public const int ERROR_INSUFFICIENT_BUFFER = 122;
        public const int ERROR_PIPE_BUSY = 231;

        public static readonly IntPtr INVALID_HANDLE_VALUE = new IntPtr(-1);

        [StructLayout(LayoutKind.Sequential)]
        public struct SECURITY_ATTRIBUTES
        {
            public int nLength;
            public IntPtr lpSecurityDescriptor;
            public int bInheritHandle;
        }

        [SupportedOSPlatform("windows")]
        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern IntPtr CreateNamedPipeW(string lpName, uint dwOpenMode, uint dwPipeMode, uint nMaxInstances,
            uint nOutBufferSize, uint nInBufferSize, uint nDefaultTimeOut, ref SECURITY_ATTRIBUTES lpSecurityAttributes);

        [SupportedOSPlatform("windows")]
        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes,
            uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [SupportedOSPlatform("windows")]
        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool WaitNamedPipeW(string lpNamedPipeName, uint nTimeOut);

        [SupportedOSPlatform("windows")]
        [DllImport(Kernel32, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetNamedPipeServerProcessId(IntPtr pipe, out uint serverProcessId);

        [SupportedOSPlatform("windows")]
        [DllImport(Kernel32, SetLastError = true, ExactSpelling = true)]
        public static extern IntPtr OpenProcess(uint dwDesiredAccess, [MarshalAs(UnmanagedType.Bool)] bool bInheritHandle, uint dwProcessId);

        [SupportedOSPlatform("windows")]
        [DllImport(Kernel32, ExactSpelling = true)]
        public static extern IntPtr GetCurrentProcess();

        [SupportedOSPlatform("windows")]
        [DllImport(Kernel32, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool CloseHandle(IntPtr hObject);

        [SupportedOSPlatform("windows")]
        [DllImport(Kernel32, ExactSpelling = true)]
        public static extern IntPtr LocalFree(IntPtr hMem);

        [SupportedOSPlatform("windows")]
        [DllImport(Kernel32, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        public static extern uint GetFinalPathNameByHandleW(IntPtr hFile, [Out] char[] lpszFilePath, uint cchFilePath, uint dwFlags);

        [SupportedOSPlatform("windows")]
        [DllImport(Advapi32, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool OpenProcessToken(IntPtr processHandle, uint desiredAccess, out IntPtr tokenHandle);

        [SupportedOSPlatform("windows")]
        [DllImport(Advapi32, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetTokenInformation(IntPtr tokenHandle, int tokenInformationClass, IntPtr tokenInformation,
            uint tokenInformationLength, out uint returnLength);

        [SupportedOSPlatform("windows")]
        [DllImport(Advapi32, SetLastError = true, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ConvertSidToStringSidW(IntPtr sid, out IntPtr stringSid);

        [SupportedOSPlatform("windows")]
        [DllImport(Advapi32, SetLastError = true, CharSet = CharSet.Unicode, ExactSpelling = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string stringSecurityDescriptor,
            uint stringSdRevision, out IntPtr securityDescriptor, out uint securityDescriptorSize);

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [DllImport("libc", ExactSpelling = true)]
        public static extern uint getuid();

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [DllImport("libc", ExactSpelling = true)]
        public static extern IntPtr realpath(byte[] path, IntPtr resolved);

        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        [DllImport("libc", ExactSpelling = true)]
        public static extern void free(IntPtr ptr);

        /// <summary>
        /// The user SID (eg. <c>S-1-5-21-...</c>) and the integrity-level SID (eg. <c>S-1-16-8192</c> medium,
        /// <c>S-1-16-12288</c> high) of the given process's token. Any failure throws.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static (string UserSid, string IntegritySid) TokenSids(IntPtr process)
        {
            if (!OpenProcessToken(process, TOKEN_QUERY, out var token)) {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcessToken failed");
            }

            try {
                // both TOKEN_USER and TOKEN_MANDATORY_LABEL start with a SID_AND_ATTRIBUTES whose first field is the PSID
                return (SidToString(TokenInformation(token, TokenUser)), SidToString(TokenInformation(token, TokenIntegrityLevel)));
            } finally {
                CloseHandle(token);
            }
        }

        [SupportedOSPlatform("windows")]
        private static string SidToString(IntPtr buffer)
        {
            var sid = Marshal.ReadIntPtr(buffer);
            try {
                if (!ConvertSidToStringSidW(sid, out var text)) {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "ConvertSidToStringSid failed");
                }

                try {
                    return Marshal.PtrToStringUni(text) ?? throw new InvalidDataException("empty SID string");
                } finally {
                    LocalFree(text);
                }
            } finally {
                Marshal.FreeHGlobal(buffer);
            }
        }

        /// <summary> Returns an HGlobal buffer (owned by the caller) holding the requested token information. </summary>
        [SupportedOSPlatform("windows")]
        private static IntPtr TokenInformation(IntPtr token, int tokenClass)
        {
            if (GetTokenInformation(token, tokenClass, IntPtr.Zero, 0, out var size)) {
                throw new InvalidDataException("GetTokenInformation returned no data");
            }

            if (Marshal.GetLastWin32Error() != ERROR_INSUFFICIENT_BUFFER || size == 0) {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "GetTokenInformation failed");
            }

            var buffer = Marshal.AllocHGlobal((int) size);
            if (!GetTokenInformation(token, tokenClass, buffer, size, out _)) {
                var error = Marshal.GetLastWin32Error();
                Marshal.FreeHGlobal(buffer);
                throw new Win32Exception(error, "GetTokenInformation failed");
            }

            return buffer;
        }

        /// <summary>
        /// A security descriptor granting access only to the given user SID, labelled at the given integrity level
        /// (<c>D:P(A;;GA;;;{user})S:(ML;;NW;;;{level})</c>). The returned memory is never freed: pipe instances are
        /// created with it for the lifetime of the process.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static IntPtr CreateSecurityDescriptor(string sddl)
        {
            if (!ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl, SDDL_REVISION_1, out var descriptor, out _)) {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "ConvertStringSecurityDescriptorToSecurityDescriptor failed");
            }

            return descriptor;
        }

        /// <summary> Creates one instance of a named pipe (duplex, byte mode, overlapped, local clients only). </summary>
        [SupportedOSPlatform("windows")]
        public static IntPtr CreatePipeInstance(string name, IntPtr securityDescriptor, bool first)
        {
            var sa = new SECURITY_ATTRIBUTES {
                nLength = Marshal.SizeOf<SECURITY_ATTRIBUTES>(),
                lpSecurityDescriptor = securityDescriptor,
                bInheritHandle = 0,
            };
            var openMode = PIPE_ACCESS_DUPLEX | FILE_FLAG_OVERLAPPED | (first ? FILE_FLAG_FIRST_PIPE_INSTANCE : 0);
            var handle = CreateNamedPipeW(
                name,
                openMode,
                PIPE_TYPE_BYTE | PIPE_READMODE_BYTE | PIPE_WAIT | PIPE_REJECT_REMOTE_CLIENTS,
                PIPE_UNLIMITED_INSTANCES,
                PIPE_BUFFER_SIZE,
                PIPE_BUFFER_SIZE,
                0,
                ref sa);
            if (handle == INVALID_HANDLE_VALUE) {
                throw new Win32Exception(Marshal.GetLastWin32Error(), "CreateNamedPipe failed");
            }

            return handle;
        }

        /// <summary>
        /// The final (normalized) path of an existing directory, as the file system reports it (<c>\\?\C:\...</c>,
        /// resolving 8.3 names, junctions/symlinks, mapped drives and on-disk casing), or the input on any failure.
        /// </summary>
        [SupportedOSPlatform("windows")]
        public static string GetFinalPath(string path)
        {
            var handle = CreateFileW(
                path,
                0,
                FILE_SHARE_READ | FILE_SHARE_WRITE | FILE_SHARE_DELETE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_BACKUP_SEMANTICS,
                IntPtr.Zero);
            if (handle == INVALID_HANDLE_VALUE) {
                return path;
            }

            try {
                var buffer = new char[1024];
                var length = GetFinalPathNameByHandleW(handle, buffer, (uint) buffer.Length, 0);
                if (length > buffer.Length) {
                    buffer = new char[length];
                    length = GetFinalPathNameByHandleW(handle, buffer, (uint) buffer.Length, 0);
                }

                return length == 0 || length > buffer.Length ? path : new string(buffer, 0, (int) length);
            } finally {
                CloseHandle(handle);
            }
        }

        /// <summary> <c>realpath(3)</c> of the path, or the input when it cannot be resolved. </summary>
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        public static string RealPath(string path)
        {
#if NET8_0_OR_GREATER
            var bytes = Encoding.UTF8.GetBytes(path + "\0");
            var resolved = realpath(bytes, IntPtr.Zero);
            if (resolved == IntPtr.Zero) {
                return path;
            }

            try {
                return Marshal.PtrToStringUTF8(resolved) ?? path;
            } finally {
                free(resolved);
            }
#else
            return path;
#endif
        }
    }
}
