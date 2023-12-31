using System;
using System.IO.Compression;
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

        public static void CreateZipFromDirectory(ILogger logger, string outputFile, string directoryToCompress)
        {
            logger.Info($"Compressing '{directoryToCompress}' to '{outputFile}' using System.IO.Compression...");
            ZipFile.CreateFromDirectory(directoryToCompress, outputFile);
        }
    }
}
