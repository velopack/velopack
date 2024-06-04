using System.Diagnostics;
using Velopack.Packaging.Exceptions;

namespace Velopack.Packaging;

public static class Exe
{
    public static void AssertSystemBinaryExists(string binaryName, string linuxInstallCmd, string osxInstallCmd)
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
            string recommendedCmd = null;
            if (VelopackRuntimeInfo.IsLinux && !String.IsNullOrEmpty(linuxInstallCmd))
                recommendedCmd = linuxInstallCmd;
            else if (VelopackRuntimeInfo.IsOSX && !String.IsNullOrEmpty(osxInstallCmd))
                recommendedCmd = osxInstallCmd;

            string message = $"Could not find '{binaryName}' binary on the system, ensure it is installed and on the PATH.";
            if (!String.IsNullOrEmpty(recommendedCmd)) {
                message += $" You might be able to install it by running: '{recommendedCmd}'";
            }

            throw new UserInfoException(message);
        }
    }

    public static string RunHostedCommand(string command, string workDir = null)
    {
        using var _1 = Utility.GetTempFileName(out var outputFile);
        File.Create(outputFile).Close();

        var fileName = "cmd.exe";
        var args = $"/S /C \"{command} >> \"{outputFile}\" 2>&1\"";

        if (!VelopackRuntimeInfo.IsWindows) {
            fileName = "/bin/bash";
            string escapedCommand = command.Replace("'", "'\\''");
            args = $"-c '{escapedCommand} >> \"{outputFile}\" 2>&1'";
        }

        var psi = new ProcessStartInfo {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            WorkingDirectory = workDir,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        process.WaitForExit();

        var stdout = Utility.Retry(() => File.ReadAllText(outputFile).Trim(), 10, 1000);
        var result = (process.ExitCode, stdout, command);
        ProcessFailedException.ThrowIfNonZero(result);
        return result.Item2;
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
