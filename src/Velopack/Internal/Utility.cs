using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Contracts;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace Velopack
{
    internal static class Utility
    {
        public const string SpecVersionFileName = "sq.version";

        /// <summary>
        /// Calculates the total percentage of a specific step that should report within a specific range.
        /// <para />
        /// If a step needs to report between 50 -> 75 %, this method should be used as CalculateProgress(percentage, 50, 75). 
        /// </summary>
        /// <param name="percentageOfCurrentStep">The percentage of the current step, a value between 0 and 100.</param>
        /// <param name="stepStartPercentage">The start percentage of the range the current step represents.</param>
        /// <param name="stepEndPercentage">The end percentage of the range the current step represents.</param>
        /// <returns>The calculated percentage that can be reported about the total progress.</returns>
        public static int CalculateProgress(int percentageOfCurrentStep, int stepStartPercentage, int stepEndPercentage)
        {
            // Ensure we are between 0 and 100
            percentageOfCurrentStep = Math.Max(Math.Min(percentageOfCurrentStep, 100), 0);

            var range = stepEndPercentage - stepStartPercentage;
            var singleValue = range / 100d;
            var totalPercentage = (singleValue * percentageOfCurrentStep) + stepStartPercentage;

            return (int) totalPercentage;
        }

        public static Action<int> CreateProgressDelegate(Action<int> rootProgress, int stepStartPercentage, int stepEndPercentage)
        {
            return percentage => {
                rootProgress(CalculateProgress(percentage, stepStartPercentage, stepEndPercentage));
            };
        }

        public static string RemoveByteOrderMarkerIfPresent(string content)
        {
            return string.IsNullOrEmpty(content)
                ? string.Empty
                : RemoveByteOrderMarkerIfPresent(Encoding.UTF8.GetBytes(content));
        }

        public static string RemoveByteOrderMarkerIfPresent(byte[] content)
        {
            byte[] output = { };

            Func<byte[], byte[], bool> matches = (bom, src) => {
                if (src.Length < bom.Length) return false;

                return !bom.Where((chr, index) => src[index] != chr).Any();
            };

            var utf32Be = new byte[] { 0x00, 0x00, 0xFE, 0xFF };
            var utf32Le = new byte[] { 0xFF, 0xFE, 0x00, 0x00 };
            var utf16Be = new byte[] { 0xFE, 0xFF };
            var utf16Le = new byte[] { 0xFF, 0xFE };
            var utf8 = new byte[] { 0xEF, 0xBB, 0xBF };

            if (matches(utf32Be, content)) {
                output = new byte[content.Length - utf32Be.Length];
            } else if (matches(utf32Le, content)) {
                output = new byte[content.Length - utf32Le.Length];
            } else if (matches(utf16Be, content)) {
                output = new byte[content.Length - utf16Be.Length];
            } else if (matches(utf16Le, content)) {
                output = new byte[content.Length - utf16Le.Length];
            } else if (matches(utf8, content)) {
                output = new byte[content.Length - utf8.Length];
            } else {
                output = content;
            }

            if (output.Length > 0) {
                Buffer.BlockCopy(content, content.Length - output.Length, output, 0, output.Length);
            }

            return Encoding.UTF8.GetString(output);
        }

        public static bool TryParseEnumU16<TEnum>(ushort enumValue, out TEnum? retVal)
        {
            retVal = default;
            bool success = Enum.IsDefined(typeof(TEnum), enumValue);
            if (success) {
                retVal = (TEnum) Enum.ToObject(typeof(TEnum), enumValue);
            }

            return success;
        }

        public static bool FullPathEquals(string path1, string path2)
        {
            return NormalizePath(path1).Equals(NormalizePath(path2), VelopackRuntimeInfo.PathStringComparison);
        }

        public static bool PathPartEquals(string part1, string part2)
        {
            return part1.Equals(part2, VelopackRuntimeInfo.PathStringComparison);
        }

        public static bool PathPartStartsWith(string part1, string startsWith)
        {
            return part1.StartsWith(startsWith, VelopackRuntimeInfo.PathStringComparison);
        }

        public static bool PathPartEndsWith(string part1, string endsWith)
        {
            return part1.EndsWith(endsWith, VelopackRuntimeInfo.PathStringComparison);
        }

        public static bool FileHasExtension(string filePath, string extension)
        {
            var ext = Path.GetExtension(filePath);
            if (!extension.StartsWith(".")) extension = "." + extension;
            return PathPartEquals(ext, extension);
        }

        public static string NormalizePath(string path)
        {
            var fullPath = Path.GetFullPath(path);
            var normalized = new Uri(fullPath, UriKind.Absolute).LocalPath;
            return normalized.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        public static bool IsFileInDirectory(string file, string directory)
        {
            var normalizedDir = NormalizePath(directory) + Path.DirectorySeparatorChar;
            var normalizedFile = NormalizePath(file);
            return normalizedFile.StartsWith(normalizedDir, VelopackRuntimeInfo.PathStringComparison);
        }

        public static IEnumerable<FileInfo> GetAllFilesRecursively(this DirectoryInfo rootPath)
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
                return Convert.ToBase64String(sha256.ComputeHash(file));
            }
        }

        public static Sources.IFileDownloader CreateDefaultDownloader()
        {
            return new Sources.HttpClientFileDownloader();
        }

        public static string GetVeloReleaseIndexName(string channel)
        {
            return $"releases.{channel ?? VelopackRuntimeInfo.SystemOs.GetOsShortName()}.json";
        }

        [Obsolete]
        public static string GetReleasesFileName(string channel)
        {
            if (channel == null) {
                // default RELEASES file name for each platform.
                if (VelopackRuntimeInfo.IsOSX) return "RELEASES-osx";
                if (VelopackRuntimeInfo.IsLinux) return "RELEASES-linux";
                return "RELEASES";
            } else {
                // if the channel is an empty string or "win", we use the default RELEASES file name.
                if (String.IsNullOrWhiteSpace(channel) || channel.ToLower() == "win") {
                    return "RELEASES";
                }
                // all other cases the RELEASES file includes the channel name.
                return $"RELEASES-{channel.ToLower()}";
            }
        }

        public static void Retry(this Action block, int retries = 4, int retryDelay = 250, ILogger? logger = null)
        {
            Retry(() => {
                block();
                return true;
            }, retries, retryDelay, logger);
        }

        public static T Retry<T>(this Func<T> block, int retries = 4, int retryDelay = 250, ILogger? logger = null)
        {
            Contract.Requires(retries > 0);

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

        public static Task RetryAsync(this Func<Task> block, int retries = 4, int retryDelay = 250, ILogger? logger = null)
        {
            return RetryAsync(async () => {
                await block().ConfigureAwait(false);
                return true;
            }, retries, retryDelay, logger);
        }

        public static async Task<T> RetryAsync<T>(this Func<Task<T>> block, int retries = 4, int retryDelay = 250, ILogger? logger = null)
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

        public static T GetAwaiterResult<T>(this Task<T> task)
        {
            return task.ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public static void GetAwaiterResult(this Task task)
        {
            task.ConfigureAwait(false).GetAwaiter().GetResult();
        }

        public static Task ForEachAsync<T>(this IEnumerable<T> source, Action<T> body, int degreeOfParallelism = 4)
        {
            return ForEachAsync(source, x => Task.Run(() => body(x)), degreeOfParallelism);
        }

        public static Task ForEachAsync<T>(this IEnumerable<T> source, Func<T, Task> body, int degreeOfParallelism = 4)
        {
            return Task.WhenAll(
                from partition in Partitioner.Create(source).GetPartitions(degreeOfParallelism)
                select Task.Run(async () => {
                    using (partition)
                        while (partition.MoveNext())
                            await body(partition.Current).ConfigureAwait(false);
                }));
        }

        /// <summary>
        /// Escapes file name such that the file name is safe for writing to disk in the packages folder
        /// </summary>
        public static string GetSafeFilename(string fileName)
        {
            string safeFileName = Path.GetFileName(fileName);
            char[] invalidFileNameChars = Path.GetInvalidFileNameChars();

            if (safeFileName.IndexOfAny(invalidFileNameChars) != -1) {
                StringBuilder safeName = new();
                foreach (char ch in safeFileName) {
                    if (Array.IndexOf(invalidFileNameChars, ch) == -1)
                        safeName.Append(ch);
                    else
                        safeName.Append('_');
                }
                safeFileName = safeName.ToString();
            }

            return safeFileName;
        }

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
                    if (DeleteFileOrDirectoryHard(target, false, true)) {
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
            return Disposable.Create(() => DeleteFileOrDirectoryHard(path, throwOnFailure: false));
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
        public static bool DeleteFileOrDirectoryHard(string path, bool throwOnFailure = true, bool renameFirst = false, ILogger? logger = null)
        {
            logger ??= NullLogger.Instance;
            Contract.Requires(!String.IsNullOrEmpty(path));
            logger.Debug($"Starting to delete: {path}");

            try {
                if (File.Exists(path)) {
                    DeleteFsiVeryHard(new FileInfo(path), logger);
                } else if (Directory.Exists(path)) {
                    if (renameFirst) {
                        // if there are locked files in a directory, we will not attempt to delte it
                        var oldPath = path + ".old";
                        Directory.Move(path, oldPath);
                        path = oldPath;
                    }

                    DeleteFsiTree(new DirectoryInfo(path), logger);
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

        private static void DeleteFsiTree(FileSystemInfo fileSystemInfo, ILogger logger)
        {
            // if junction / symlink, don't iterate, just delete it.
            if (fileSystemInfo.Attributes.HasFlag(FileAttributes.ReparsePoint)) {
                DeleteFsiVeryHard(fileSystemInfo, logger);
                return;
            }

            // recursively delete children
            try {
                var directoryInfo = fileSystemInfo as DirectoryInfo;
                if (directoryInfo != null) {
                    foreach (FileSystemInfo childInfo in directoryInfo.GetFileSystemInfos()) {
                        DeleteFsiTree(childInfo, logger);
                    }
                }
            } catch (Exception ex) {
                logger.Warn(ex, $"Unable to traverse children of '{fileSystemInfo.FullName}'");
            }

            // finally, delete myself, we should try this even if deleting children failed
            // because Directory.Delete can also be recursive
            DeleteFsiVeryHard(fileSystemInfo, logger);
        }

        private static void DeleteFsiVeryHard(FileSystemInfo fileSystemInfo, ILogger logger)
        {
            // don't try to delete the running process
            if (FullPathEquals(fileSystemInfo.FullName, VelopackRuntimeInfo.EntryExePath))
                return;

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
                Retry(() => {
                    try {
                        deleteMe();
                    } catch (DirectoryNotFoundException) {
                        return; // good!
                    }
                }, retries: 4, retryDelay: 50);
            } catch (Exception ex) {
                logger?.Warn(ex, $"Unable to delete child '{fileSystemInfo.FullName}'");
                throw;
            }
        }

        //public static string PackageDirectoryForAppDir(string rootAppDirectory)
        //{
        //    return Path.Combine(rootAppDirectory, "packages");
        //}

        //public static string LocalReleaseFileForAppDir(string rootAppDirectory)
        //{
        //    return Path.Combine(PackageDirectoryForAppDir(rootAppDirectory), "RELEASES");
        //}

        //public static IEnumerable<ReleaseEntry> LoadLocalReleases(string localReleaseFile)
        //{
        //    var file = File.OpenRead(localReleaseFile);

        //    // NB: sr disposes file
        //    using (var sr = new StreamReader(file, Encoding.UTF8)) {
        //        return ReleaseEntry.ParseReleaseFile(sr.ReadToEnd());
        //    }
        //}

        //public static ReleaseEntry FindLatestFullVersion(IEnumerable<ReleaseEntry> localReleases, RID compatibleRid)
        //{
        //    return FindCompatibleVersions(localReleases, compatibleRid).FirstOrDefault(f => !f.IsDelta);
        //}

        //public static IEnumerable<ReleaseEntry> FindCompatibleVersions(IEnumerable<ReleaseEntry> localReleases, RID compatibleRid)
        //{
        //    if (!localReleases.Any()) {
        //        return null;
        //    }

        //    if (compatibleRid == null || !compatibleRid.IsValid) {
        //        return localReleases.OrderByDescending(x => x.Version);
        //    }

        //    return localReleases
        //        .Where(r => r.Rid.BaseRID == compatibleRid.BaseRID)
        //        .Where(r => r.Rid.Architecture == compatibleRid.Architecture)
        //        .OrderByDescending(x => x.Version);
        //}

        public static string GetAppUserModelId(string packageId, string exeName)
        {
            return String.Format("com.velopack.{0}.{1}", packageId.Replace(" ", ""),
                exeName.Replace(".exe", "").Replace(" ", ""));
        }

        public static bool IsHttpUrl(string urlOrPath)
        {
            if (!Uri.TryCreate(urlOrPath, UriKind.Absolute, out Uri? uri)) {
                return false;
            }

            return uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps;
        }

        public static Uri AppendPathToUri(Uri uri, string path)
        {
            var builder = new UriBuilder(uri);
            if (!builder.Path.EndsWith("/")) {
                builder.Path += "/";
            }

            builder.Path += path;
            return builder.Uri;
        }

        public static Uri EnsureTrailingSlash(Uri uri)
        {
            return AppendPathToUri(uri, "");
        }

        public static async Task<int> GetExitCodeAsync(this Process p)
        {
#if NET5_0_OR_GREATER
            await p.WaitForExitAsync().ConfigureAwait(false);
            return p.ExitCode;
#else
            var tcs = new TaskCompletionSource<int>();
            var thread = new Thread(() => {
                try {
                    p.WaitForExit();
                    tcs.SetResult(p.ExitCode);
                } catch (Exception ex) {
                    tcs.SetException(ex);
                }
            });
            thread.IsBackground = true;
            thread.Start();
            await tcs.Task.ConfigureAwait(false);
            return p.ExitCode;
#endif
        }

        public static Uri AddQueryParamsToUri(Uri uri, IEnumerable<KeyValuePair<string, string>> newQuery)
        {
            var query = System.Web.HttpUtility.ParseQueryString(uri.Query);

            foreach (var entry in newQuery) {
                query[entry.Key] = entry.Value;
            }

            var builder = new UriBuilder(uri);
            builder.Query = query.ToString();

            return builder.Uri;
        }

        readonly static string[] peExtensions = new[] { ".exe", ".dll", ".node" };

        public static bool FileIsLikelyPEImage(string name)
        {
            var ext = Path.GetExtension(name);
            return peExtensions.Any(x => ext.Equals(x, StringComparison.OrdinalIgnoreCase));
        }

        public static Guid CreateGuidFromHash(string text)
        {
            return CreateGuidFromHash(text, Utility.IsoOidNamespace);
        }

        public static Guid CreateGuidFromHash(byte[] data)
        {
            return CreateGuidFromHash(data, Utility.IsoOidNamespace);
        }

        public static Guid CreateGuidFromHash(string text, Guid namespaceId)
        {
            return CreateGuidFromHash(Encoding.UTF8.GetBytes(text), namespaceId);
        }

        public static Guid CreateGuidFromHash(byte[] nameBytes, Guid namespaceId)
        {
            // convert the namespace UUID to network order (step 3)
            byte[] namespaceBytes = namespaceId.ToByteArray();
            SwapByteOrder(namespaceBytes);

            // comput the hash of the name space ID concatenated with the 
            // name (step 4)
            byte[] hash;
            using (var algorithm = SHA1.Create()) {
                algorithm.TransformBlock(namespaceBytes, 0, namespaceBytes.Length, null, 0);
                algorithm.TransformFinalBlock(nameBytes, 0, nameBytes.Length);
                hash = algorithm.Hash!;
            }

            // most bytes from the hash are copied straight to the bytes of 
            // the new GUID (steps 5-7, 9, 11-12)
            var newGuid = new byte[16];
            Array.Copy(hash, 0, newGuid, 0, 16);

            // set the four most significant bits (bits 12 through 15) of 
            // the time_hi_and_version field to the appropriate 4-bit 
            // version number from Section 4.1.3 (step 8)
            newGuid[6] = (byte) ((newGuid[6] & 0x0F) | (5 << 4));

            // set the two most significant bits (bits 6 and 7) of the 
            // clock_seq_hi_and_reserved to zero and one, respectively 
            // (step 10)
            newGuid[8] = (byte) ((newGuid[8] & 0x3F) | 0x80);

            // convert the resulting UUID to local byte order (step 13)
            SwapByteOrder(newGuid);
            return new Guid(newGuid);
        }

        /// <summary>
        /// The namespace for fully-qualified domain names (from RFC 4122, Appendix C).
        /// </summary>
        public static readonly Guid DnsNamespace = new Guid("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

        /// <summary>
        /// The namespace for URLs (from RFC 4122, Appendix C).
        /// </summary>
        public static readonly Guid UrlNamespace = new Guid("6ba7b811-9dad-11d1-80b4-00c04fd430c8");

        /// <summary>
        /// The namespace for ISO OIDs (from RFC 4122, Appendix C).
        /// </summary>
        public static readonly Guid IsoOidNamespace = new Guid("6ba7b812-9dad-11d1-80b4-00c04fd430c8");

        // Converts a GUID (expressed as a byte array) to/from network order (MSB-first).
        static void SwapByteOrder(byte[] guid)
        {
            SwapBytes(guid, 0, 3);
            SwapBytes(guid, 1, 2);
            SwapBytes(guid, 4, 5);
            SwapBytes(guid, 6, 7);
        }

        static void SwapBytes(byte[] guid, int left, int right)
        {
            byte temp = guid[left];
            guid[left] = guid[right];
            guid[right] = temp;
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

        public static TEnum[] GetEnumValues<TEnum>() where TEnum : struct, Enum
        {
#if NET6_0_OR_GREATER
            return Enum.GetValues<TEnum>();
#else
            return Enum.GetValues(typeof(TEnum)).Cast<TEnum>().ToArray();
#endif
        }
    }
}