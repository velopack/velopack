using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Runtime.Versioning;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Ipc
{
    /// <summary>
    /// The accepting side. <see cref="Start"/> binds synchronously (so a failure is reported) and then accepts
    /// connections for the rest of the process on a background <c>Velopack IPC</c> thread, which hands every
    /// connection to the callback on the thread pool. There is no stop API.
    /// </summary>
    internal static class IpcServer
    {
        // a started server owns its lock for the rest of the process: the caller need not keep a reference, and a
        // finalized LockFile would release the lock while the server is still accepting
        private static readonly List<LockFile> HeldLocks = new();

        /// <summary>
        /// Returns false (with a logged warning) when the endpoint could not be bound, eg. because another process
        /// already owns the pipe name; <paramref name="lockFile"/> is then disposed.
        /// </summary>
        public static bool Start(IpcEndpoint endpoint, LockFile? lockFile, Action<IpcConnection> onConnection, IVelopackLogger log)
        {
            try {
                Func<IpcConnection> accept;
                if (VelopackRuntimeInfo.IsWindows) {
                    accept = BindWindows(endpoint, log);
                }
#if NET8_0_OR_GREATER
                else if (VelopackRuntimeInfo.IsLinux || VelopackRuntimeInfo.IsOSX) {
                    accept = BindUnix(endpoint);
                }
#endif
                else {
                    throw new PlatformNotSupportedException("IPC needs the net8.0+ Velopack build on this OS.");
                }

                if (lockFile != null) {
                    lock (HeldLocks) HeldLocks.Add(lockFile);
                }

                var thread = new Thread(() => AcceptLoop(accept, onConnection, log)) {
                    IsBackground = true,
                    Name = "Velopack IPC",
                };
                thread.Start();
                return true;
            } catch (Exception ex) {
                log.Warn(ex, "IPC: could not start the server.");
                lockFile?.Dispose();
                return false;
            }
        }

        private static void AcceptLoop(Func<IpcConnection> accept, Action<IpcConnection> onConnection, IVelopackLogger log)
        {
            while (true) {
                IpcConnection connection;
                try {
                    connection = accept();
                } catch (Exception ex) {
                    log.Warn($"IPC: accept failed: {ex.Message}");
                    Thread.Sleep(IpcProtocol.RetryInterval);
                    continue;
                }

                ThreadPool.QueueUserWorkItem(_ => {
                    try {
                        onConnection(connection);
                    } catch (Exception ex) {
                        log.Error(ex, "IPC: unhandled error while serving a connection.");
                    }
                });
            }

            // ReSharper disable once FunctionNeverReturns
        }

        /// <summary>
        /// Creates the first pipe instance (failing if the name is already taken) with a DACL granting access only to
        /// the current user and a mandatory label at the current integrity level (named pipes carry no label by
        /// default, which would let a medium-integrity process push activations into an elevated instance). The
        /// returned accept function always keeps one unconnected instance waiting, so the name never disappears.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static Func<IpcConnection> BindWindows(IpcEndpoint endpoint, IVelopackLogger log)
        {
            var (user, level) = IpcNative.TokenSids(IpcNative.GetCurrentProcess());
            var descriptor = IpcNative.CreateSecurityDescriptor($"D:P(A;;GA;;;{user})S:(ML;;NW;;;{level})");
            NamedPipeServerStream? waiting = WrapServerPipe(IpcNative.CreatePipeInstance(endpoint.Address, descriptor, true));

            return () => {
                while (true) {
                    var current = waiting ?? WrapServerPipe(IpcNative.CreatePipeInstance(endpoint.Address, descriptor, false));
                    waiting = null;
                    try {
                        current.WaitForConnection();
                    } catch (Exception ex) {
                        // eg. the client connected and went away again; this instance is spent
                        log.Debug($"IPC: WaitForConnection failed, recreating the instance: {ex.Message}");
                        current.Dispose();
                        continue;
                    }

                    // the next instance exists before this one is handed out, so the name stays reachable
                    try {
                        waiting = WrapServerPipe(IpcNative.CreatePipeInstance(endpoint.Address, descriptor, false));
                    } catch (Exception ex) {
                        log.Warn($"IPC: could not create the next pipe instance: {ex.Message}");
                    }

                    return new IpcConnection(current);
                }
            };
        }

        [SupportedOSPlatform("windows")]
        private static NamedPipeServerStream WrapServerPipe(IntPtr handle)
        {
            var safeHandle = new SafePipeHandle(handle, true);
            try {
                return new NamedPipeServerStream(PipeDirection.InOut, true, false, safeHandle);
            } catch {
                safeHandle.Dispose();
                throw;
            }
        }

#if NET8_0_OR_GREATER
        /// <summary> Unlinks a stale socket file (only the lock holder binds), binds, and restricts the socket to the owner. </summary>
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static Func<IpcConnection> BindUnix(IpcEndpoint endpoint)
        {
            if (File.Exists(endpoint.Address)) {
                File.Delete(endpoint.Address);
            }

            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            try {
                listener.Bind(new UnixDomainSocketEndPoint(endpoint.Address));
                listener.Listen(16);
                File.SetUnixFileMode(endpoint.Address, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            } catch {
                listener.Dispose();
                throw;
            }

            return () => {
                var socket = listener.Accept();
                return new IpcConnection(new NetworkStream(socket, true), socket);
            };
        }
#endif
    }
}
