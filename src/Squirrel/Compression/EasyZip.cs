#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
using System;
using System.IO.Compression;
using Microsoft.Extensions.Logging;

namespace Squirrel.Compression
{
    internal static class EasyZip
    {
        public static void ExtractZipToDirectory(ILogger logger, string inputFile, string outputDirectory)
        {
            logger.Info($"Extracting '{inputFile}' to '{outputDirectory}' using System.IO.Compression...");
            Utility.DeleteFileOrDirectoryHard(outputDirectory);
            ZipFile.ExtractToDirectory(inputFile, outputDirectory);
        }

        public static void CreateZipFromDirectory(ILogger logger, string outputFile, string directoryToCompress)
        {
            logger.Info($"Compressing '{directoryToCompress}' to '{outputFile}' using System.IO.Compression...");
            ZipFile.CreateFromDirectory(directoryToCompress, outputFile);
        }

        //private static void AddAllFromDirectoryInNestedDir(
        //    IWritableArchive writableArchive,
        //    string filePath, string searchPattern = "*.*", SearchOption searchOption = SearchOption.AllDirectories)
        //{
        //    var di = new DirectoryInfo(filePath);
        //    var parent = di.Parent;

        //    using (writableArchive.PauseEntryRebuilding())
        //    {
        //        foreach (var path in Directory.EnumerateFiles(filePath, searchPattern, searchOption))
        //        {
        //            var fileInfo = new FileInfo(path);
        //            writableArchive.AddEntry(fileInfo.FullName.Substring(parent.FullName.Length), fileInfo.OpenRead(), true, fileInfo.Length,
        //                fileInfo.LastWriteTime);
        //        }
        //    }
        //}
    }
}
