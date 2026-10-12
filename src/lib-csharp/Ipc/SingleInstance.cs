using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Ipc
{
    /// <summary> The outcome of <see cref="SingleInstance.Elect"/>. </summary>
    internal enum Election
    {
        /// <summary> Handle the activation (if any) in this process; there is no server, or it could not be reached. </summary>
        Standalone,
        /// <summary> This process holds the endpoint lock and should start the server (<see cref="SingleInstance.Start"/>). </summary>
        Server,
        /// <summary> The activation was delivered to the running instance (acked); this process should exit. </summary>
        Forwarded,
    }

    /// <summary> The endpoint and the lock an elected server holds. </summary>
    internal sealed class SingleInstanceServer
    {
        public IpcEndpoint Endpoint { get; }
        public LockFile Lock { get; }

        public SingleInstanceServer(IpcEndpoint endpoint, LockFile lockFile)
        {
            Endpoint = endpoint;
            Lock = lockFile;
        }
    }

    /// <summary>
    /// <c>SingleInstance</c> association launches: the first instance of an app holds the endpoint lock and serves
    /// association activations over IPC; a later launch carrying <c>--veloapp-fileassoc</c> / <c>--veloapp-urlproto</c>
    /// arguments forwards them to that instance and exits. Association values are never logged (only counts and lengths).
    /// </summary>
    internal static class SingleInstance
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
        public static Election Elect(VelopackLocator locator, List<(string Flag, string Value)> associations, IVelopackLogger log,
            out SingleInstanceServer? server)
        {
            server = null;
            var endpoint = IpcEndpoint.ForApp(locator.AppId!, locator.RootAppDir!, log);
            if (endpoint == null) {
                return Election.Standalone;
            }

            switch (TryLock(endpoint, out var lockFile)) {
            case LockAttempt.Acquired:
                server = new SingleInstanceServer(endpoint, lockFile!);
                return Election.Server;
            case LockAttempt.Failed:
                log.Warn("SingleInstance: cannot use the endpoint lock, handling activations in-process.");
                return Election.Standalone;
            }

            if (associations.Count == 0) {
                return Election.Standalone;
            }

            // NTFS allows file names with unpaired UTF-16 surrogates (recovered verbatim from the raw command line);
            // JSON cannot carry them, so such a value is handled here rather than delivered altered.
            var message = new IpcMessage(IpcProtocol.TypeAssociation) { Items = new List<IpcItem>() };
            foreach (var (flag, value) in associations) {
                try {
                    StrictUtf8.GetByteCount(value);
                } catch (EncoderFallbackException) {
                    log.Info("association value is not valid Unicode, handling in-process");
                    return Election.Standalone;
                }

                message.Items.Add(new IpcItem(flag, value));
            }

            if (StrictUtf8.GetByteCount(IpcJson.Serialize(message)) > IpcProtocol.MaxMessageBytes) {
                log.Warn("SingleInstance: activation is too large to forward, handling it in-process.");
                return Election.Standalone;
            }

            var timer = Stopwatch.StartNew();
            while (true) {
                IpcConnection? connection;
                try {
                    connection = IpcClient.TryConnect(endpoint);
                } catch (Exception ex) {
                    log.Warn($"SingleInstance: cannot connect to the running instance, handling the activation in-process: {ex.Message}");
                    return Election.Standalone;
                }

                if (connection != null) {
                    // never retried after sending: a timeout means a duplicate at worst, never a loss
                    using (connection) {
                        try {
                            var reply = connection.Request(message, IpcProtocol.ExchangeTimeout);
                            if (reply.IsAck) {
                                return Election.Forwarded;
                            }

                            log.Warn(
                                $"SingleInstance: running instance answered '{reply.Type}' ({reply.Message}), handling the activation in-process.");
                        } catch (Exception ex) {
                            log.Warn($"SingleInstance: forwarding failed, handling the activation in-process: {ex.Message}");
                        }
                    }

                    return Election.Standalone;
                }

                if (timer.Elapsed >= IpcProtocol.ConnectWindow) {
                    log.Warn("SingleInstance: no running instance answered, handling the activation in-process.");
                    return Election.Standalone;
                }

                switch (TryLock(endpoint, out lockFile)) {
                case LockAttempt.Acquired:
                    server = new SingleInstanceServer(endpoint, lockFile!);
                    return Election.Server;
                case LockAttempt.Failed:
                    log.Warn("SingleInstance: cannot use the endpoint lock, handling the activation in-process.");
                    return Election.Standalone;
                }

                Thread.Sleep(IpcProtocol.RetryInterval);
            }
        }

        /// <summary>
        /// Starts serving association activations for the rest of the process (the server keeps the lock). Callbacks
        /// run on the connection thread, serialized through one gate, and the <c>ack</c> is sent only after they have
        /// returned. A failure to start simply leaves the process without a server (the lock is released).
        /// </summary>
        public static void Start(SingleInstanceServer server, VelopackApp app, IVelopackLogger log)
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
                log.Info("SingleInstance: serving association activations for this app.");
            } else {
                log.Warn("SingleInstance: could not start the IPC server, later activations start new instances.");
            }
        }

        private static IpcMessage Handle(IpcMessage request, VelopackApp app, object gate, IVelopackLogger log)
        {
            if (request.Type != IpcProtocol.TypeAssociation) {
                return IpcMessage.Error(IpcProtocol.ErrUnknownMessageType).ReplyTo(request);
            }

            var resolved = new List<(string Flag, string Value)>();
            foreach (var item in request.Items ?? new List<IpcItem>()) {
                var flag = VelopackApp.CanonicalAssociationFlag(item.Flag);
                if (flag == null || item.Value.Length == 0 || app.GetAssociationCallback(flag) == null) {
                    return IpcMessage.Error(IpcProtocol.ErrUnhandledAssociation).ReplyTo(request);
                }

                resolved.Add((flag, item.Value));
            }

            lock (gate) {
                foreach (var (flag, value) in resolved) {
                    log.Info($"IPC: association ({flag}), {value.Length} chars");
                    app.InvokeAssociationCallback(log, flag, value);
                }
            }

            return IpcMessage.Ack().ReplyTo(request);
        }
    }
}
