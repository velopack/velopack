using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Security.Extensions;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Util;

namespace Velopack.Packaging.Windows;

public class CodeSign
{
    public ILogger Log { get; }

    private readonly IFancyConsole _console;

    public CodeSign(ILogger logger, IFancyConsole console)
    {
        Log = logger;
        _console = console;
    }

    /// <summary>Returns true if Windows considers <paramref name="filePath"/> Authenticode signed and trusted.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    public static bool IsTrusted(string filePath)
    {
        using var fileStream = File.OpenRead(filePath);
        var targetPackageSignatureInfo = FileSignatureInfo.GetFromFileStream(fileStream);
        return targetPackageSignatureInfo.State == SignatureState.SignedAndTrusted;
    }

    /// <summary>
    /// Returns false for files that do not exist, or (on Windows) that already have a trusted Authenticode signature.
    /// </summary>
    public static bool ShouldSign(ILogger log, string filePath)
    {
        if (String.IsNullOrWhiteSpace(filePath)) return true;

        if (!File.Exists(filePath)) {
            log.Warn($"Cannot sign '{filePath}', file does not exist.");
            return false;
        }

        try {
            if (OperatingSystem.IsWindows() && IsTrusted(filePath)) {
                log.Debug($"'{filePath}' is already signed, skipping...");
                return false;
            }
        } catch (Exception ex) {
            log.Error(ex, "Failed to determine signing status for " + filePath);
        }

        return true;
    }

    public void Sign(string[] filePaths, string signArguments, int parallelism, Action<int> progress, bool signAsTemplate)
    {
        Queue<string> pendingSign = new Queue<string>();

        foreach (var f in filePaths) {
            if (ShouldSign(Log, f)) {
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
                Log.Info($"Preparing to codesign using a multiple file signing template, with a parallelism of {parallelism}.");
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

        if (pendingSign.Count == 0) {
            progress(100);
            return;
        }

        if (!signAsTemplate && !VelopackRuntimeInfo.IsWindows) {
            throw new PlatformNotSupportedException("signtool.exe does not work on non-Windows platforms.");
        }

        do {
            List<string> filesToSign = [];
            for (int i = Math.Max(1, Math.Min(pendingSign.Count, parallelism)); i > 0; i--) {
                filesToSign.Add(pendingSign.Dequeue());
            }

            string command;
            if (!signAsTemplate) {
                command = $"\"{HelperFile.SignToolPath}\" sign {signArguments} {QuoteFileArgsWindows(filesToSign)}";
            } else if (VelopackRuntimeInfo.IsWindows) {
                command = signArguments.Replace("{{file}}", QuoteFileArgsWindows(filesToSign));
            } else {
                command = SubstituteFilesBash(signArguments, filesToSign);
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

        var psi = BuildShellStartInfo(command, signLogFile);

        using var process = Process.Start(psi);
        process.WaitForExit();

        if (process.ExitCode != 0) {
            var fullCommand = VelopackRuntimeInfo.IsWindows ? psi.FileName + " " + psi.Arguments : command;
            var cmdWithPasswordHidden = new Regex(@"\/p\s+?[^\s]+").Replace(fullCommand, "/p ********");
            Log.Debug($"Signing command failed - {Environment.NewLine}    {_console.EscapeMarkup(cmdWithPasswordHidden)}");
            var output = File.Exists(signLogFile) ? File.ReadAllText(signLogFile).Trim() : "No output file was created.";
            var verboseHint = Log.IsEnabled(LogLevel.Debug) ? "" : " Specify --verbose argument to print signing command.";
            throw new UserInfoException(
                $"Signing command failed.{verboseHint}" + Environment.NewLine +
                $"Output was:" + Environment.NewLine + output);
        }
    }

    public static string QuoteFileArgsWindows(IEnumerable<string> filePaths)
    {
        return String.Join(" ", filePaths.Select(f => $"\"{f}\""));
    }

    public static string QuoteFileArgsBash(IEnumerable<string> filePaths)
    {
        // bash takes everything inside single quotes literally, a ' itself is written as '\''
        return String.Join(" ", filePaths.Select(f => $"'{f.Replace("'", "'\\''")}'"));
    }

    /// <summary>
    /// Replaces {{file}} in a bash command with the quoted file paths. If the placeholder sits inside a
    /// "...", '...' or $'...' string, that string is closed around the paths so each path is still one word.
    /// Not tracked: heredoc bodies, ${...} expansions, and backslash rules inside `...` (use $(...) instead).
    /// </summary>
    public static string SubstituteFilesBash(string template, IEnumerable<string> filePaths)
    {
        const string placeholder = "{{file}}";
        var quotedFiles = QuoteFileArgsBash(filePaths);
        var sb = new StringBuilder();
        template = template.Replace("\r\n", "\n");

        // $(...), `...` and (...) each start a fresh quoting context, e.g. in "$(dirname {{file}})" the
        // placeholder is unquoted. quote is '\0' (none), '\'', '"', or '$' for an ANSI-C $'...' string.
        var contexts = new Stack<(char opener, char quote)>();
        contexts.Push(('\0', '\0'));

        void SetQuote(char q) => contexts.Push((contexts.Pop().opener, q));

        for (int i = 0; i < template.Length; i++) {
            var (opener, quote) = contexts.Peek();

            if (String.CompareOrdinal(template, i, placeholder, 0, placeholder.Length) == 0) {
                var close = quote switch { '\0' => "", '$' => "'", _ => quote.ToString() };
                var reopen = quote == '$' ? "$'" : close;
                sb.Append(close).Append(quotedFiles).Append(reopen);
                i += placeholder.Length - 1;
                continue;
            }

            char c = template[i];
            char next = i + 1 < template.Length ? template[i + 1] : '\0';
            sb.Append(c);

            if (quote == '\'') {
                if (c == '\'') SetQuote('\0');
            } else if (c == '\\') {
                if (next != '\0') sb.Append(template[++i]);
            } else if (quote == '$') {
                if (c == '\'') SetQuote('\0');
            } else if (c == '$' && next == '(') {
                sb.Append(template[++i]);
                contexts.Push(('(', '\0'));
            } else if (c == '$' && next == '\'' && quote == '\0') {
                sb.Append(template[++i]);
                SetQuote('$');
            } else if (c == '`') {
                if (opener == '`' && quote == '\0') contexts.Pop();
                else contexts.Push(('`', '\0'));
            } else if (quote == '"') {
                if (c == '"') SetQuote('\0');
            } else if (c == '#' && (i == 0 || " \t\n;&|(".Contains(template[i - 1]))) {
                // a comment runs to the end of the line, and quotes inside it mean nothing
                int end = template.IndexOf('\n', i);
                if (end < 0) end = template.Length;
                sb.Append(template, i + 1, end - i - 1);
                i = end - 1;
            } else if (c == '"' || c == '\'') {
                SetQuote(c);
            } else if (c == '(') {
                contexts.Push(('(', '\0'));
            } else if (c == ')' && opener == '(') {
                contexts.Pop();
            }
        }

        return sb.ToString();
    }

    public static ProcessStartInfo BuildShellStartInfo(string command, string logFile)
    {
        var psi = new ProcessStartInfo {
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (VelopackRuntimeInfo.IsWindows) {
            psi.FileName = "cmd.exe";
            psi.Arguments = $"/S /C \"{command} >> \"{logFile}\" 2>&1\"";
        } else {
            // /usr/bin/env is FHS-guaranteed even on distros like NixOS where /bin/bash doesn't exist.
            // ArgumentList hands the command to bash verbatim, and 'exec' redirects the output of every
            // command in it, not just the last one.
            psi.FileName = "/usr/bin/env";
            psi.ArgumentList.Add("bash");
            psi.ArgumentList.Add("-c");
            psi.ArgumentList.Add($"exec >> {QuoteFileArgsBash([logFile])} 2>&1\n{command}");
        }

        return psi;
    }
}
