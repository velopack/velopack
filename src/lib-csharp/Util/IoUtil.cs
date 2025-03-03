using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Logging;

namespace Velopack.Util
{
    internal static class IoUtil
    {
        public static IEnumerable<FileInfo> GetAllFilesRecursively(this DirectoryInfo? rootPath)
        {
            if (rootPath == null) return Enumerable.Empty<FileInfo>();
            return rootPath.EnumerateFiles("*", SearchOption.AllDirectories);
        }

        public static string CalculateFileSHA1(string filePath)
        {
            var bufferSize = 1000000; // 1mb
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize)) {
                return CalculateStreamSHA1(stream);
            }
        }

        public static string CalculateStreamSHA1(Stream file)
        {
            using (var sha1 = SHA1.Create()) {
                return BitConverter.ToString(sha1.ComputeHash(file)).Replace("-", String.Empty);
            }
        }

        /// <inheritdoc cref="CalculateStreamSHA256"/>
        public static string CalculateFileSHA256(string filePath)
        {
            var bufferSize = 1000000; // 1mb
            using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, bufferSize)) {
                return CalculateStreamSHA256(stream);
            }
        }

        /// <summary>
        /// Get SHA256 hash of the specified file and returns the result as a base64 encoded string (with length 44)
        /// </summary>
        public static string CalculateStreamSHA256(Stream file)
        {
            using (var sha256 = SHA256.Create()) {
                return BitConverter.ToString(sha256.ComputeHash(file)).Replace("-", String.Empty);
            }
        }

        public static void MoveFile(string source, string dest, bool overwrite)
        {
#if NET6_0_OR_GREATER
            File.Move(source, dest, overwrite);
#else
            if (!File.Exists(source)) throw new FileNotFoundException("File not found", source);
            if (overwrite) File.Delete(dest);
            File.Move(source, dest);
#endif
        }

        /// <summary>
        /// Repeatedly tries various methods to delete a file system object. Optionally renames the directory first.
        /// Optionally ignores errors.
        /// </summary>
        /// <param name="path">The path of the file system entity to delete.</param>
        /// <param name="throwOnFailure">Whether this function should throw if the delete fails.</param>
        /// <param name="renameFirst">Try to rename this object first before deleting. Can help prevent partial delete of folders.</param>
        /// <param name="logger">Logger for diagnostic messages.</param>
        /// <returns>True if the file system object was deleted, false otherwise.</returns>
        public static bool DeleteFileOrDirectoryHard(string path, bool throwOnFailure = true, bool renameFirst = false, IVelopackLogger? logger = null)
        {
            logger ??= NullVelopackLogger.Instance;
            logger.Debug($"Starting to delete: {path}");

            string? currentExePath = null;
            try {
                currentExePath = Process.GetCurrentProcess().MainModule?.FileName;
            } catch {
                // ... ignore
            }

            try {
                if (File.Exists(path)) {
                    DeleteFsiVeryHard(new FileInfo(path), currentExePath, logger);
                } else if (Directory.Exists(path)) {
                    if (renameFirst) {
                        // if there are locked files in a directory, we will not attempt to delte it
                        var oldPath = path + ".old";
                        Directory.Move(path, oldPath);
                        path = oldPath;
                    }

                    DeleteFsiTree(new DirectoryInfo(path), currentExePath, logger);
                } else {
                    if (throwOnFailure)
                        logger?.Warn($"Cannot delete '{path}' if it does not exist.");
                }

                return true;
            } catch (Exception ex) {
                logger.Error(ex, $"Unable to delete '{path}'");
                if (throwOnFailure)
                    throw;
                return false;
            }
        }

        private static void DeleteFsiTree(FileSystemInfo fileSystemInfo, string? currentExePath, IVelopackLogger logger)
        {
            // if junction / symlink, don't iterate, just delete it.
            if (fileSystemInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) {
                DeleteFsiVeryHard(fileSystemInfo, currentExePath, logger);
                return;
            }

            // recursively delete children
            try {
                if (fileSystemInfo is DirectoryInfo directoryInfo) {
                    foreach (FileSystemInfo childInfo in directoryInfo.GetFileSystemInfos()) {
                        DeleteFsiTree(childInfo, currentExePath, logger);
                    }
                }
            } catch (Exception ex) {
                logger.Warn(ex, $"Unable to traverse children of '{fileSystemInfo.FullName}'");
            }

            // finally, delete myself, we should try this even if deleting children failed
            // because Directory.Delete can also be recursive
            DeleteFsiVeryHard(fileSystemInfo, currentExePath, logger);
        }

        private static void DeleteFsiVeryHard(FileSystemInfo fileSystemInfo, string? currentExePath, IVelopackLogger logger)
        {
            // don't try to delete the running process
            if (currentExePath != null && PathUtil.FullPathEquals(fileSystemInfo.FullName, currentExePath)) {
                return;
            }

            // try to remove "ReadOnly" attributes
            try { fileSystemInfo.Attributes = FileAttributes.Normal; } catch { }

            try { fileSystemInfo.Refresh(); } catch { }

            // use this instead of fsi.Delete() because it is more resilient/aggressive
            Action deleteMe = fileSystemInfo is DirectoryInfo
                ? () => Directory.Delete(fileSystemInfo.FullName, true)
                : () => File.Delete(fileSystemInfo.FullName);

            // retry a few times. if a directory in this tree is open in Windows Explorer,
            // it might be locked for a little while WE cleans up handles
            try {
                Retry(
                    () => {
                        try {
                            deleteMe();
                        } catch (DirectoryNotFoundException) {
                            return; // good!
                        }
                    },
                    retries: 4,
                    retryDelay: 50);
            } catch (Exception ex) {
                logger?.Warn(ex, $"Unable to delete child '{fileSystemInfo.FullName}'");
                throw;
            }
        }

        public static void Retry(this Action block, int retries = 4, int retryDelay = 250, IVelopackLogger? logger = null)
        {
            Retry(
                () => {
                    block();
                    return true;
                },
                retries,
                retryDelay,
                logger);
        }

        public static T Retry<T>(this Func<T> block, int retries = 4, int retryDelay = 250, IVelopackLogger? logger = null)
        {
            while (true) {
                try {
                    T ret = block();
                    return ret;
                } catch (Exception ex) {
                    if (retries == 0) throw;
                    logger?.Warn($"Operation failed ({ex.Message}). Retrying {retries} more times...");
                    retries--;
                    Thread.Sleep(retryDelay);
                }
            }
        }

        public static Task RetryAsync(this Func<Task> block, int retries = 4, int retryDelay = 250, IVelopackLogger? logger = null)
        {
            return RetryAsync(
                async () => {
                    await block().ConfigureAwait(false);
                    return true;
                },
                retries,
                retryDelay,
                logger);
        }

        public static async Task<T> RetryAsync<T>(this Func<Task<T>> block, int retries = 4, int retryDelay = 250, IVelopackLogger? logger = null)
        {
            while (true) {
                try {
                    return await block().ConfigureAwait(false);
                } catch (Exception ex) {
                    if (retries == 0) throw;
                    logger?.Warn($"Operation failed ({ex.Message}). Retrying {retries} more times...");
                    retries--;
                    await Task.Delay(retryDelay).ConfigureAwait(false);
                }
            }
        }
    }
}