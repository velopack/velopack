using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Velopack.Packaging.Exceptions;

namespace Velopack.Packaging
{
    public static class Exe
    {
        public static void AssertSystemBinaryExists(string binaryName)
        {
            try {
                if (VelopackRuntimeInfo.IsWindows) {
                    var output = InvokeAndThrowIfNonZero("where", new[] { binaryName }, null);
                    if (String.IsNullOrWhiteSpace(output) || !File.Exists(output))
                        throw new ProcessFailedException("", "");
                } else if (VelopackRuntimeInfo.IsOSX) {
                    InvokeAndThrowIfNonZero("command", new[] { "-v", binaryName }, null);
                } else if (VelopackRuntimeInfo.IsLinux) {
                    InvokeAndThrowIfNonZero("which", new[] { binaryName }, null);
                } else {
                    throw new PlatformNotSupportedException();
                }
            } catch (ProcessFailedException) {
                throw new Exception($"Could not find '{binaryName}' on the system, ensure it is installed and on the PATH.");
            }
        }

        public static string InvokeAndThrowIfNonZero(string exePath, IEnumerable<string> args, string workingDir, IDictionary<string, string> envVar = null)
        {
            var result = InvokeProcess(exePath, args, workingDir, CancellationToken.None, envVar);
            ProcessFailedException.ThrowIfNonZero(result);
            return result.StdOutput;
        }

        public static (int ExitCode, string StdOutput) InvokeProcess(ProcessStartInfo psi, CancellationToken ct)
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

        public static (int ExitCode, string StdOutput, string Command) InvokeProcess(string fileName, IEnumerable<string> args, string workingDirectory, CancellationToken ct = default, IDictionary<string, string> envVar = null)
        {
            var psi = CreateProcessStartInfo(fileName, workingDirectory);
            if (envVar != null) {
                foreach (var kvp in envVar) {
                    psi.EnvironmentVariables[kvp.Key] = kvp.Value;
                }
            }
            psi.AppendArgumentListSafe(args, out var argString);
            var p = InvokeProcess(psi, ct);
            return (p.ExitCode, p.StdOutput, $"{fileName} {argString}");
        }

        public static ProcessStartInfo CreateProcessStartInfo(string fileName, string workingDirectory)
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
}
