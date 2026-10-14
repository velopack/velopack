using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using Velopack.Logging;

namespace Velopack.Ipc
{
    /// <summary>
    /// Where an app's IPC server lives: the derived key, the transport address (pipe name / socket path) and the lock
    /// file whose holder is the server. C# and Rust derive byte-identical endpoints from the same inputs (see the golden
    /// vectors in <c>IpcEndpointTests</c>).
    /// </summary>
    internal sealed class IpcEndpoint
    {
        private const string KeyPrefix = "velopack-ipc-v1";
        private const string PipePrefix = @"\\.\pipe\";
        /// <summary> <c>sun_path</c> holds 104 bytes on macOS including the terminator. </summary>
        private const int MaxSocketPathBytes = 103;

        /// <summary> 24 lowercase hex chars derived from app id, install root and user, or a caller-chosen key. </summary>
        public string Key { get; }
        /// <summary> <c>\\.\pipe\velopack-{key}</c> on Windows, <c>{tmp}/velopack-{uid}/{key}.sock</c> elsewhere. </summary>
        public string Address { get; }
        /// <summary> <c>{LocalAppData}\velopack\ipc\{key}.lock</c> on Windows, <c>{tmp}/velopack-{uid}/{key}.lock</c> elsewhere. </summary>
        public string LockPath { get; }

        private IpcEndpoint(string key, string address, string lockPath)
        {
            Key = key;
            Address = address;
            LockPath = lockPath;
        }

        /// <summary> The name <c>NamedPipeClientStream</c> takes: the pipe name on Windows, the absolute socket path elsewhere. </summary>
        public string PipeName => VelopackRuntimeInfo.IsWindows ? Address.Substring(PipePrefix.Length) : Address;

        /// <summary>
        /// The endpoint of the installed app with manifest id <paramref name="appId"/> at install root
        /// <paramref name="root"/> for the current user, or null (with a logged warning) when IPC is unavailable.
        /// </summary>
        public static IpcEndpoint? ForApp(string appId, string root, IVelopackLogger log)
        {
            if (string.IsNullOrEmpty(appId) || string.IsNullOrEmpty(root)) {
                log.Warn("IPC endpoint needs an app id and an install root; IPC disabled.");
                return null;
            }

            try {
                var key = DeriveKey(appId, NormalizedRoot(root), CurrentUser());
                return FromKey(key, log);
            } catch (Exception ex) {
                log.Warn(ex, "IPC endpoint could not be derived; IPC disabled.");
                return null;
            }
        }

        /// <summary>
        /// The endpoint for an arbitrary key (<c>[0-9a-z-]{1,40}</c>), eg. a random one chosen by a parent process and
        /// passed to a child through the environment. Null (with a logged warning) when the location cannot be prepared.
        /// </summary>
        public static IpcEndpoint? FromKey(string key, IVelopackLogger log)
        {
            if (!IsValidKey(key)) {
                log.Warn($"Invalid IPC key '{key}'; IPC disabled.");
                return null;
            }

            try {
                if (VelopackRuntimeInfo.IsWindows) {
                    return WindowsEndpoint(key);
                }

                if (VelopackRuntimeInfo.IsLinux || VelopackRuntimeInfo.IsOSX) {
                    return UnixEndpoint(key);
                }

                log.Warn("IPC is not supported on this OS.");
                return null;
            } catch (Exception ex) {
                log.Warn(ex, "IPC endpoint location could not be prepared; IPC disabled.");
                return null;
            }
        }

        private static bool IsValidKey(string key)
        {
            if (string.IsNullOrEmpty(key) || key.Length > 40) {
                return false;
            }

            foreach (var c in key) {
                if (!((c >= '0' && c <= '9') || (c >= 'a' && c <= 'z') || c == '-')) {
                    return false;
                }
            }

            return true;
        }

        [SupportedOSPlatform("windows")]
        private static IpcEndpoint WindowsEndpoint(string key)
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            if (string.IsNullOrEmpty(localAppData)) {
                throw new InvalidOperationException("LocalAppData unavailable");
            }

            var lockDir = Path.Combine(localAppData, "velopack", "ipc");
            Directory.CreateDirectory(lockDir);
            return new IpcEndpoint(key, PipePrefix + "velopack-" + key, Path.Combine(lockDir, key + ".lock"));
        }

        /// <summary>
        /// Lock and socket live in the same per-user private directory on purpose: macOS purges unused <c>$TMPDIR</c>
        /// entries together, and a server whose files were purged is simply superseded by the next launch.
        /// </summary>
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        private static IpcEndpoint UnixEndpoint(string key)
        {
            var baseDir = SocketBaseDir(Environment.GetEnvironmentVariable("TMPDIR"));
            var dir = Path.Combine(baseDir, "velopack-" + CurrentUser());
            EnsurePrivateDir(dir);
            var address = Path.Combine(dir, key + ".sock");
            var bytes = Encoding.UTF8.GetByteCount(address);
            if (bytes > MaxSocketPathBytes) {
                throw new PathTooLongException($"IPC socket path is too long ({bytes} bytes, max {MaxSocketPathBytes})");
            }

            return new IpcEndpoint(key, address, Path.Combine(dir, key + ".lock"));
        }

        /// <summary>
        /// The directory the per-user socket directory lives in, by the same rule as the Rust library
        /// (<c>ipc::unix::socket_base_dir</c>), so both find each other whatever the environment of either process: on
        /// macOS always the per-user temporary directory (<c>confstr(_CS_DARWIN_USER_TEMP_DIR)</c>; <c>$TMPDIR</c> is
        /// ignored as it may be unset, eg. over ssh or in a launchd job), <c>/tmp</c> if that fails; elsewhere
        /// <paramref name="tmpdir"/> (the value of <c>$TMPDIR</c>) when it is set and not empty, else <c>/tmp</c>.
        /// Trailing slashes are trimmed.
        /// </summary>
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        internal static string SocketBaseDir(string? tmpdir)
        {
            var dir = VelopackRuntimeInfo.IsOSX
                ? IpcNative.DarwinUserTempDir() ?? "/tmp"
                : string.IsNullOrEmpty(tmpdir) ? "/tmp" : tmpdir!;
            return TrimRootUnix(dir);
        }

        /// <summary>
        /// Creates the directory (an existing one is fine: two simultaneous starts both create it), refuses a symlink and
        /// sets mode 0700, which fails for a directory another user owns (unless we are root; the peer uid checks of
        /// <see cref="IpcClient.CurrentUserOnly"/> still refuse a foreign server or client then). Any failure disables IPC.
        /// </summary>
        [SupportedOSPlatform("linux")]
        [SupportedOSPlatform("macos")]
        internal static void EnsurePrivateDir(string dir)
        {
            Directory.CreateDirectory(dir);
            if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0) {
                throw new IOException($"{dir} is a symlink");
            }

            if (IpcNative.chmod(dir, 0x1C0 /* 0700 */) != 0) {
                throw new IOException($"chmod 0700 {dir} failed (errno {Marshal.GetLastWin32Error()})");
            }
        }

        /// <summary> The string SID of the process token user on Windows, the decimal uid elsewhere. </summary>
        private static string CurrentUser()
        {
            if (VelopackRuntimeInfo.IsWindows) {
                return CurrentUserSid().Value;
            }

            if (VelopackRuntimeInfo.IsLinux || VelopackRuntimeInfo.IsOSX) {
                return IpcNative.getuid().ToString();
            }

            throw new PlatformNotSupportedException();
        }

        /// <summary> The SID of the current user, the only principal an IPC pipe server grants access to. </summary>
        [SupportedOSPlatform("windows")]
        internal static SecurityIdentifier CurrentUserSid()
        {
            using var identity = WindowsIdentity.GetCurrent();
            return identity.User ?? throw new InvalidOperationException("the process token has no user SID");
        }

        /// <summary>
        /// The install root as the endpoint hashes it: the final (canonical) path when it resolves, then
        /// <see cref="NormalizeRootWindows"/> / <see cref="TrimRootUnix"/>.
        /// </summary>
        internal static string NormalizedRoot(string root)
        {
            if (VelopackRuntimeInfo.IsWindows) {
                return NormalizeRootWindows(IpcNative.GetFinalPath(root));
            }

            if (VelopackRuntimeInfo.IsLinux || VelopackRuntimeInfo.IsOSX) {
                return TrimRootUnix(IpcNative.RealPath(root));
            }

            throw new PlatformNotSupportedException();
        }

        /// <summary>
        /// The pure part of the Windows root normalization: <c>/</c> to <c>\</c>, the <c>\\?\</c> and <c>\\?\UNC\</c>
        /// prefixes removed, trailing <c>\</c> trimmed, ASCII letters lowercased (non-ASCII is left as the file system
        /// reports it).
        /// </summary>
        internal static string NormalizeRootWindows(string root)
        {
            var s = root.Replace('/', '\\');
            if (s.StartsWith(@"\\?\UNC\", StringComparison.Ordinal)) {
                s = @"\\" + s.Substring(8);
            } else if (s.StartsWith(@"\\?\", StringComparison.Ordinal)) {
                s = s.Substring(4);
            }

            s = s.TrimEnd('\\');
            var chars = s.ToCharArray();
            for (var i = 0; i < chars.Length; i++) {
                if (chars[i] >= 'A' && chars[i] <= 'Z') {
                    chars[i] = (char) (chars[i] + 32);
                }
            }

            return new string(chars);
        }

        /// <summary> The pure part of the Unix root normalization: trailing <c>/</c> trimmed while longer than one character. </summary>
        internal static string TrimRootUnix(string root)
        {
            var s = root;
            while (s.Length > 1 && s[s.Length - 1] == '/') {
                s = s.Substring(0, s.Length - 1);
            }

            return s;
        }

        /// <summary>
        /// The endpoint key: the first 12 bytes of SHA-256 over
        /// <c>"velopack-ipc-v1\0{appId}\0{normalizedRoot}\0{user}"</c> (UTF-8), as lowercase hex.
        /// </summary>
        internal static string DeriveKey(string appId, string normalizedRoot, string user)
        {
            var input = Encoding.UTF8.GetBytes(KeyPrefix + "\0" + appId + "\0" + normalizedRoot + "\0" + user);
            using var sha = SHA256.Create();
            var digest = sha.ComputeHash(input);
            var sb = new StringBuilder(24);
            for (var i = 0; i < 12; i++) {
                sb.Append(digest[i].ToString("x2"));
            }

            return sb.ToString();
        }
    }
}
