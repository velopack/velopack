using System;
using System.Collections.Concurrent;
using System.IO;
using System.Linq;
using Velopack.Exceptions;
using Velopack.Logging;

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

        // Every allocated entry gets a sidecar "<entry>.lock" file which stays exclusively locked
        // (LockFile) until the entry's disposer runs. CleanupAbandonedTempEntries uses it to tell
        // in-use entries (lock held by a live process) apart from abandoned ones (the owner
        // crashed, so the OS released the lock). The lock lives beside the entry rather than
        // inside it because temp directory contents are packed verbatim into build outputs (pkg
        // payloads, MSI harvests, delta repacks), so a marker file inside the directory would
        // leak into shipped packages.
        private const string LockFileSuffix = ".lock";

        // Entries younger than this are never cleaned up, so a scan cannot race an allocation in
        // another process that has created the entry but not yet locked its sidecar. It is also
        // the only protection for entries made by older library versions (which wrote no sidecar)
        // and for entries whose sidecar lock could not be acquired.
        private static readonly TimeSpan MinimumCleanupAge = TimeSpan.FromDays(1);

        // Entries allocated by this process and not yet disposed. Cleanup must not probe these:
        // POSIX record locks (lockf) do not conflict within the owning process, so a same-process
        // probe would both succeed and - by closing its fd - silently drop the real lock.
        private static readonly ConcurrentDictionary<string, byte> ActiveEntries = new();

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
            return AllocateTempEntry(out newTempDirectory, rootTempDir, createDirectory: true);
        }

        public static IDisposable GetTempFileName(out string newTempFile)
        {
            return GetTempFileName(out newTempFile, GetDefaultTempBaseDirectory());
        }

        public static IDisposable GetTempFileName(out string newTempFile, string rootTempDir)
        {
            // NB: the file itself is intentionally not created here. Several callers hand this
            // path to external tools (zstd, msdelta), P/Invoke, or File.Move that expect it to
            // not exist yet. The random name is what makes allocation collision-free.
            return AllocateTempEntry(out newTempFile, rootTempDir, createDirectory: false);
        }

        private static IDisposable AllocateTempEntry(out string path, string rootTempDir, bool createDirectory)
        {
            for (int i = 0; i < MaxAllocationAttempts; i++) {
                var target = Path.Combine(rootTempDir, "temp." + Guid.NewGuid().ToString("N"));
                if (Directory.Exists(target) || File.Exists(target)) {
                    // A leftover entry with this exact guid, e.g. from a crash. Astronomically
                    // unlikely, but retrying with a fresh guid costs nothing.
                    continue;
                }

                var sidecarLock = TryLockSidecar(target + LockFileSuffix);
                ActiveEntries[target] = 1;
                if (createDirectory) {
                    Directory.CreateDirectory(target);
                }

                path = target;
                return Disposable.Create(() => {
                    IoUtil.DeleteFileOrDirectoryHard(target, throwOnFailure: false);
                    sidecarLock?.Dispose();
                    IoUtil.DeleteFileOrDirectoryHard(target + LockFileSuffix, throwOnFailure: false);
                    ActiveEntries.TryRemove(target, out _);
                });
            }

            throw new Exception($"Unable to allocate a free temp path after {MaxAllocationAttempts} attempts.");
        }

        private static LockFile? TryLockSidecar(string lockPath)
        {
            try {
                var sidecarLock = new LockFile(lockPath);
                sidecarLock.LockAsync(retries: 0).GetAwaiterResult();
                return sidecarLock;
            } catch {
                // The entry still works without its lock; it just falls back to age-based
                // cleanup, the same as entries created by older library versions.
                return null;
            }
        }

        /// <summary>
        /// Deletes abandoned entries from the default velopack temp directory - leftovers from
        /// processes which died before their temp disposer ran. An entry is abandoned when it is
        /// older than <see cref="MinimumCleanupAge"/> and no live process holds its sidecar lock.
        /// Only the C# library writes to this directory, so this is the only cleanup of it: it
        /// runs from <see cref="VelopackApp"/> startup (end-user machines) and from the vpk CLI
        /// entry point (build machines).
        /// </summary>
        public static void CleanupAbandonedTempEntries(IVelopackLogger? logger = null)
        {
            try {
                CleanupAbandonedTempEntries(GetDefaultTempBaseDirectory(), logger);
            } catch (Exception ex) {
                logger?.Warn(ex, "Failed to clean up velopack temp directory.");
            }
        }

        public static void CleanupAbandonedTempEntries(string rootTempDir, IVelopackLogger? logger = null)
        {
            logger ??= NullVelopackLogger.Instance;
            foreach (var entry in Directory.EnumerateFileSystemEntries(rootTempDir, "temp.*").ToArray()) {
                try {
                    CleanupTempEntry(entry, logger);
                } catch (Exception ex) {
                    logger.Warn(ex, $"Failed to clean up temp entry: {entry}");
                }
            }
        }

        private static void CleanupTempEntry(string entry, IVelopackLogger logger)
        {
            if (!Directory.Exists(entry) && !File.Exists(entry)) {
                // already removed earlier in this scan (e.g. a sidecar deleted with its entry).
                // probing it with LockFile would re-create the file, so bail out here.
                return;
            }

            if (entry.EndsWith(LockFileSuffix, StringComparison.Ordinal)) {
                // Sidecar locks are deleted together with the entry they guard; one is only
                // handled on its own here if it was orphaned (its entry is already gone). An
                // active temp file whose caller has not created the file yet also has no on-disk
                // owner, hence the ActiveEntries check.
                var owner = entry.Substring(0, entry.Length - LockFileSuffix.Length);
                if (!ActiveEntries.ContainsKey(owner) && !Directory.Exists(owner) && !File.Exists(owner) && TryProbeAndReleaseLock(entry)) {
                    IoUtil.DeleteFileOrDirectoryHard(entry, throwOnFailure: false, logger: logger);
                }

                return;
            }

            if (ActiveEntries.ContainsKey(entry)) {
                return; // in use by this process
            }

            FileSystemInfo info = Directory.Exists(entry) ? new DirectoryInfo(entry) : new FileInfo(entry);
            if (DateTime.UtcNow - info.LastWriteTimeUtc < MinimumCleanupAge) {
                return;
            }

            var sidecar = entry + LockFileSuffix;
            if (File.Exists(sidecar) && !TryProbeAndReleaseLock(sidecar)) {
                return; // the owning process is still alive and using this entry
            }

            logger.Info("Deleting abandoned temp entry: " + entry);
            IoUtil.DeleteFileOrDirectoryHard(entry, throwOnFailure: false, logger: logger);
            IoUtil.DeleteFileOrDirectoryHard(sidecar, throwOnFailure: false, logger: logger);
        }

        private static bool TryProbeAndReleaseLock(string lockPath)
        {
            // If the lock can be acquired, the owning process is gone. It is released again
            // immediately so the open handle does not block the deletion that follows.
            using var probe = new LockFile(lockPath);
            try {
                probe.LockAsync(retries: 0).GetAwaiterResult();
                return true;
            } catch (AcquireLockFailedException) {
                return false;
            }
        }
    }
}
