using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Velopack.Util
{
    internal static class TempUtil
    {
        // Guards temp-name allocation. xunit runs test collections on parallel threads that all
        // share one temp root, and the "pick the lowest free temp.N" scan below returns a name
        // without creating anything on disk (callers hand the path to external tools that expect
        // it not to exist yet). Two threads could therefore be handed the same name before either
        // materialised it - one would create it as a directory while the other expected a file.
        // The lock makes the scan atomic and the reservation set marks a handed-out name as taken
        // until its disposer runs, so the filesystem alone need not track in-flight allocations.
        private static readonly object AllocationLock = new object();
        private static readonly HashSet<string> ReservedNames = new HashSet<string>(StringComparer.Ordinal);

        public static string GetDefaultTempBaseDirectory()
        {
            string tempDir;

            var velopackTemp = Environment.GetEnvironmentVariable("VELOPACK_TEMP");
            var envTempDir = new[] { "TMPDIR", "TEMP", "TMP" }
                .Select(Environment.GetEnvironmentVariable)
                .FirstOrDefault(x => !string.IsNullOrWhiteSpace(x));

            if (!string.IsNullOrWhiteSpace(velopackTemp)) {
                tempDir = velopackTemp!;
            } else if (!string.IsNullOrWhiteSpace(envTempDir)) {
                tempDir = Path.Combine(envTempDir!, "velopack");
            } else if (VelopackRuntimeInfo.IsWindows) {
                tempDir = Path.Combine(Path.GetTempPath(), "velopack");
            } else if (VelopackRuntimeInfo.IsOSX || VelopackRuntimeInfo.IsLinux) {
                tempDir = "/tmp/velopack";
            } else {
                throw new PlatformNotSupportedException();
            }

            var di = new DirectoryInfo(tempDir);
            if (!di.Exists) di.Create();

            return di.FullName;
        }

        private static string ReserveNextTempName(string tempDir)
        {
            lock (AllocationLock) {
                for (int i = 1; i < 1000; i++) {
                    string name = "temp." + i;
                    var target = Path.Combine(tempDir, name);

                    // already handed out to another caller in this process, not yet disposed.
                    if (ReservedNames.Contains(target)) {
                        continue;
                    }

                    FileSystemInfo? info = null;
                    if (Directory.Exists(target)) info = new DirectoryInfo(target);
                    else if (File.Exists(target)) info = new FileInfo(target);

                    // this dir/file does not exist, lets use it.
                    if (info == null) {
                        ReservedNames.Add(target);
                        return target;
                    }

                    // this dir/file exists, but it is old, let's re-use it.
                    // this shouldn't generally happen, but crashes do exist.
                    if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromDays(1)) {
                        if (IoUtil.DeleteFileOrDirectoryHard(target, false, true)) {
                            // the dir/file was deleted successfully.
                            ReservedNames.Add(target);
                            return target;
                        }
                    }
                }

                throw new Exception(
                    "Unable to find free temp path. Has the temp directory exceeded it's maximum number of items? (1000)");
            }
        }

        public static IDisposable GetTempDirectory(out string newTempDirectory)
        {
            return GetTempDirectory(out newTempDirectory, GetDefaultTempBaseDirectory());
        }

        public static IDisposable GetTempDirectory(out string newTempDirectory, string rootTempDir)
        {
            var disp = GetTempFileName(out newTempDirectory, rootTempDir);
            Directory.CreateDirectory(newTempDirectory);
            return disp;
        }

        public static IDisposable GetTempFileName(out string newTempFile)
        {
            return GetTempFileName(out newTempFile, GetDefaultTempBaseDirectory());
        }

        public static IDisposable GetTempFileName(out string newTempFile, string rootTempDir)
        {
            var path = ReserveNextTempName(rootTempDir);
            newTempFile = path;
            return Disposable.Create(() => {
                IoUtil.DeleteFileOrDirectoryHard(path, throwOnFailure: false);
                lock (AllocationLock) {
                    ReservedNames.Remove(path);
                }
            });
        }
    }
}