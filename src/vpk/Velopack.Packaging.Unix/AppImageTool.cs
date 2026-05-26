using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.Packaging.Unix;

public class AppImageTool
{
    public const string DefaultCompressionAlgorithm = "gzip";

    public static void CreateLinuxAppImage(string appDir, string outputFile, RuntimeCpu machine, ILogger logger, string compression)
    {
        compression ??= DefaultCompressionAlgorithm;

        string runtime = machine switch {
            RuntimeCpu.x86 => HelperFile.AppImageRuntimeX86,
            RuntimeCpu.x64 => HelperFile.AppImageRuntimeX64,
            RuntimeCpu.arm64 => HelperFile.AppImageRuntimeArm64,
            _ => throw new ArgumentOutOfRangeException(nameof(machine), machine, null)
        };

        string tmpSquashFile = outputFile + ".tmpfs";

        try {
            if (VelopackRuntimeInfo.IsWindows) {
                logger.Info("Creating squashfs filesystem from AppDir");
                var tool = HelperFile.FindHelperFile("mksquashfs.exe");
                List<string> args = [appDir, tmpSquashFile, "-c", compression];
                if (compression == "xz") {
                    args.AddRange(["-b", "16384"]);
                }
                logger.Debug(Exe.InvokeAndThrowIfNonZero(tool, args, null));
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
                ];

                // If SOURCE_DATE_EPOCH is not set, pin filesystem time to 0 for determinism.
                // When SOURCE_DATE_EPOCH is set, mksquashfs expects to control timestamps via env; do not pass -mkfs-time.
                if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("SOURCE_DATE_EPOCH"))) {
                    args.AddRange([
                        "-mkfs-time",
                        "0"
                    ]);
                }

                // see: https://github.com/AppImage/AppImageKit/blob/e8dadbb09fed3ae3c3d5a5a9ba2c47a072f71c40/src/appimagetool.c#L188-L195
                if (compression == "xz") {
                    args.AddRange([
                        "-Xdict-size",
                        "100%",
                        "-b",
                        "16384"
                    ]);
                }

                logger.Info("Compressing AppDir into squashfs filesystem");
                logger.Debug(Exe.InvokeAndThrowIfNonZero(tool, args, null));
            }

            logger.Info($"Creating AppImage with {Path.GetFileName(runtime)} runtime");
            File.Copy(runtime, outputFile, true);
            // Ensure the copied runtime is writable/executable before appending squashfs.
            // ChmodFileAsExecutable sets 755 which includes write permission.
            Chmod.ChmodFileAsExecutable(outputFile);

            using var outputfs = File.Open(outputFile, FileMode.Append, FileAccess.Write, FileShare.None);
            using var squashfs = File.OpenRead(tmpSquashFile);
            squashfs.CopyTo(outputfs);

            Chmod.ChmodFileAsExecutable(outputFile);
        } finally {
            IoUtil.DeleteFileOrDirectoryHard(tmpSquashFile);
        }
    }
}
