using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Velopack.Packaging.Exceptions;

namespace Velopack.Packaging.Windows;

[SupportedOSPlatform("windows")]
public class CodeSign
{
    public ILogger Log { get; }

    public CodeSign(ILogger logger)
    {
        Log = logger;
    }

    private bool CheckIsAlreadySigned(string filePath)
    {
        if (String.IsNullOrWhiteSpace(filePath)) return true;

        if (!File.Exists(filePath)) {
            Log.Warn($"Cannot sign '{filePath}', file does not exist.");
            return true;
        }

        try {
            if (AuthenticodeTools.IsTrusted(filePath)) {
                Log.Debug($"'{filePath}' is already signed, skipping...");
                return true;
            }
        } catch (Exception ex) {
            Log.Error(ex, "Failed to determine signing status for " + filePath);
        }

        return false;
    }

    public void SignPEFilesWithSignTool(string rootDir, string[] filePaths, string signArguments, int parallelism, Action<int> progress)
    {
        Queue<string> pendingSign = new Queue<string>();

        foreach (var f in filePaths) {
            if (!CheckIsAlreadySigned(f)) {
                // try to find the path relative to rootDir
                if (String.IsNullOrEmpty(rootDir)) {
                    pendingSign.Enqueue(f);
                } else {
                    var partialPath = Utility.NormalizePath(f).Substring(Utility.NormalizePath(rootDir).Length).Trim('/', '\\');
                    pendingSign.Enqueue(partialPath);
                }
            } else {
                Log.Debug($"'{f}' is already signed, and will not be signed again.");
            }
        }

        if (filePaths.Length != pendingSign.Count) {
            var diff = filePaths.Length - pendingSign.Count;
            Log.Info($"{pendingSign.Count} files will be signed, {diff} will be skipped because they are already signed.");
        }

        // here we invoke signtool.exe with 'cmd.exe /C' and redirect output to a file, because something
        // about how the dotnet tool host works prevents signtool from being able to open a token password
        // prompt, meaning signing fails for those with an HSM.
        using var _1 = Utility.GetTempFileName(out var signLogFile);

        var totalToSign = pendingSign.Count;

        do {
            List<string> filesToSign = new List<string>();
            for (int i = Math.Min(pendingSign.Count, parallelism); i > 0; i--) {
                filesToSign.Add(pendingSign.Dequeue());
            }

            var filesToSignStr = String.Join(" ", filesToSign.Select(f => $"\"{f}\""));
            var command = $"\"{HelperFile.SignToolPath}\" sign {signArguments} {filesToSignStr}";
            RunSigningCommand(command, rootDir, signLogFile);

            int processed = totalToSign - pendingSign.Count;
            Log.Info($"Code-signed {processed}/{totalToSign} files");
            progress((int) ((double) processed / totalToSign * 100));
        } while (pendingSign.Count > 0);

        Log.Debug("SignTool Output: " + Environment.NewLine + File.ReadAllText(signLogFile).Trim());
    }

    public void SignPEFileWithTemplate(string filePath, string signTemplate)
    {
        if (VelopackRuntimeInfo.IsWindows && CheckIsAlreadySigned(filePath)) {
            Log.Debug($"'{filePath}' is already signed, and will not be signed again.");
            return;
        }

        using var _1 = Utility.GetTempFileName(out var signLogFile);
        var command = signTemplate.Replace("\"{{file}}\"", "{{file}}").Replace("{{file}}", $"\"{filePath}\"");
        RunSigningCommand(command, Path.GetDirectoryName(filePath), signLogFile);
        Log.Debug("SignTool Output: " + Environment.NewLine + File.ReadAllText(signLogFile).Trim());
    }

    private void RunSigningCommand(string command, string workDir, string signLogFile)
    {
        // here we invoke signtool.exe with 'cmd.exe /C' and redirect output to a file, because something
        // about how the dotnet tool host works prevents signtool from being able to open a token password
        // prompt, meaning signing fails for those with an HSM.

        string args = $"/S /C \"{command} >> \"{signLogFile}\" 2>&1\"";

        var psi = new ProcessStartInfo {
            FileName = "cmd.exe",
            Arguments = args,
            UseShellExecute = false,
            WorkingDirectory = workDir,
            CreateNoWindow = true,
        };

        var process = Process.Start(psi);
        process.WaitForExit();

        if (process.ExitCode != 0) {
            var cmdWithPasswordHidden = "cmd.exe " + new Regex(@"\/p\s+?[^\s]+").Replace(command, "/p ********");
            Log.Debug($"Signing command failed: {cmdWithPasswordHidden}");
            var output = File.Exists(signLogFile) ? File.ReadAllText(signLogFile).Trim() : "No output file was created.";
            throw new UserInfoException(
                $"Signing command failed. Specify --verbose argument to print signing command." + Environment.NewLine +
                $"Output was:" + Environment.NewLine + output);
        }
    }
}
