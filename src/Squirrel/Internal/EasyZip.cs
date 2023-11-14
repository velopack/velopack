using System;
using System.Collections.Generic;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
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
            if (Extract7z(inputFile, outputDirectory))
                return;

            Log.Info($"Extracting '{inputFile}' to '{outputDirectory}' using System.IO.Compression...");

            Utility.DeleteFileOrDirectoryHard(outputDirectory);
            ZipFile.ExtractToDirectory(inputFile, outputDirectory);
        }

        public static void CreateZipFromDirectory(string outputFile, string directoryToCompress)
        {
            if (Compress7z(outputFile, directoryToCompress))
                return;

            Log.Info($"Compressing '{directoryToCompress}' to '{outputFile}' using System.IO.Compression...");
            ZipFile.CreateFromDirectory(directoryToCompress, outputFile);
        }

        private static bool Extract7z(string zipFilePath, string outFolder)
        {
#if !NETFRAMEWORK
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;
#endif
            Log.Info($"Extracting '{zipFilePath}' to '{outFolder}' using 7z...");
            try {
                var args = String.Format("x \"{0}\" -tzip -mmt on -aoa -y -o\"{1}\" *", zipFilePath, outFolder);
                var psi = Utility.CreateProcessStartInfo(HelperExe.SevenZipPath, args);
                var result = Utility.InvokeProcessUnsafeAsync(psi, CancellationToken.None)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                if (result.ExitCode != 0) throw new Exception(result.StdOutput);
                return true;
            } catch (Exception ex) {
                Log.Warn("Unable to extract archive with 7z.exe\n" + ex.Message);
                return false;
            }
        }

        private static bool Compress7z(string zipFilePath, string inFolder)
        {
#if !NETFRAMEWORK
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                return false;
#endif
            Log.Info($"Compressing '{inFolder}' to '{zipFilePath}' using 7z...");
            try {
                var args = String.Format("a \"{0}\" -tzip -aoa -y -mmt on *", zipFilePath);
                var psi = Utility.CreateProcessStartInfo(HelperExe.SevenZipPath, args, inFolder);
                var result = Utility.InvokeProcessUnsafeAsync(psi, CancellationToken.None)
                    .ConfigureAwait(false).GetAwaiter().GetResult();
                if (result.ExitCode != 0) throw new Exception(result.StdOutput);
                return true;
            } catch (Exception ex) {
                Log.Warn("Unable to create archive with 7z.exe\n" + ex.Message);
                return false;
            }
        }

        public static bool IsDirectory(this ZipArchiveEntry entry)
        {
            return entry.FullName.EndsWith("/") || entry.FullName.EndsWith("\\") || String.IsNullOrEmpty(entry.Name);
        }
    }
}
