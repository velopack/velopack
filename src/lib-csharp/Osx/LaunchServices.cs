using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using Velopack.Logging;

namespace Velopack.Osx
{
    /// <summary>
    /// LaunchServices registration of the macOS association helper bundle(s). LaunchServices never scans the nested
    /// <c>Contents/Helpers/*.app</c> of an app on its own (registering or launching the outer bundle leaves the helper
    /// unknown), so the helper's URL schemes / document types only become bindable through an explicit
    /// <c>LSRegisterURL</c>: at every installed app start, from UpdateMac after an apply, and from the .pkg postinstall.
    /// Mirrors <c>launch_services.rs</c> in the Rust library.
    /// </summary>
    internal static class LaunchServices
    {
        private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
        private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
        private const uint kCFStringEncodingUTF8 = 0x08000100;
        private const int kCFURLPOSIXPathStyle = 0;

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFStringCreateWithBytes(IntPtr alloc, byte[] bytes, IntPtr numBytes, uint encoding, byte isExternalRepresentation);

        [DllImport(CoreFoundation)]
        private static extern IntPtr CFURLCreateWithFileSystemPath(IntPtr alloc, IntPtr path, IntPtr pathStyle, byte isDirectory);

        [DllImport(CoreFoundation)]
        private static extern void CFRelease(IntPtr cf);

        [DllImport(CoreServices)]
        private static extern int LSRegisterURL(IntPtr url, byte update);

        /// <summary>Every <c>Contents/Helpers/*.app</c> under <paramref name="rootAppDir"/> with a <c>Contents/MacOS/AssocHelper</c>, sorted.</summary>
        public static string[] FindAssociationHelpers(string rootAppDir)
        {
            var helpersDir = Path.Combine(rootAppDir, "Contents", "Helpers");
            if (!Directory.Exists(helpersDir)) {
                return Array.Empty<string>();
            }

            return Directory.GetDirectories(helpersDir, "*.app")
                .Where(app => File.Exists(Path.Combine(app, "Contents", "MacOS", "AssocHelper")))
                .OrderBy(app => app, StringComparer.Ordinal)
                .ToArray();
        }

        /// <summary>
        /// Registers the app bundle and every nested association helper bundle with LaunchServices. Does nothing when
        /// the bundle has no helper. Failures (including a missing API) are logged, never thrown.
        /// </summary>
        public static void RegisterAssociationHelpers(string rootAppDir, IVelopackLogger log)
        {
            try {
                var helpers = FindAssociationHelpers(rootAppDir);
                if (helpers.Length == 0) {
                    return;
                }

                log.Info($"Registering {rootAppDir} and {helpers.Length} association helper(s) with LaunchServices.");
                Register(rootAppDir, log);
                foreach (var helper in helpers) {
                    Register(helper, log);
                }
            } catch (Exception ex) {
                log.Warn($"LaunchServices registration of the association helper failed ({ex.GetType().Name}: {ex.Message}).");
            }
        }

        private static void Register(string path, IVelopackLogger log)
        {
            var bytes = Encoding.UTF8.GetBytes(path);
            var cfPath = CFStringCreateWithBytes(IntPtr.Zero, bytes, (IntPtr) bytes.Length, kCFStringEncodingUTF8, 0);
            if (cfPath == IntPtr.Zero) {
                log.Warn($"LSRegisterURL({path}) skipped: the path is not a valid string.");
                return;
            }

            try {
                var url = CFURLCreateWithFileSystemPath(IntPtr.Zero, cfPath, (IntPtr) kCFURLPOSIXPathStyle, 1);
                if (url == IntPtr.Zero) {
                    log.Warn($"LSRegisterURL({path}) skipped: the path is not a valid file URL.");
                    return;
                }

                try {
                    var status = LSRegisterURL(url, 1);
                    if (status != 0) {
                        log.Warn($"LSRegisterURL({path}) failed with OSStatus {status}.");
                    }
                } finally {
                    CFRelease(url);
                }
            } finally {
                CFRelease(cfPath);
            }
        }
    }
}
