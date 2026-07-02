using System;
using System.IO;
using System.Linq;

namespace Velopack.Util
{
    internal static class TempUtil
    {
        // Temp entries are named "temp.{guid}". The high-entropy random suffix guarantees that
        // concurrent callers - whether threads in one process or separate processes sharing the
        // same temp root - never derive the same candidate path. This replaces the old sequential
        // "temp.N" scheme, where two callers each independently picking "the lowest free slot"
        // could both choose e.g. "temp.1" and collide through a check-then-create TOCTOU window.
        private const int MaxAllocationAttempts = 1000;

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

        public static IDisposable GetTempDirectory(out string newTempDirectory)
        {
            return GetTempDirectory(out newTempDirectory, GetDefaultTempBaseDirectory());
        }

        public static IDisposable GetTempDirectory(out string newTempDirectory, string rootTempDir)
        {
            for (int i = 0; i < MaxAllocationAttempts; i++) {
                var target = GetRandomTempPath(rootTempDir);
                if (Directory.Exists(target) || File.Exists(target)) {
                    // A leftover entry with this exact guid, e.g. from a crash. Astronomically
                    // unlikely, but retrying with a fresh guid costs nothing.
                    continue;
                }

                Directory.CreateDirectory(target);
                newTempDirectory = target;
                return CreateDeleter(target);
            }

            throw new Exception($"Unable to allocate a free temp directory after {MaxAllocationAttempts} attempts.");
        }

        public static IDisposable GetTempFileName(out string newTempFile)
        {
            return GetTempFileName(out newTempFile, GetDefaultTempBaseDirectory());
        }

        public static IDisposable GetTempFileName(out string newTempFile, string rootTempDir)
        {
            for (int i = 0; i < MaxAllocationAttempts; i++) {
                var target = GetRandomTempPath(rootTempDir);
                if (Directory.Exists(target) || File.Exists(target)) {
                    continue;
                }

                // NB: the file itself is intentionally not created here. Several callers hand this
                // path to external tools (zstd, msdelta), P/Invoke, or File.Move that expect it to
                // not exist yet. The random name is what makes allocation collision-free.
                newTempFile = target;
                return CreateDeleter(target);
            }

            throw new Exception($"Unable to allocate a free temp file name after {MaxAllocationAttempts} attempts.");
        }

        private static string GetRandomTempPath(string rootTempDir)
        {
            return Path.Combine(rootTempDir, "temp." + Guid.NewGuid().ToString("N"));
        }

        private static IDisposable CreateDeleter(string path)
        {
            return Disposable.Create(() => IoUtil.DeleteFileOrDirectoryHard(path, throwOnFailure: false));
        }
    }
}
