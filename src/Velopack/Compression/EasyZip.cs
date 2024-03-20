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
        private const string SYMLINK_EXT = ".__symlink";

        public static void ExtractZipToDirectory(ILogger logger, string inputFile, string outputDirectory)
        {
            logger.Debug($"Extracting '{inputFile}' to '{outputDirectory}' using System.IO.Compression...");
            Utility.DeleteFileOrDirectoryHard(outputDirectory);

            List<ZipArchiveEntry> symlinks = new();
            using (ZipArchive archive = ZipFile.Open(inputFile, ZipArchiveMode.Read)) {
                foreach (ZipArchiveEntry entry in archive.Entries) {
                    if (entry.FullName.EndsWith(SYMLINK_EXT)) {
                        symlinks.Add(entry);
                    } else {
                        entry.ExtractRelativeToDirectory(outputDirectory, true);
                    }
                }

                // process symlinks after, because creating them requires the target to exist
                foreach (var sym in symlinks) {
                    sym.ExtractRelativeToDirectory(outputDirectory, true);
                }
            }
        }

        private static void ExtractRelativeToDirectory(this ZipArchiveEntry source, string destinationDirectoryName, bool overwrite)
        {
            // Note that this will give us a good DirectoryInfo even if destinationDirectoryName exists:
            DirectoryInfo di = Directory.CreateDirectory(destinationDirectoryName);
            string destinationDirectoryFullPath = di.FullName;
            var sep = Path.DirectorySeparatorChar.ToString();
            if (!destinationDirectoryFullPath.EndsWith(sep)) {
                destinationDirectoryFullPath = string.Concat(destinationDirectoryFullPath, sep);
            }

            string fileDestinationPath = Path.GetFullPath(Path.Combine(destinationDirectoryFullPath, SanitizeEntryFilePath(source.FullName)));

            if (!fileDestinationPath.StartsWith(destinationDirectoryFullPath, VelopackRuntimeInfo.PathStringComparison))
                throw new IOException("IO_ExtractingResultsInOutside");

            if (source.FullName.EndsWith(SYMLINK_EXT)) {
                // Handle symlink extraction
                fileDestinationPath = fileDestinationPath.Replace(SYMLINK_EXT, string.Empty);
                Directory.CreateDirectory(Path.GetDirectoryName(fileDestinationPath)!);
                using (var reader = new StreamReader(source.Open())) {
                    var targetPath = reader.ReadToEnd();
                    var absolute = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(fileDestinationPath)!, targetPath));
                    SymbolicLink.Create(fileDestinationPath, absolute, true, true);
                }
                return;
            }

            if (Path.GetFileName(fileDestinationPath).Length == 0) {
                // If it is a directory:
                if (source.Length != 0)
                    throw new IOException("IO_DirectoryNameWithData");

                Directory.CreateDirectory(fileDestinationPath);
            } else {
                // If it is a file:
                // Create containing directory:
                Directory.CreateDirectory(Path.GetDirectoryName(fileDestinationPath)!);
                source.ExtractToFile(fileDestinationPath, overwrite: overwrite);
            }
        }

        internal static string SanitizeEntryFilePath(string entryPath) => entryPath.Replace('\0', '_');

        public static async Task CreateZipFromDirectoryAsync(ILogger logger, string outputFile, string directoryToCompress, Action<int>? progress = null,
            CompressionLevel compressionLevel = CompressionLevel.Optimal, CancellationToken cancelToken = default)
        {
            progress ??= (x => { });
            logger.Debug($"Compressing '{directoryToCompress}' to '{outputFile}' using System.IO.Compression...");

            // we have stopped using ZipFile so we can add async and determinism.
            // ZipFile.CreateFromDirectory(directoryToCompress, outputFile);
            try {
                await DeterministicCreateFromDirectoryAsync(directoryToCompress, outputFile, compressionLevel, progress, cancelToken).ConfigureAwait(false);
            } catch {
                try { File.Delete(outputFile); } catch { }
                throw;
            }
        }

        private static char s_pathSeperator = '/';
        private static readonly DateTime ZipFormatMinDate = new DateTime(1980, 1, 1, 0, 0, 0, DateTimeKind.Utc);

        private static async Task DeterministicCreateFromDirectoryAsync(string sourceDirectoryName, string destinationArchiveFileName, CompressionLevel compressionLevel,
            Action<int> progress, CancellationToken cancelToken)
        {
            Encoding entryNameEncoding = Encoding.UTF8;
            sourceDirectoryName = Path.GetFullPath(sourceDirectoryName);
            destinationArchiveFileName = Path.GetFullPath(destinationArchiveFileName);
            using ZipArchive zipArchive = ZipFile.Open(destinationArchiveFileName, ZipArchiveMode.Create, entryNameEncoding);
            DirectoryInfo directoryInfo = new DirectoryInfo(sourceDirectoryName);
            string fullName = directoryInfo.FullName;

            var directories = directoryInfo
                .EnumerateDirectories("*", SearchOption.AllDirectories)
                .OrderBy(f => f.FullName)
                .ToArray();

            foreach (var dir in directories) {
                cancelToken.ThrowIfCancellationRequested();

                // if dir is a symlink, write it as a file containing path to target
                if (SymbolicLink.Exists(dir.FullName)) {
                    string entryName = EntryFromPath(dir.FullName, fullName.Length, dir.FullName.Length - fullName.Length);
                    string symlinkTarget = SymbolicLink.GetTarget(dir.FullName, relative: true)
                        .Replace(Path.DirectorySeparatorChar, s_pathSeperator) + s_pathSeperator;
                    var entry = zipArchive.CreateEntry(entryName + SYMLINK_EXT);
                    using (var writer = new StreamWriter(entry.Open())) {
                        await writer.WriteAsync(symlinkTarget).ConfigureAwait(false);
                    }
                    continue;
                }

                // if directory is empty, write it as an empty entry ending in s_pathSeperator
                if (IsDirEmpty(dir)) {
                    string entryName = EntryFromPath(dir.FullName, fullName.Length, dir.FullName.Length - fullName.Length);
                    var entry = zipArchive.CreateEntry(entryName + s_pathSeperator);
                    entry.LastWriteTime = ZipFormatMinDate;
                    continue;
                }

                // if none of the above, enumerate files and add them to the archive
                var files = dir
                    .EnumerateFiles("*", SearchOption.TopDirectoryOnly)
                    .OrderBy(f => f.FullName)
                    .ToArray();

                for (var i = 0; i < files.Length; i++) {
                    cancelToken.ThrowIfCancellationRequested();
                    var fileInfo = files[i];
                    int length = fileInfo.FullName.Length - fullName.Length;
                    string entryName = EntryFromPath(fileInfo.FullName, fullName.Length, length);

                    if (SymbolicLink.Exists(fileInfo.FullName)) {
                        // Handle symlink: Store the symlink target instead of its content
                        string symlinkTarget = SymbolicLink.GetTarget(fileInfo.FullName, relative: true)
                                .Replace(Path.DirectorySeparatorChar, s_pathSeperator);
                        var entry = zipArchive.CreateEntry(entryName + SYMLINK_EXT);
                        using (var writer = new StreamWriter(entry.Open())) {
                            await writer.WriteAsync(symlinkTarget).ConfigureAwait(false);
                        }
                        continue;
                    }

                    // Regular file handling
                    var sourceFileName = fileInfo.FullName;
                    using Stream stream = File.Open(sourceFileName, FileMode.Open, FileAccess.Read, FileShare.Read);
                    ZipArchiveEntry zipArchiveEntry = zipArchive.CreateEntry(entryName, compressionLevel);
                    zipArchiveEntry.LastWriteTime = ZipFormatMinDate;
                    using (Stream destination = zipArchiveEntry.Open()) {
                        await stream.CopyToAsync(destination, 81920, cancelToken).ConfigureAwait(false);
                    }

                    progress((int) ((double) i / files.Length * 100));
                }
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
