using Velopack.Core;
using Velopack.Packaging.Compression;
using Velopack.Packaging.Exceptions;

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

        List<string> args = new() {
            "--patch-from", oldFile,
            "-o", outputFile,
            "--force",
            "--single-thread",
            newFile,
        };

        var windowLog = FIO_highbit64(new FileInfo(oldFile).Length) + 1;
        if (windowLog >= 27) {
            args.Add($"--long={windowLog}");
        }

        if (windowLog > 30) {
            throw new UserInfoException(
                $"The file '{Path.GetFileName(oldFile)}' is too large for delta compression. You can disable delta generation using '--delta none'.");
        }

        if (mode == DeltaMode.BestSize) {
            args.Add("-19");
            args.Add("--zstd=targetLength=4096");
            args.Add("--zstd=chainLog=30");
        }

        Exe.InvokeAndThrowIfNonZero(_zstdExe, args, null);
    }

    public void ApplyPatch(string baseFile, string patchFile, string outputFile)
    {
        List<string> args = new() {
            "--decompress",
            "--patch-from", baseFile,
            "-o", outputFile,
            "--force",
            patchFile,
        };

        var windowLog = FIO_highbit64(new FileInfo(baseFile).Length) + 1;
        if (windowLog >= 27) {
            args.Add($"--long={windowLog}");
        }

        Exe.InvokeAndThrowIfNonZero(_zstdExe, args, null);
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