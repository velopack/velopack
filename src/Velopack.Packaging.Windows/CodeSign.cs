using System.Diagnostics;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Velopack.Packaging.Exceptions;

namespace Velopack.Packaging.Windows
{
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
                var command = $"/S /C \"\"{HelperFile.SignToolPath}\" sign {signArguments} {filesToSignStr} >> \"{signLogFile}\" 2>&1\"";

                var psi = new ProcessStartInfo {
                    FileName = "cmd.exe",
                    Arguments = command,
                    UseShellExecute = false,
                    WorkingDirectory = rootDir,
                    CreateNoWindow = true
                };

                var process = Process.Start(psi);
                process.WaitForExit();

                if (process.ExitCode != 0) {
                    var cmdWithPasswordHidden = "cmd.exe " + new Regex(@"\/p\s+?[^\s]+").Replace(command, "/p ********");
                    Log.Debug($"Signing command failed: {cmdWithPasswordHidden}");
                    var output = File.Exists(signLogFile) ? File.ReadAllText(signLogFile).Trim() : "No output file was created.";
                    throw new UserInfoException(
                        $"Signing command failed. Specify --verbose argument to print signing command.\n" +
                        $"Output was:" + Environment.NewLine + output);
                }

                int processed = totalToSign - pendingSign.Count;
                Log.Info($"Code-signed {processed}/{totalToSign} files");
                progress((int) ((double) processed / totalToSign * 100));
            } while (pendingSign.Count > 0);

            Log.Info("SignTool Output: " + Environment.NewLine + File.ReadAllText(signLogFile).Trim());
        }

        public void SignPEFileWithTemplate(string filePath, string signTemplate)
        {
            if (VelopackRuntimeInfo.IsWindows && CheckIsAlreadySigned(filePath)) {
                Log.Debug($"'{filePath}' is already signed, and will not be signed again.");
                return;
            }

            var command = signTemplate.Replace("\"{{file}}\"", "{{file}}").Replace("{{file}}", $"\"{filePath}\"");

            var result = Exe.InvokeProcess(command, null, null);
            if (result.ExitCode != 0) {
                var cmdWithPasswordHidden = new Regex(@"\/p\s+?[^\s]+").Replace(result.Command, "/p ********");
                Log.Debug($"Signing command failed: {cmdWithPasswordHidden}");
                throw new Exception(
                    $"Signing command failed. Specify --verbose argument to print signing command.\n\n" +
                    $"Output was:\n" + result.StdOutput);
            }

            Log.Info("Sign successful: " + result.StdOutput);
        }

        //private static ProcessStartInfo CreateProcessStartInfo(string fileName, string arguments, string workingDirectory = "")
        //{
        //    var psi = new ProcessStartInfo(fileName, arguments);
        //    psi.UseShellExecute = false;
        //    psi.WindowStyle = ProcessWindowStyle.Hidden;
        //    psi.ErrorDialog = false;
        //    psi.CreateNoWindow = true;
        //    psi.RedirectStandardOutput = true;
        //    psi.RedirectStandardError = true;
        //    psi.WorkingDirectory = workingDirectory;
        //    return psi;
        //}

        //private void SignPEFile(string filePath, string signParams, string signTemplate)
        //{
        //    try {
        //        if (AuthenticodeTools.IsTrusted(filePath)) {
        //            Log.Debug($"'{filePath}' is already signed, skipping...");
        //            return;
        //        }
        //    } catch (Exception ex) {
        //        Log.Error(ex, "Failed to determine signing status for " + filePath);
        //    }

        //    string cmd;
        //    ProcessStartInfo psi;
        //    if (!String.IsNullOrEmpty(signParams)) {
        //        // use embedded signtool.exe with provided parameters
        //        cmd = $"sign {signParams} \"{filePath}\"";
        //        psi = CreateProcessStartInfo(HelperFile.SignToolPath, cmd);
        //        cmd = "signtool.exe " + cmd;
        //    } else if (!String.IsNullOrEmpty(signTemplate)) {
        //        // escape custom sign command and pass it to cmd.exe
        //        cmd = signTemplate.Replace("\"{{file}}\"", "{{file}}").Replace("{{file}}", $"\"{filePath}\"");
        //        psi = CreateProcessStartInfo("cmd", $"/c {EscapeCmdExeMetachars(cmd)}");
        //    } else {
        //        Log.Debug($"{filePath} was not signed. (skipped; no signing parameters)");
        //        return;
        //    }

        //    var processResult = InvokeProcessUnsafeAsync(psi, CancellationToken.None)
        //        .ConfigureAwait(false).GetAwaiter().GetResult();

        //    if (processResult.ExitCode != 0) {
        //        var cmdWithPasswordHidden = new Regex(@"/p\s+\w+").Replace(cmd, "/p ********");
        //        throw new Exception("Signing command failed: \n > " + cmdWithPasswordHidden + "\n" + processResult.StdOutput);
        //    } else {
        //        Log.Info("Sign successful: " + processResult.StdOutput);
        //    }
        //}

        //private static string EscapeCmdExeMetachars(string command)
        //{
        //    var result = new StringBuilder();
        //    foreach (var ch in command) {
        //        switch (ch) {
        //        case '(':
        //        case ')':
        //        case '%':
        //        case '!':
        //        case '^':
        //        case '"':
        //        case '<':
        //        case '>':
        //        case '&':
        //        case '|':
        //            result.Append('^');
        //            break;
        //        }
        //        result.Append(ch);
        //    }
        //    return result.ToString();
        //}

        //private class ProcessResult
        //{
        //    public int ExitCode { get; set; }
        //    public string StdOutput { get; set; }

        //    public ProcessResult(int exitCode, string stdOutput)
        //    {
        //        ExitCode = exitCode;
        //        StdOutput = stdOutput;
        //    }
        //}

        //private static async Task<ProcessResult> InvokeProcessUnsafeAsync(ProcessStartInfo psi, CancellationToken ct)
        //{
        //    var pi = Process.Start(psi);
        //    await Task.Run(() => {
        //        while (!ct.IsCancellationRequested) {
        //            if (pi.WaitForExit(2000)) return;
        //        }

        //        if (ct.IsCancellationRequested) {
        //            pi.Kill();
        //            ct.ThrowIfCancellationRequested();
        //        }
        //    }).ConfigureAwait(false);

        //    string textResult = await pi.StandardOutput.ReadToEndAsync().ConfigureAwait(false);
        //    if (String.IsNullOrWhiteSpace(textResult) || pi.ExitCode != 0) {
        //        textResult = (textResult ?? "") + "\n" + await pi.StandardError.ReadToEndAsync().ConfigureAwait(false);

        //        if (String.IsNullOrWhiteSpace(textResult)) {
        //            textResult = String.Empty;
        //        }
        //    }

        //    return new ProcessResult(pi.ExitCode, textResult.Trim());
        //}
    }
}
