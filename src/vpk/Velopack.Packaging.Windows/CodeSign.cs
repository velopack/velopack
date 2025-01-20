using System.Diagnostics;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Security.Extensions;
using Velopack.Core;
using Velopack.Util;

namespace Velopack.Packaging.Windows;

public class CodeSign
{
    public ILogger Log { get; }

    public CodeSign(ILogger logger)
    {
        Log = logger;
    }

    private bool IsTrusted(string filePath)
    {
        using var fileStream = File.OpenRead(filePath);
        var targetPackageSignatureInfo = FileSignatureInfo.GetFromFileStream(fileStream);
        return targetPackageSignatureInfo.State == SignatureState.SignedAndTrusted;
    }

    private bool ShouldSign(string filePath)
    {
        if (String.IsNullOrWhiteSpace(filePath)) return true;

        if (!File.Exists(filePath)) {
            Log.Warn($"Cannot sign '{filePath}', file does not exist.");
            return false;
        }

        try {
            if (VelopackRuntimeInfo.IsWindows && IsTrusted(filePath)) {
                Log.Debug($"'{filePath}' is already signed, skipping...");
                return false;
            }
        } catch (Exception ex) {
            Log.Error(ex, "Failed to determine signing status for " + filePath);
        }

        return true;
    }

    public void Sign(string[] filePaths, string signArguments, int parallelism, Action<int> progress, bool signAsTemplate)
    {
        Queue<string> pendingSign = new Queue<string>();

        foreach (var f in filePaths) {
            if (ShouldSign(f)) {
                pendingSign.Enqueue(Path.GetFullPath(f));
            }
        }

        using var _1 = TempUtil.GetTempFileName(out var signLogFile);
        var totalToSign = pendingSign.Count;

        if (signAsTemplate) {
            if (signArguments.Contains("{{file}}")) {
                Log.Info("Preparing to codesign using a single file signing template, ignoring --signParallel option.");
                parallelism = 1;
            } else if (signArguments.Contains("{{file...}}")) {
                Log.Info($"Preparing to codesign using a single file signing template, with a parallelism of {parallelism}.");
                signArguments = signArguments.Replace("{{file...}}", "{{file}}");
            } else {
                throw new UserInfoException(
                    "The sign template must contain '{{{file}}}' or '{{{file...}}}', " +
                    "which will be substituted by one, or many files, respectively.");
            }
        } else {
            Log.Info($"Preparing to codesign using embedded signtool.exe, with a parallelism of {parallelism}.");
        }

        if (filePaths.Length != pendingSign.Count) {
            var diff = filePaths.Length - pendingSign.Count;
            Log.Info($"{pendingSign.Count} file(s) will be signed, {diff} will be skipped.");
        }

        do {
            List<string> filesToSign = [];
            for (int i = Math.Max(1, Math.Min(pendingSign.Count, parallelism)); i > 0; i--) {
                filesToSign.Add(pendingSign.Dequeue());
            }

            var filesToSignStr = String.Join(" ", filesToSign.Select(f => $"\"{f}\""));

            string command;
            if (signAsTemplate) {
                command = signArguments.Replace("{{file}}", filesToSignStr);
            } else {
                if (VelopackRuntimeInfo.IsWindows) {
                    command = $"\"{HelperFile.SignToolPath}\" sign {signArguments} {filesToSignStr}";
                } else {
                    throw new PlatformNotSupportedException("signtool.exe does not work on non-Windows platforms.");
                }
            }

            RunSigningCommand(command, signLogFile);

            int processed = totalToSign - pendingSign.Count;
            Log.Info($"Code-signed {processed}/{totalToSign} files");
            progress((int) ((double) processed / totalToSign * 100));
        } while (pendingSign.Count > 0);

        Log.Debug("SignTool Output: " + Environment.NewLine + File.ReadAllText(signLogFile).Trim());
    }

    private void RunSigningCommand(string command, string signLogFile)
    {
        // here we invoke signtool.exe with 'cmd.exe /C' and redirect output to a file, because something
        // about how the dotnet tool host works prevents signtool from being able to open a token password
        // prompt, meaning signing fails for those with an HSM.

        var fileName = "cmd.exe";
        var args = $"/S /C \"{command} >> \"{signLogFile}\" 2>&1\"";

        if (!VelopackRuntimeInfo.IsWindows) {
            fileName = "/bin/bash";
            string escapedCommand = command.Replace("'", "'\\''");
            args = $"-c \"{escapedCommand} >> \\\"{signLogFile}\\\" 2>&1\"";
        }

        var psi = new ProcessStartInfo {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        using var process = Process.Start(psi);
        process.WaitForExit();

        if (process.ExitCode != 0) {
            var cmdWithPasswordHidden = fileName + " " + new Regex(@"\/p\s+?[^\s]+").Replace(args, "/p ********");
            Log.Debug($"Signing command failed - {Environment.NewLine}    {cmdWithPasswordHidden}");
            var output = File.Exists(signLogFile) ? File.ReadAllText(signLogFile).Trim() : "No output file was created.";
            throw new UserInfoException(
                $"Signing command failed. Specify --verbose argument to print signing command." + Environment.NewLine +
                $"Output was:" + Environment.NewLine + output);
        }
    }
}