using System;
using System.IO;

namespace Velopack.Util
{
    internal static class TempUtil
    {
        public static string GetDefaultTempBaseDirectory()
        {
            string tempDir;

            if (VelopackRuntimeInfo.IsOSX || VelopackRuntimeInfo.IsLinux) {
                tempDir = "/tmp/velopack";
            } else if (VelopackRuntimeInfo.IsWindows) {
                tempDir = Path.Combine(Path.GetTempPath(), "Velopack");
            } else {
                throw new PlatformNotSupportedException();
            }

            if (Environment.GetEnvironmentVariable("VELOPACK_TEMP") is var squirrlTmp
                && !string.IsNullOrWhiteSpace(squirrlTmp))
                tempDir = squirrlTmp;

            var di = new DirectoryInfo(tempDir);
            if (!di.Exists) di.Create();

            return di.FullName;
        }

        private static string GetNextTempName(string tempDir)
        {
            for (int i = 1; i < 1000; i++) {
                string name = "temp." + i;
                var target = Path.Combine(tempDir, name);

                FileSystemInfo? info = null;
                if (Directory.Exists(target)) info = new DirectoryInfo(target);
                else if (File.Exists(target)) info = new FileInfo(target);

                // this dir/file does not exist, lets use it.
                if (info == null) {
                    return target;
                }

                // this dir/file exists, but it is old, let's re-use it.
                // this shouldn't generally happen, but crashes do exist.
                if (DateTime.UtcNow - info.LastWriteTimeUtc > TimeSpan.FromDays(1)) {
                    if (IoUtil.DeleteFileOrDirectoryHard(target, false, true)) {
                        // the dir/file was deleted successfully.
                        return target;
                    }
                }
            }

            throw new Exception(
                "Unable to find free temp path. Has the temp directory exceeded it's maximum number of items? (1000)");
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
            var path = GetNextTempName(rootTempDir);
            newTempFile = path;
            return Disposable.Create(() => IoUtil.DeleteFileOrDirectoryHard(path, throwOnFailure: false));
        }
    }
}