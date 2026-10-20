using Velopack.Core;
using Velopack.Packaging.Compression;
using Velopack.Packaging.Exceptions;
using Velopack.Util;

namespace Velopack.Packaging;

public class Zstd
{
    private readonly string _zstdExe;

    public Zstd(string zstdExe)
    {
        _zstdExe = zstdExe;
    }

    public void CreatePatch(string oldFile, string newFile, string outputFile, DeltaMode mode)
    {
        if (mode == DeltaMode.None)
            throw new ArgumentException("DeltaMode.None is not supported.", nameof(mode));

        var windowLog = FIO_highbit64(new FileInfo(oldFile).Length) + 1;
        if (windowLog > 30) {
            throw new UserInfoException(
                $"The file '{Path.GetFileName(oldFile)}' is too large for delta compression. You can disable delta generation using '--delta none'.");
        }

        Invoke(
            oldFile,
            newFile,
            outputFile,
            (dict, input, output) => {
                List<string> args = new() {
                    "--patch-from", dict,
                    "-o", output,
                    "--force",
                    "--single-thread",
                    input,
                };

                if (windowLog >= 27) {
                    args.Add($"--long={windowLog}");
                }

                if (mode == DeltaMode.BestSize) {
                    args.Add("-19");
                    args.Add("--zstd=targetLength=4096");
                    args.Add("--zstd=chainLog=30");
                }

                return args;
            });
    }

    public void ApplyPatch(string baseFile, string patchFile, string outputFile)
    {
        var windowLog = FIO_highbit64(new FileInfo(baseFile).Length) + 1;

        Invoke(
            baseFile,
            patchFile,
            outputFile,
            (dict, input, output) => {
                List<string> args = new() {
                    "--decompress",
                    "--patch-from", dict,
                    "-o", output,
                    "--force",
                    input,
                };

                if (windowLog >= 27) {
                    args.Add($"--long={windowLog}");
                }

                return args;
            });
    }

    private void Invoke(string dictFile, string inputFile, string outputFile, Func<string, string, string, List<string>> buildArgs)
    {
        // NB: zstd.exe on Windows reads its arguments in the ANSI code page, so any path
        // character it can't represent turns into '?' and the file can't be found
        // (facebook/zstd#3882). In that case the inputs are temporarily moved into an empty
        // directory under ASCII names, and zstd is run from there with relative paths.
        if (!VelopackRuntimeInfo.IsWindows || (IsAscii(dictFile) && IsAscii(inputFile) && IsAscii(outputFile))) {
            Exe.InvokeAndThrowIfNonZero(_zstdExe, buildArgs(dictFile, inputFile, outputFile), null);
            return;
        }

        using var _ = TempUtil.GetTempDirectory(out var stagingDir);
        var stagedDict = Path.Combine(stagingDir, "dict");
        var stagedInput = Path.Combine(stagingDir, "input");
        var stagedOutput = Path.Combine(stagingDir, "output");

        File.Move(dictFile, stagedDict);
        try {
            File.Move(inputFile, stagedInput);
            try {
                Exe.InvokeAndThrowIfNonZero(_zstdExe, buildArgs("dict", "input", "output"), stagingDir);
            } finally {
                File.Move(stagedInput, inputFile);
            }
        } finally {
            File.Move(stagedDict, dictFile);
        }

        File.Move(stagedOutput, outputFile, true);
    }

    private static bool IsAscii(string path)
    {
        return path.All(c => c < 128);
    }

    private int FIO_highbit64(long v)
    {
        int count = 0;
        v >>= 1;
        while (v > 0) {
            v >>= 1;
            count++;
        }

        return count;
    }
}
