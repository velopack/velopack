using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.Versioning;
using System.Security.Principal;

namespace Velopack.Ipc
{
    /// <summary> The connecting side. </summary>
    internal static class IpcClient
    {
        /// <summary> Bounds one connection attempt (a busy pipe is waited for this long); callers retry. </summary>
        private const int ConnectTimeoutMs = 10;
        private const int ErrorSemTimeout = 121;

        /// <summary>
        /// Unix only: the runtime checks the peer's uid on both ends (server and client), so a socket placed by another
        /// user is refused. Not used on Windows, where its owner check compares against the token's default owner (the
        /// Administrators group when elevated); <see cref="VerifyServer"/> does the Windows check instead.
        /// </summary>
#if NETSTANDARD2_0
        internal const PipeOptions CurrentUserOnly = (PipeOptions) 0x20000000;
#elif NETFRAMEWORK
        internal const PipeOptions CurrentUserOnly = PipeOptions.None;
#else
        internal const PipeOptions CurrentUserOnly = PipeOptions.CurrentUserOnly;
#endif

        /// <summary>
        /// Connects to the server at <paramref name="endpoint"/>. Null means nobody is listening (the caller may retry).
        /// Throws when the endpoint is unusable, access is denied, or (Windows) the pipe is not owned by the current
        /// user, in which case nothing has been sent.
        /// </summary>
        public static IpcConnection? TryConnect(IpcEndpoint endpoint)
        {
            // Identification: a squatter could not impersonate us beyond identifying us (a Windows concept)
            var impersonation = VelopackRuntimeInfo.IsWindows ? TokenImpersonationLevel.Identification : TokenImpersonationLevel.None;
            var options = PipeOptions.Asynchronous | (VelopackRuntimeInfo.IsWindows ? PipeOptions.None : CurrentUserOnly);
            var pipe = new NamedPipeClientStream(".", endpoint.PipeName, PipeDirection.InOut, options, impersonation);
            try {
                try {
                    // never 0: that is NMPWAIT_USE_DEFAULT_WAIT, a wait for a busy pipe as long as its creator chose
                    pipe.Connect(ConnectTimeoutMs);
                } catch (Exception ex) when (ex is TimeoutException || (ex is IOException && (ex.HResult & 0xFFFF) == ErrorSemTimeout)) {
                    // no instance exists, or every instance is busy (.NET Framework reports the latter as ERROR_SEM_TIMEOUT)
                    pipe.Dispose();
                    return null;
                }

                if (VelopackRuntimeInfo.IsWindows) {
                    VerifyServer(pipe);
                }

                return new IpcConnection(pipe);
            } catch {
                pipe.Dispose();
                throw;
            }
        }

        /// <summary>
        /// Before anything is written, the pipe must be owned by our user SID (the server sets it explicitly; another
        /// user cannot make our SID the owner). Any failure along the way is a refusal.
        /// </summary>
        [SupportedOSPlatform("windows")]
        private static void VerifyServer(NamedPipeClientStream pipe)
        {
            try {
                var owner = pipe.GetAccessControl().GetOwner(typeof(SecurityIdentifier));
                if (owner == null || !owner.Equals(IpcEndpoint.CurrentUserSid())) {
                    throw new UnauthorizedAccessException("the pipe is owned by another user");
                }
            } catch (Exception ex) {
                throw new UnauthorizedAccessException("IPC server could not be verified: " + ex.Message, ex);
            }
        }
    }
}
