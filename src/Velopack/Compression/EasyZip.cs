using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace Velopack.Compression
{
    internal static class EasyZip
    {
        public static void ExtractZipToDirectory(ILogger logger, string inputFile, string outputDirectory)
        {
            logger.Debug($"Extracting '{inputFile}' to '{outputDirectory}' using System.IO.Compression...");
            Utility.DeleteFileOrDirectoryHard(outputDirectory);
            ZipFile.ExtractToDirectory(inputFile, outputDirectory);
        }

        public static void CreateZipFromDirectory(ILogger logger, string outputFile, string directoryToCompress, Action<int>? progress = null,
            CompressionLevel compressionLevel = CompressionLevel.Optimal)
        {
            progress ??= (x => { });
            logger.Debug($"Compressing '{directoryToCompress}' to '{outputFile}' using System.IO.Compression...");

            // we have stopped using ZipFile so we can add async and determinism.
            // ZipFile.CreateFromDirectory(directoryToCompress, outputFile);
            try {
                DeterministicCreateFromDirectory(directoryToCompress, outputFile, compressionLevel, false, Encoding.UTF8, progress);
            } catch {
                try { File.Delete(outputFile); } catch { }
                throw;
            }
        }

        public static async Task CreateZipFromDirectoryAsync(ILogger logger, string outputFile, string directoryToCompress, Action<int>? progress = null,
            CompressionLevel compressionLevel = CompressionLevel.Optimal, CancellationToken cancelToken = default)
        {
            progress ??= (x => { });
            logger.Debug($"Compressing '{directoryToCompress}' to '{outputFile}' using System.IO.Compression...");

            // we have stopped using ZipFile so we can add async and determinism.
            // ZipFile.CreateFromDirectory(directoryToCompress, outputFile);
            try {
                await DeterministicCreateFromDirectoryAsync(directoryToCompress, outputFile, compressionLevel, false, Encoding.UTF8, progress, cancelToken).ConfigureAwait(false);
            } catch {
                try { File.Delete(outputFile); } catch { }
                throw;
            }
        }

        private static char s_pathSeperator = '/';
        private static readonly DateTime ZipFormatMinDate = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static void DeterministicCreateFromDirectory(string sourceDirectoryName, string destinationArchiveFileName, CompressionLevel compressionLevel,
            bool includeBaseDirectory, Encoding entryNameEncoding, Action<int> progress)
        {
            sourceDirectoryName = Path.GetFullPath(sourceDirectoryName);
            destinationArchiveFileName = Path.GetFullPath(destinationArchiveFileName);
            using ZipArchive zipArchive = ZipFile.Open(destinationArchiveFileName, ZipArchiveMode.Create, entryNameEncoding);
            bool flag = true;
            DirectoryInfo directoryInfo = new DirectoryInfo(sourceDirectoryName);
            string fullName = directoryInfo.FullName;
            if (includeBaseDirectory && directoryInfo.Parent != null) {
                fullName = directoryInfo.Parent.FullName;
            }

            var files = directoryInfo
                .EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
                .OrderBy(f => f.FullName)
                .ToArray();

            for (var i = 0; i < files.Length; i++) {
                var item = files[i];
                flag = false;
                int length = item.FullName.Length - fullName.Length;
                string text = EntryFromPath(item.FullName, fullName.Length, length);

                if (item is FileInfo) {
                    var sourceFileName = item.FullName;
                    var entryName = text;
                    using Stream stream = File.Open(sourceFileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    ZipArchiveEntry zipArchiveEntry = zipArchive.CreateEntry(entryName, compressionLevel);
                    zipArchiveEntry.LastWriteTime = ZipFormatMinDate;
                    using (Stream destination2 = zipArchiveEntry.Open()) {
                        stream.CopyTo(destination2);
                    }
                } else if (item is DirectoryInfo possiblyEmptyDir && IsDirEmpty(possiblyEmptyDir)) {
                    var entry = zipArchive.CreateEntry(text + s_pathSeperator);
                    entry.LastWriteTime = ZipFormatMinDate;
                }

                progress((int) ((double) i / files.Length * 100));
            }

            if (includeBaseDirectory && flag) {
                string text = EntryFromPath(directoryInfo.Name, 0, directoryInfo.Name.Length);
                var entry = zipArchive.CreateEntry(text + s_pathSeperator);
                entry.LastWriteTime = ZipFormatMinDate;
            }
        }

        private static async Task DeterministicCreateFromDirectoryAsync(string sourceDirectoryName, string destinationArchiveFileName, CompressionLevel compressionLevel,
            bool includeBaseDirectory, Encoding entryNameEncoding, Action<int> progress, CancellationToken cancelToken)
        {
            sourceDirectoryName = Path.GetFullPath(sourceDirectoryName);
            destinationArchiveFileName = Path.GetFullPath(destinationArchiveFileName);
            using ZipArchive zipArchive = ZipFile.Open(destinationArchiveFileName, ZipArchiveMode.Create, entryNameEncoding);
            bool flag = true;
            DirectoryInfo directoryInfo = new DirectoryInfo(sourceDirectoryName);
            string fullName = directoryInfo.FullName;
            if (includeBaseDirectory && directoryInfo.Parent != null) {
                fullName = directoryInfo.Parent.FullName;
            }

            var files = directoryInfo
                .EnumerateFileSystemInfos("*", SearchOption.AllDirectories)
                .OrderBy(f => f.FullName)
                .ToArray();

            for (var i = 0; i < files.Length; i++) {
                cancelToken.ThrowIfCancellationRequested();
                var item = files[i];
                flag = false;
                int length = item.FullName.Length - fullName.Length;
                string text = EntryFromPath(item.FullName, fullName.Length, length);

                if (item is FileInfo) {
                    var sourceFileName = item.FullName;
                    var entryName = text;
                    using Stream stream = File.Open(sourceFileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    ZipArchiveEntry zipArchiveEntry = zipArchive.CreateEntry(entryName, compressionLevel);
                    zipArchiveEntry.LastWriteTime = ZipFormatMinDate;
                    using (Stream destination2 = zipArchiveEntry.Open()) {
                        await stream.CopyToAsync(destination2, 81920, cancelToken).ConfigureAwait(false);
                    }
                } else if (item is DirectoryInfo possiblyEmptyDir && IsDirEmpty(possiblyEmptyDir)) {
                    var entry = zipArchive.CreateEntry(text + s_pathSeperator);
                    entry.LastWriteTime = ZipFormatMinDate;
                }

                progress((int) ((double) i / files.Length * 100));
            }

            cancelToken.ThrowIfCancellationRequested();
            if (includeBaseDirectory && flag) {
                string text = EntryFromPath(directoryInfo.Name, 0, directoryInfo.Name.Length);
                var entry = zipArchive.CreateEntry(text + s_pathSeperator);
                entry.LastWriteTime = ZipFormatMinDate;
            }
        }

        private static string EntryFromPath(string entry, int offset, int length)
        {
            while (length > 0 && (entry[offset] == Path.DirectorySeparatorChar || entry[offset] == Path.AltDirectorySeparatorChar)) {
                offset++;
                length--;
            }

            if (length == 0) {
                return string.Empty;
            }

            char[] array = entry.ToCharArray(offset, length);
            for (int i = 0; i < array.Length; i++) {
                if (array[i] == Path.DirectorySeparatorChar || array[i] == Path.AltDirectorySeparatorChar) {
                    array[i] = s_pathSeperator;
                }
            }

            return new string(array);
        }

        private static bool IsDirEmpty(DirectoryInfo possiblyEmptyDir)
        {
            using (IEnumerator<FileSystemInfo> enumerator = possiblyEmptyDir.EnumerateFileSystemInfos("*", SearchOption.AllDirectories).GetEnumerator()) {
                if (enumerator.MoveNext()) {
                    FileSystemInfo current = enumerator.Current;
                    return false;
                }
            }

            return true;
        }
    }
}
