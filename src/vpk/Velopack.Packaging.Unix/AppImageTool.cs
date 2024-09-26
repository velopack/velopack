using ICSharpCode.SharpZipLib.Tar;
using Microsoft.Extensions.Logging;
using Velopack.Compression;
using Velopack.Util;

namespace Velopack.Packaging.Unix;

public class AppImageTool
{
    public const string DefaultCompressionAlgorithm = "zstd";
    
    public static void CreateLinuxAppImage(string appDir, string outputFile, RuntimeCpu machine, ILogger logger, string compression)
    {
        compression ??= DefaultCompressionAlgorithm;
        
        string runtime = machine switch {
            RuntimeCpu.x64 => HelperFile.AppImageRuntimeX64,
            RuntimeCpu.arm64 => HelperFile.AppImageRuntimeArm64,
            _ => throw new ArgumentOutOfRangeException(nameof(machine), machine, null)
        };

        string tmpSquashFile = outputFile + ".tmpfs";
        string tmpTarFile = outputFile + ".tmptar";

        try {
            if (VelopackRuntimeInfo.IsWindows) {
                // to workaround a permissions limitation of gensquashfs.exe
                // we need to create a tar archive of the AppDir, setting Linux permissions in the tar header
                // and then use tar2sqfs.exe to create the squashfs filesystem
                logger.Info("Compressing AppDir into tar and setting file permissions");
                using (var outStream = File.Create(tmpTarFile))
                using (var tarArchive = TarArchive.CreateOutputTarArchive(outStream)) {
                    tarArchive.RootPath = Path.GetFullPath(appDir);
                    void AddDirectoryToTar(TarArchive tarArchive, DirectoryInfo dir)
                    {
                        var directories = dir.GetDirectories();
                        foreach (var directory in directories) {
                            AddDirectoryToTar(tarArchive, directory);
                        }

                        var filenames = dir.GetFiles();
                        foreach (var filename in filenames) {
                            var tarEntry = TarEntry.CreateEntryFromFile(filename.FullName);
                            tarEntry.TarHeader.Magic = "ustar";
                            tarEntry.TarHeader.Version = "00";
                            tarEntry.TarHeader.ModTime = EasyZip.ZipFormatMinDate;
                            tarEntry.TarHeader.Mode = Convert.ToInt32("755", 8);
                            tarArchive.WriteEntry(tarEntry, true);
                        }
                    }
                    AddDirectoryToTar(tarArchive, new DirectoryInfo(appDir));
                }

                logger.Info("Converting tar into squashfs filesystem");
                var tool = HelperFile.FindHelperFile("squashfs-tools\\tar2sqfs.exe");
                logger.Debug(Exe.RunHostedCommand($"\"{tool}\" -c {compression} -b 128K \"{tmpSquashFile}\" < \"{tmpTarFile}\""));
            } else {
                Exe.AssertSystemBinaryExists("mksquashfs", "sudo apt install squashfs-tools", "brew install squashfs");
                var tool = "mksquashfs";
                List<string> args =
                [
                    appDir,
                    tmpSquashFile,
                    "-comp",
                    compression,
                    "-root-owned",
                    "-noappend",
                    "-b",
                    "128K",
                    "-mkfs-time",
                    "0",
                ];
                logger.Info("Compressing AppDir into squashfs filesystem");
                logger.Debug(Exe.InvokeAndThrowIfNonZero(tool, args, null));
            }

            logger.Info($"Creating AppImage with {Path.GetFileName(runtime)} runtime");
            File.Copy(runtime, outputFile, true);

            using var outputfs = File.Open(outputFile, FileMode.Append);
            using var squashfs = File.OpenRead(tmpSquashFile);
            squashfs.CopyTo(outputfs);

            Chmod.ChmodFileAsExecutable(outputFile);
        } finally {
            IoUtil.DeleteFileOrDirectoryHard(tmpSquashFile);
            IoUtil.DeleteFileOrDirectoryHard(tmpTarFile);
        }
    }
}
