using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Extensions.Logging;

namespace Velopack.Packaging;

public enum DeltaMode
{
    None,
    BestSpeed,
    BestSize,
}

public class HelperFile
{
    public string UpdatePath => FindHelperFile("Update.exe");

#if DEBUG
    public string UpdateMacPath => FindHelperFile("update", MachO.IsMachOImage);
#else
    public string UpdateMacPath => FindHelperFile("UpdateMac", MachO.IsMachOImage);
#endif

    private static List<string> _searchPaths = new List<string>();
    protected readonly ILogger Log;

    static HelperFile()
    {
#if !DEBUG
        AddSearchPath(VelopackRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor");
#endif
    }

    public HelperFile(ILogger logger)
    {
        Log = logger;
    }

    public static string FindTestFile(string toFind) => FindHelperFile(toFind, throwWhenNotFound: true);

    public static void AddSearchPath(params string[] pathParts)
    {
        AddSearchPath(Path.Combine(pathParts));
    }

    public static void AddSearchPath(string path)
    {
        if (Directory.Exists(path))
            _searchPaths.Insert(0, path);
    }

    public void CreateZstdPatch(string oldFile, string newFile, string outputFile, DeltaMode mode)
    {
        if (mode == DeltaMode.None)
            throw new ArgumentException("DeltaMode.None is not supported.", nameof(mode));

        List<string> args = new() {
            "--patch-from", oldFile,
            newFile,
            "-o", outputFile,
            "--force",
        };

        if (mode == DeltaMode.BestSize) {
            args.Add("-19");
            args.Add("--single-thread");
            args.Add("--zstd");
            args.Add("targetLength=4096");
            args.Add("--zstd");
            args.Add("chainLog=30");
        }

        var deltaMode = mode switch {
            DeltaMode.None => "none",
            DeltaMode.BestSpeed => "bsdiff",
            DeltaMode.BestSize => "xdelta",
            _ => throw new InvalidEnumArgumentException(nameof(mode), (int) mode, typeof(DeltaMode)),
        };

        string zstdPath;
        if (VelopackRuntimeInfo.IsWindows) {
            zstdPath = FindHelperFile("zstd.exe");
        } else {
            zstdPath = "zstd";
            AssertSystemBinaryExists(zstdPath);
        }

        InvokeAndThrowIfNonZero(zstdPath, args, null);
    }

    public void AssertSystemBinaryExists(string binaryName)
    {
        try {
            if (VelopackRuntimeInfo.IsWindows) {
                var output = InvokeAndThrowIfNonZero("where", new[] { binaryName }, null);
                if (String.IsNullOrWhiteSpace(output) || !File.Exists(output))
                    throw new ProcessFailedException("", "");
            } else {
                InvokeAndThrowIfNonZero("command", new[] { "-v", binaryName }, null);
            }
        } catch (ProcessFailedException) {
            throw new Exception($"Could not find '{binaryName}' on the system, ensure it is installed and on the PATH.");
        }
    }

    // protected static string FindAny(params string[] names)
    // {
    //     var findCommand = SquirrelRuntimeInfo.IsWindows ? "where" : "which";
    //
    //     // first search the usual places
    //     foreach (var n in names) {
    //         var helper = FindHelperFile(n, throwWhenNotFound: false);
    //         if (helper != null)
    //             return helper;
    //     }
    //     
    //     // then see if there is something on the path
    //     foreach (var n in names) {
    //         var result = ProcessUtil.InvokeProcess(findCommand, new[] { n }, null, CancellationToken.None);
    //         if (result.ExitCode == 0) {
    //             return n;
    //         }
    //     }
    //
    //     throw new Exception($"Could not find any of {String.Join(", ", names)}.");
    // }

    protected static string FindHelperFile(string toFind, Func<string, bool> predicate = null, bool throwWhenNotFound = true)
    {
        var baseDirs = new[] {
            AppContext.BaseDirectory,
            Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
            Environment.CurrentDirectory,
        };

        var files = _searchPaths
            .Concat(baseDirs)
            .Where(d => !String.IsNullOrEmpty(d))
            .Distinct()
            .Select(d => Path.Combine(d, toFind))
            .Where(d => File.Exists(d))
            .Select(Path.GetFullPath);

        if (predicate != null)
            files = files.Where(predicate);

        var result = files.FirstOrDefault();
        if (result == null && throwWhenNotFound)
            throw new Exception($"Could not find '{toFind}'.");

        return result;
    }

    protected static string InvokeAndThrowIfNonZero(string exePath, IEnumerable<string> args, string workingDir)
    {
        var result = InvokeProcess(exePath, args, workingDir);
        ProcessFailedException.ThrowIfNonZero(result);
        return result.StdOutput;
    }

    protected static (int ExitCode, string StdOutput) InvokeProcess(ProcessStartInfo psi, CancellationToken ct)
    {
        var pi = Process.Start(psi);
        while (!ct.IsCancellationRequested) {
            if (pi.WaitForExit(500)) break;
        }

        if (ct.IsCancellationRequested && !pi.HasExited) {
            pi.Kill();
            ct.ThrowIfCancellationRequested();
        }

        string output = pi.StandardOutput.ReadToEnd();
        string error = pi.StandardError.ReadToEnd();
        var all = (output ?? "") + Environment.NewLine + (error ?? "");

        return (pi.ExitCode, all.Trim());
    }

    protected static (int ExitCode, string StdOutput, string Command) InvokeProcess(string fileName, IEnumerable<string> args, string workingDirectory, CancellationToken ct = default)
    {
        var psi = CreateProcessStartInfo(fileName, workingDirectory);
        psi.AppendArgumentListSafe(args, out var argString);
        var p = InvokeProcess(psi, ct);
        return (p.ExitCode, p.StdOutput, $"{fileName} {argString}");
    }

    protected static ProcessStartInfo CreateProcessStartInfo(string fileName, string workingDirectory)
    {
        var psi = new ProcessStartInfo(fileName);
        psi.UseShellExecute = false;
        psi.WindowStyle = ProcessWindowStyle.Hidden;
        psi.ErrorDialog = false;
        psi.CreateNoWindow = true;
        psi.RedirectStandardOutput = true;
        psi.RedirectStandardError = true;
        psi.WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory;
        return psi;
    }
}
