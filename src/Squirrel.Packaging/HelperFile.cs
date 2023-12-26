using System.ComponentModel;
using System.Diagnostics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

namespace Squirrel.Packaging;

public class HelperFile
{
    private static List<string> _searchPaths = new List<string>();
    protected readonly ILogger Log;

    static HelperFile()
    {
        //        AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "wix");

        //#if DEBUG
        //        AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "build", "publish");
        //        AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "build", "Release", "squirrel", "tools");
        //        AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor");
        //        AddSearchPath(SquirrelRuntimeInfo.BaseDirectory, "..", "..", "..", "vendor", "wix");
        //#endif
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
