using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Squirrel.SimpleSplat;

namespace Squirrel
{
    internal static class EasyZip
    {
        private static IFullLogger Log = SquirrelLocator.CurrentMutable.GetService<ILogManager>().GetLogger(typeof(EasyZip));

        public static void ExtractZipToDirectory(string inputFile, string outputDirectory)
        {
            Log.Info($"Extracting '{inputFile}' to '{outputDirectory}' using System.IO.Compression...");
            Utility.DeleteFileOrDirectoryHard(outputDirectory);
            ZipFile.ExtractToDirectory(inputFile, outputDirectory);
        }

        public static void CreateZipFromDirectory(string outputFile, string directoryToCompress, bool nestDirectory = false)
        {
            if (nestDirectory) {
                throw new NotImplementedException();
                //AddAllFromDirectoryInNestedDir(archive, directoryToCompress);
            } else {
                Log.Info($"Compressing '{directoryToCompress}' to '{outputFile}' using System.IO.Compression...");
                ZipFile.CreateFromDirectory(directoryToCompress, outputFile);
            }
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

        public static bool IsDirectory(this ZipArchiveEntry entry)
        {
            return entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\") || String.IsNullOrEmpty(entry.Name);
        }
    }
}
