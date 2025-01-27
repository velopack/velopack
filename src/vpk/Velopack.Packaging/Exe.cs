using System.Diagnostics;
using System.Text;
using Velopack.Core;
using Velopack.Packaging.Exceptions;
using Velopack.Util;

namespace Velopack.Packaging;

public static class Exe
{
    public static void AssertSystemBinaryExists(string binaryName, string linuxInstallCmd, string osxInstallCmd)
    {
        try {
            if (VelopackRuntimeInfo.IsWindows) {
                var output = InvokeAndThrowIfNonZero("where", new[] { binaryName }, null);
                if (String.IsNullOrWhiteSpace(output) || !File.Exists(output))
                    throw new ProcessFailedException("", "", "");
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
        using var _1 = TempUtil.GetTempFileName(out var outputFile);
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
            WorkingDirectory = workDir ?? "",
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        if (process == null)
            throw new Exception("Failed to start process");

        process.WaitForExit();

        var stdout = IoUtil.Retry(() => File.ReadAllText(outputFile).Trim(), 10, 1000);
        var result = (process.ExitCode, stdout, "", command);
        ProcessFailedException.ThrowIfNonZero(result);
        return stdout;
    }

    public static void RunHostedCommandNoWait(string command, string workDir = null)
    {
        using var _1 = TempUtil.GetTempFileName(out var outputFile);
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
            WorkingDirectory = workDir ?? "",
            CreateNoWindow = true,
        };

        if (Process.Start(psi) == null)
            throw new Exception("Failed to start process");
    }

    public static string InvokeAndThrowIfNonZero(string exePath, IEnumerable<string> args, string workingDir, IDictionary<string, string> envVar = null)
    {
        var result = InvokeProcess(exePath, args, workingDir, CancellationToken.None, envVar);
        ProcessFailedException.ThrowIfNonZero(result);
        return result.StdOutput;
    }

    public static (int ExitCode, string StdOutput, string StdErr) InvokeProcess(ProcessStartInfo psi, CancellationToken ct)
    {
        var process = new Process();
        process.StartInfo = psi;

        var sOut = new StringBuilder();
        var sErr = new StringBuilder();

        process.OutputDataReceived += (sender, e) => {
            if (e.Data != null) sOut.AppendLine(e.Data);
        };

        process.ErrorDataReceived += (sender, e) => {
            if (e.Data != null) sErr.AppendLine(e.Data);
        };

        if (!process.Start())
            throw new Exception("Failed to start process");

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        while (!ct.IsCancellationRequested) {
            if (process.WaitForExit(500)) break;
        }

        if (ct.IsCancellationRequested && !process.HasExited) {
            process.Kill();
            ct.ThrowIfCancellationRequested();
        }

        // need to call this once more to wait for the streams to finish. if WaitForExit is called with a timeout, the streams will not be fully read.
        process.WaitForExit();

        return (process.ExitCode, sOut.ToString().Trim(), sErr.ToString().Trim());
    }

    public static (int ExitCode, string StdOutput, string StdErr, string Command) InvokeProcess(string fileName, IEnumerable<string> args,
        string workingDirectory, CancellationToken ct = default,
        IDictionary<string, string> envVar = null)
    {
        var psi = CreateProcessStartInfo(fileName, workingDirectory);
        if (envVar != null) {
            foreach (var kvp in envVar) {
                psi.EnvironmentVariables[kvp.Key] = kvp.Value;
            }
        }

        psi.AppendArgumentListSafe(args, out var argString);
        var p = InvokeProcess(psi, ct);
        return (p.ExitCode, p.StdOutput, p.StdErr, $"{fileName} {argString}");
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