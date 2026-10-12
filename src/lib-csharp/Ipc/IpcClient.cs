using System;
using System.ComponentModel;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Win32.SafeHandles;

namespace Velopack.Ipc
{
    /// <summary> The connecting side. </summary>
    internal static class IpcClient
    {
        /// <summary>
        /// Connects to the server at <paramref name="endpoint"/>. Null means nobody is listening (the caller may retry).
        /// Throws when the endpoint is unusable, or (Windows) when access is denied or the server process cannot be
        /// verified as running under the current user and integrity level, in which case nothing has been sent.
        /// </summary>
        public static IpcConnection? TryConnect(IpcEndpoint endpoint)
        {
            if (VelopackRuntimeInfo.IsWindows) {
                return ConnectWindows(endpoint);
            }
#if NET8_0_OR_GREATER
            if (VelopackRuntimeInfo.IsLinux || VelopackRuntimeInfo.IsOSX) {
                return ConnectUnix(endpoint);
            }
#endif
            throw new PlatformNotSupportedException("IPC needs the net8.0+ Velopack build on this OS.");
        }

        [SupportedOSPlatform("windows")]
        private static IpcConnection? ConnectWindows(IpcEndpoint endpoint)
        {
            // SECURITY_IDENTIFICATION: a squatter cannot impersonate us beyond identification
            var handle = IpcNative.CreateFileW(
                endpoint.Address,
                IpcNative.GENERIC_READ | IpcNative.GENERIC_WRITE,
                0,
                IntPtr.Zero,
                IpcNative.OPEN_EXISTING,
                IpcNative.FILE_FLAG_OVERLAPPED | IpcNative.SECURITY_SQOS_PRESENT | IpcNative.SECURITY_IDENTIFICATION,
                IntPtr.Zero);
            if (handle == IpcNative.INVALID_HANDLE_VALUE) {
                var error = Marshal.GetLastWin32Error();
                switch (error) {
                case IpcNative.ERROR_FILE_NOT_FOUND:
                    return null;
                case IpcNative.ERROR_PIPE_BUSY:
                    IpcNative.WaitNamedPipeW(endpoint.Address, 100);
                    return null;
                case IpcNative.ERROR_ACCESS_DENIED:
                    throw new UnauthorizedAccessException("IPC server access denied (another user or integrity level)");
                default:
                    throw new Win32Exception(error, "CreateFile on the IPC pipe failed");
                }
            }

            var safeHandle = new SafePipeHandle(handle, true);
            try {
                VerifyServer(handle);
                return new IpcConnection(new NamedPipeClientStream(PipeDirection.InOut, true, true, safeHandle));
            } catch {
                safeHandle.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Before anything is written, the server process must run under our user SID and integrity level. Any failure
        /// along the way (a squatter from another user typically makes <c>OpenProcess</c> fail) is a mismatch.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static void VerifyServer(IntPtr pipe)
        {
            try {
                if (!IpcNative.GetNamedPipeServerProcessId(pipe, out var pid)) {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "GetNamedPipeServerProcessId failed");
                }

                var process = IpcNative.OpenProcess(IpcNative.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (process == IntPtr.Zero) {
                    throw new Win32Exception(Marshal.GetLastWin32Error(), "OpenProcess failed");
                }

                (string UserSid, string IntegritySid) server;
                try {
                    server = IpcNative.TokenSids(process);
                } finally {
                    IpcNative.CloseHandle(process);
                }

                var own = IpcNative.TokenSids(IpcNative.GetCurrentProcess());
                if (!string.Equals(server.UserSid, own.UserSid, StringComparison.Ordinal)
                    || !string.Equals(server.IntegritySid, own.IntegritySid, StringComparison.Ordinal)) {
                    throw new UnauthorizedAccessException("server user or integrity level differs");
                }
            } catch (Exception ex) {
                throw new UnauthorizedAccessException("IPC server could not be verified: " + ex.Message, ex);
            }
        }

#if NET8_0_OR_GREATER
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static IpcConnection? ConnectUnix(IpcEndpoint endpoint)
        {
            if (!File.Exists(endpoint.Address)) {
                return null;
            }

            var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try {
                socket.Connect(new UnixDomainSocketEndPoint(endpoint.Address));
            } catch (SocketException ex)
                when (ex.SocketErrorCode == SocketError.ConnectionRefused || ex.SocketErrorCode == SocketError.AddressNotAvailable) {
                // a stale file from a crashed server
                socket.Dispose();
                return null;
            } catch {
                socket.Dispose();
                throw;
            }

            return new IpcConnection(new NetworkStream(socket, true), socket);
        }
#endif
    }
}
