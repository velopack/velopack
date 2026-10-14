using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Ipc
{
    /// <summary> The outcome of <see cref="ActivationForwarding.Elect"/>. </summary>
    internal enum Election
    {
        /// <summary> Handle the activation (if any) in this process; there is no server, or it could not be reached. </summary>
        Standalone,
        /// <summary> This process holds the endpoint lock and should start the server (<see cref="ActivationForwarding.Start"/>). </summary>
        Server,
        /// <summary> The activation was delivered to the running instance (acked); this process should exit. </summary>
        Forwarded,
    }

    /// <summary> The endpoint and the lock an elected server holds. </summary>
    internal sealed class ActivationServer
    {
        public IpcEndpoint Endpoint { get; }
        public LockFile Lock { get; }

        public ActivationServer(IpcEndpoint endpoint, LockFile lockFile)
        {
            Endpoint = endpoint;
            Lock = lockFile;
        }
    }

    /// <summary>
    /// Activation forwarding (<see cref="VelopackApp.ForwardActivationsToRunningInstance"/>): the first instance of an
    /// app holds the endpoint lock and serves association activations over IPC; a later launch carrying
    /// <c>--veloapp-fileassoc</c> / <c>--veloapp-urlproto</c> arguments forwards them to that instance and exits.
    /// Association values are never logged (only counts and lengths).
    /// </summary>
    internal static class ActivationForwarding
    {
        private static readonly Encoding StrictUtf8 = new UTF8Encoding(false, true);

        private static LockAttempt TryLock(IpcEndpoint endpoint, out LockFile? lockFile)
        {
            lockFile = new LockFile(endpoint.LockPath);
            var attempt = lockFile.TryLockOnce();
            if (attempt != LockAttempt.Acquired) {
                lockFile.Dispose();
                lockFile = null;
            }

            return attempt;
        }

        /// <summary>
        /// Decides this process's role. The endpoint is keyed on the app id, the install root and the current user; the
        /// lock makes the choice race-free. A plain launch (no associations) never forwards. A launch with associations
        /// keeps trying for <see cref="IpcProtocol.ConnectWindow"/> to either reach the server or take over the lock;
        /// when neither happens, or the delivery fails in any way, it handles the activation itself (never losing it,
        /// at the price of a possible duplicate).
        /// </summary>
        public static Election Elect(VelopackLocator locator, List<IpcItem> associations, IVelopackLogger log,
            out ActivationServer? server)
        {
            server = null;
            var endpoint = IpcEndpoint.ForApp(locator.AppId!, locator.RootAppDir!, log);
            if (endpoint == null) {
                return Election.Standalone;
            }

            switch (TryLock(endpoint, out var lockFile)) {
            case LockAttempt.Acquired:
                server = new ActivationServer(endpoint, lockFile!);
                return Election.Server;
            case LockAttempt.Failed:
                log.Warn("Activation forwarding: cannot use the endpoint lock, handling activations in-process.");
                return Election.Standalone;
            }

            if (associations.Count == 0) {
                return Election.Standalone;
            }

            // NTFS allows file names with unpaired UTF-16 surrogates (recovered verbatim from the raw command line);
            // JSON cannot carry them, so such a value is handled here rather than delivered altered.
            foreach (var association in associations) {
                try {
                    StrictUtf8.GetByteCount(association.Value);
                } catch (EncoderFallbackException) {
                    log.Info("association value is not valid Unicode, handling in-process");
                    return Election.Standalone;
                }
            }

            var message = new IpcMessage(IpcProtocol.TypeAssociation) { Items = associations };

            var timer = Stopwatch.StartNew();
            while (true) {
                IpcConnection? connection;
                try {
                    connection = IpcClient.TryConnect(endpoint);
                } catch (Exception ex) {
                    log.Warn("Activation forwarding: cannot connect to the running instance, handling the activation in-process: " +
                             ex.Message);
                    return Election.Standalone;
                }

                if (connection != null) {
                    // never retried after sending: a timeout means a duplicate at worst, never a loss
                    using (connection) {
                        AllowServerForeground(connection, log);
                        try {
                            var reply = connection.Request(message, IpcProtocol.ExchangeTimeout);
                            if (reply.IsAck) {
                                return Election.Forwarded;
                            }

                            log.Warn($"Activation forwarding: running instance answered '{reply.Type}' ({reply.Message}), " +
                                     "handling the activation in-process.");
                        } catch (Exception ex) {
                            log.Warn($"Activation forwarding failed, handling the activation in-process: {ex.Message}");
                        }
                    }

                    return Election.Standalone;
                }

                if (timer.Elapsed >= IpcProtocol.ConnectWindow) {
                    log.Warn("Activation forwarding: no running instance answered, handling the activation in-process.");
                    return Election.Standalone;
                }

                switch (TryLock(endpoint, out lockFile)) {
                case LockAttempt.Acquired:
                    server = new ActivationServer(endpoint, lockFile!);
                    return Election.Server;
                case LockAttempt.Failed:
                    log.Warn("Activation forwarding: cannot use the endpoint lock, handling the activation in-process.");
                    return Election.Standalone;
                }

                Thread.Sleep(IpcProtocol.RetryInterval);
            }
        }

        /// <summary>
        /// The launching process was the one the shell activated: let the (verified) running instance take the
        /// foreground, so its callback can bring its window up. Best effort.
        /// </summary>
        private static void AllowServerForeground(IpcConnection connection, IVelopackLogger log)
        {
            if (!VelopackRuntimeInfo.IsWindows || connection.Stream is not PipeStream pipe) {
                return;
            }

            try {
                if (!IpcNative.GetNamedPipeServerProcessId(pipe.SafePipeHandle, out var pid)
                    || !DefaultProcessImpl.AllowSetForegroundWindow((int) pid)) {
                    log.Debug("Activation forwarding: AllowSetForegroundWindow for the running instance failed.");
                }
            } catch (Exception ex) {
                log.Debug($"Activation forwarding: AllowSetForegroundWindow for the running instance failed: {ex.Message}");
            }
        }

        /// <summary>
        /// Starts serving association activations for the rest of the process (the server keeps the lock). Callbacks
        /// run on the connection thread, serialized through one gate, and the <c>ack</c> is sent only after they have
        /// returned. A failure to start simply leaves the process without a server (the lock is released).
        /// </summary>
        public static void Start(ActivationServer server, VelopackApp app, IVelopackLogger log)
        {
            var gate = new object();
            var started = IpcServer.Start(
                server.Endpoint,
                server.Lock,
                connection => {
                    using (connection) {
                        connection.ServeRequest(request => Handle(request, app, gate, log), log);
                    }
                },
                log);
            if (started) {
                log.Info("Activation forwarding: serving association activations for this app.");
            } else {
                log.Warn("Activation forwarding: could not start the IPC server, later activations start new instances.");
            }
        }

        private static IpcMessage Handle(IpcMessage request, VelopackApp app, object gate, IVelopackLogger log)
        {
            if (request.Type != IpcProtocol.TypeAssociation) {
                return IpcMessage.Error(IpcProtocol.ErrUnknownMessageType).ReplyTo(request);
            }

            var resolved = new List<IpcItem>();
            foreach (var item in request.Items ?? new List<IpcItem>()) {
                var flag = VelopackApp.CanonicalAssociationFlag(item.Flag);
                if (flag == null || item.Value.Length == 0 || app.GetAssociationCallback(flag) == null) {
                    return IpcMessage.Error(IpcProtocol.ErrUnhandledAssociation).ReplyTo(request);
                }

                resolved.Add(new IpcItem(flag, item.Value));
            }

            lock (gate) {
                foreach (var item in resolved) {
                    log.Info($"IPC: association ({item.Flag}), {item.Value.Length} chars");
                    app.InvokeAssociationCallback(log, item.Flag, item.Value);
                }
            }

            return IpcMessage.Ack().ReplyTo(request);
        }
    }
}
