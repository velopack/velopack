using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Velopack.Compression
{
    internal static class EasyZip
    {
        public static void ExtractZipToDirectory(ILogger logger, string inputFile, string outputDirectory)
        {
            logger.Info($"Extracting '{inputFile}' to '{outputDirectory}' using System.IO.Compression...");
            Utility.DeleteFileOrDirectoryHard(outputDirectory);
            ZipFile.ExtractToDirectory(inputFile, outputDirectory);
        }

        public static void CreateZipFromDirectory(ILogger logger, string outputFile, string directoryToCompress, bool deterministic = true)
        {
            logger.Info($"Compressing '{directoryToCompress}' to '{outputFile}' using System.IO.Compression...");

            if (deterministic) {
                DeterministicCreateFromDirectory(directoryToCompress, outputFile, null, false, Encoding.UTF8);

            } else {
                ZipFile.CreateFromDirectory(directoryToCompress, outputFile);
            }
        }

        private static char s_pathSeperator = '/';
        private static readonly DateTime ZipFormatMinDate = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static void DeterministicCreateFromDirectory(string sourceDirectoryName, string destinationArchiveFileName, CompressionLevel? compressionLevel, bool includeBaseDirectory, Encoding entryNameEncoding)
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

            foreach (FileSystemInfo item in files) {
                flag = false;
                int length = item.FullName.Length - fullName.Length;
                string text = EntryFromPath(item.FullName, fullName.Length, length);

                if (item is FileInfo) {
                    DoCreateEntryFromFile(zipArchive, item.FullName, text, compressionLevel);
                } else if (item is DirectoryInfo possiblyEmptyDir && IsDirEmpty(possiblyEmptyDir)) {
                    var entry = zipArchive.CreateEntry(text + s_pathSeperator);
                    entry.LastWriteTime = ZipFormatMinDate;
                }
            }

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

        internal static ZipArchiveEntry DoCreateEntryFromFile(ZipArchive destination, string sourceFileName, string entryName, CompressionLevel? compressionLevel)
        {
            if (destination == null) {
                throw new ArgumentNullException("destination");
            }

            if (sourceFileName == null) {
                throw new ArgumentNullException("sourceFileName");
            }

            if (entryName == null) {
                throw new ArgumentNullException("entryName");
            }

            using Stream stream = File.Open(sourceFileName, FileMode.Open, FileAccess.Read, FileShare.Read);
            ZipArchiveEntry zipArchiveEntry = (compressionLevel.HasValue ? destination.CreateEntry(entryName, compressionLevel.Value) : destination.CreateEntry(entryName));

            zipArchiveEntry.LastWriteTime = ZipFormatMinDate;

            using (Stream destination2 = zipArchiveEntry.Open()) {
                stream.CopyTo(destination2);
            }

            return zipArchiveEntry;
        }
    }
}
