using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Threading;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Ipc
{
    /// <summary>
    /// The accepting side. <see cref="Start"/> binds synchronously (so a failure is reported) and then accepts
    /// connections for the rest of the process on a background <c>Velopack IPC</c> thread, which hands every
    /// connection to the callback on the thread pool. The next server instance is created before the current one is
    /// handed out, so the address never disappears. There is no stop API.
    /// </summary>
    internal static class IpcServer
    {
        // a started server owns its lock for the rest of the process: the caller need not keep a reference, and a
        // finalized LockFile would release the lock while the server is still accepting
        private static readonly List<LockFile> HeldLocks = new();

        /// <summary>
        /// Returns false (with a logged warning) when the endpoint could not be bound, eg. because a pipe of that name
        /// created by another user denies us a new instance; <paramref name="lockFile"/> is then disposed.
        /// </summary>
        public static bool Start(IpcEndpoint endpoint, LockFile? lockFile, Action<IpcConnection> onConnection, IVelopackLogger log)
        {
            try {
                Func<NamedPipeServerStream> create;
                if (VelopackRuntimeInfo.IsWindows) {
                    create = WindowsInstanceFactory(endpoint);
                } else {
                    // a socket file left by a crashed server; only the lock holder (or a caller-chosen key) gets here
                    File.Delete(endpoint.Address);
                    create = () => new NamedPipeServerStream(endpoint.Address, PipeDirection.InOut,
                        NamedPipeServerStream.MaxAllowedServerInstances, PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous | IpcClient.CurrentUserOnly);
                }

                var waiting = create();
                if (lockFile != null) {
                    lock (HeldLocks) HeldLocks.Add(lockFile);
                }

                var thread = new Thread(() => AcceptLoop(waiting, create, onConnection, log)) {
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

        private static void AcceptLoop(NamedPipeServerStream? waiting, Func<NamedPipeServerStream> create,
            Action<IpcConnection> onConnection, IVelopackLogger log)
        {
            while (true) {
                NamedPipeServerStream current;
                try {
                    current = waiting ?? create();
                    waiting = null;
                } catch (Exception ex) {
                    log.Warn($"IPC: could not create a server instance: {ex.Message}");
                    Thread.Sleep(IpcProtocol.RetryInterval);
                    continue;
                }

                try {
                    current.WaitForConnection();
                } catch (Exception ex) {
                    // eg. the client connected and went away again; this instance is spent
                    log.Debug($"IPC: WaitForConnection failed, recreating the instance: {ex.Message}");
                    current.Dispose();
                    continue;
                }

                try {
                    waiting = create();
                } catch (Exception ex) {
                    log.Warn($"IPC: could not create the next server instance: {ex.Message}");
                }

                var connection = new IpcConnection(current);
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
        /// The descriptor of every pipe the Windows server creates (the same as the Rust server's
        /// <c>O:{user}D:P(A;;GA;;;{user})</c>): owned by the current user, which is what clients verify (an elevated process
        /// would otherwise default to the Administrators group), and access granted to that user only, so elevated and
        /// non-elevated instances of the same user can talk in both directions. Remote clients are rejected by the pipe mode.
        /// </summary>
        [SupportedOSPlatform("windows")]
        internal static PipeSecurity CreatePipeSecurity()
        {
            var user = IpcEndpoint.CurrentUserSid();
            var security = new PipeSecurity();
            security.SetOwner(user);
            security.SetAccessRuleProtection(true, false);
            security.AddAccessRule(new PipeAccessRule(user, PipeAccessRights.FullControl, AccessControlType.Allow));
            return security;
        }

        /// <summary> The first instance is created with <c>FILE_FLAG_FIRST_PIPE_INSTANCE</c>, so a squatted name fails the start. </summary>
        [SupportedOSPlatform("windows")]
        private static Func<NamedPipeServerStream> WindowsInstanceFactory(IpcEndpoint endpoint)
        {
            var descriptor = CreatePipeSecurity().GetSecurityDescriptorBinaryForm();
            var first = true;
            return () => {
                var stream = IpcNative.CreatePipeServer(endpoint.PipeName, descriptor, first);
                first = false;
                return stream;
            };
        }
    }
}
