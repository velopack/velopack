using System.Diagnostics;
using Neovolve.Logging.Xunit;
using Velopack.Core;
using Velopack.Packaging.Windows;
using Velopack.Util;
using Velopack.Vpk;
using Velopack.Vpk.Logging;

namespace Velopack.Packaging.Tests;

public class CodeSignTests
{
    private readonly ITestOutputHelper _output;

    public CodeSignTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private static string GetBashPath()
    {
        if (!VelopackRuntimeInfo.IsWindows) return "/bin/bash";
        var gitBash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "Git", "bin", "bash.exe");
        if (File.Exists(gitBash)) return gitBash;
        gitBash = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Git", "bin", "bash.exe");
        if (File.Exists(gitBash)) return gitBash;
        return null;
    }

    private static string RunViaBash(string command, string logFile)
    {
        var bash = GetBashPath();
        Assert.SkipWhen(bash == null, "bash not found");

        var args = $"-c \"{command} >> \\\"{logFile}\\\" 2>&1\"";
        var psi = new ProcessStartInfo {
            FileName = bash,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi);
        process.WaitForExit();
        return File.Exists(logFile) ? File.ReadAllText(logFile).Trim() : "";
    }

    private static string RunViaCmd(string command, string logFile)
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "cmd.exe only on Windows");

        var args = $"/S /C \"{command} >> \"{logFile}\" 2>&1\"";
        var psi = new ProcessStartInfo {
            FileName = "cmd.exe",
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi);
        process.WaitForExit();
        return File.Exists(logFile) ? File.ReadAllText(logFile).Trim() : "";
    }

    private static (int exitCode, string output) RunViaShellArgs(string command, string logFile)
    {
        var (fileName, args) = CodeSign.BuildShellArgs(command, logFile);
        var psi = new ProcessStartInfo {
            FileName = fileName,
            Arguments = args,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        using var process = Process.Start(psi);
        process.WaitForExit();
        var output = File.Exists(logFile) ? File.ReadAllText(logFile).Trim() : "";
        return (process.ExitCode, output);
    }

    // ── Bash: single-quote quoting (the PR #759 fix) ─────────────────

    [Fact]
    public void Bash_QuoteFileArgsBash_FilesWithSpaces_ArePassedCorrectly()
    {
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        using var _2 = TempUtil.GetTempFileName(out var logFile);

        var file1 = Path.Combine(dir, "Bus Monitor.exe");
        var file2 = Path.Combine(dir, "My App.exe");
        File.WriteAllText(file1, "");
        File.WriteAllText(file2, "");

        var fileArgs = CodeSign.QuoteFileArgsBash([file1, file2]);
        var output = RunViaBash($"echo {fileArgs}", logFile);

        Assert.Contains("Bus Monitor.exe", output);
        Assert.Contains("My App.exe", output);
    }

    [Fact]
    public void Bash_QuoteFileArgsBash_SingleFileNoSpaces_Works()
    {
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        using var _2 = TempUtil.GetTempFileName(out var logFile);

        var file1 = Path.Combine(dir, "simple.exe");
        File.WriteAllText(file1, "");

        var fileArgs = CodeSign.QuoteFileArgsBash([file1]);
        var output = RunViaBash($"echo {fileArgs}", logFile);

        Assert.Contains("simple.exe", output);
    }

    [Fact]
    public void Bash_QuoteFileArgsBash_SignTemplate_WorksEndToEnd()
    {
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        using var _2 = TempUtil.GetTempFileName(out var logFile);

        var file1 = Path.Combine(dir, "My Spaced App.dll");
        File.WriteAllText(file1, "");

        var signTemplate = "echo signing {{file}}";
        var fileArgs = CodeSign.QuoteFileArgsBash([file1]);
        var command = signTemplate.Replace("{{file}}", fileArgs);
        var output = RunViaBash(command, logFile);

        Assert.Contains("My Spaced App.dll", output);
    }

    [Fact]
    public void Bash_QuoteFileArgsWindows_DoubleQuotes_BreakWithSpaces()
    {
        // Demonstrates the bug that the single-quote fix solves:
        // double-quoted paths inside bash -c "..." lose their quoting.
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        using var _2 = TempUtil.GetTempFileName(out var logFile);

        var file1 = Path.Combine(dir, "Bus Monitor.exe");
        File.WriteAllText(file1, "");

        var fileArgs = CodeSign.QuoteFileArgsWindows([file1]);
        var output = RunViaBash($"echo {fileArgs}", logFile);

        Assert.DoesNotContain("Bus Monitor.exe", output);
    }

    [Fact]
    public void Bash_EscapeForBash_DollarSign_IsNotExpanded()
    {
        using var _ = TempUtil.GetTempFileName(out var logFile);

        var escaped = CodeSign.EscapeForBash("$HOME");
        var output = RunViaBash($"echo {escaped}", logFile);

        Assert.Contains("$HOME", output);
        // should NOT have been expanded to the actual home directory
        Assert.DoesNotContain("/home/", output);
        Assert.DoesNotContain("/Users/", output);
        Assert.DoesNotContain("C:\\Users\\", output);
    }

    [Fact]
    public void Bash_EscapeForBash_Backtick_IsNotExpanded()
    {
        using var _ = TempUtil.GetTempFileName(out var logFile);

        var escaped = CodeSign.EscapeForBash("`echo injected`");
        var output = RunViaBash($"echo {escaped}", logFile);

        // If the backticks were expanded, the output would be just "injected".
        // Since they're escaped, the literal backtick characters are preserved.
        Assert.Contains("`", output);
    }

    [Fact]
    public void Bash_EscapeForBash_PlainText_PassesThroughUnchanged()
    {
        using var _ = TempUtil.GetTempFileName(out var logFile);

        var escaped = CodeSign.EscapeForBash("hello world");
        var output = RunViaBash($"echo {escaped}", logFile);

        Assert.Contains("hello", output);
        Assert.Contains("world", output);
    }

    [Fact]
    public void Bash_MultipleSpacedFiles_AllPreserved()
    {
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        using var _2 = TempUtil.GetTempFileName(out var logFile);

        var files = new[] {
            Path.Combine(dir, "PcanView.exe"),
            Path.Combine(dir, "Bus Monitor.exe"),
            Path.Combine(dir, "Squirrel.exe"),
            Path.Combine(dir, "Bus Monitor_ExecutionStub.exe"),
        };
        foreach (var f in files) File.WriteAllText(f, "");

        var fileArgs = CodeSign.QuoteFileArgsBash(files);
        var output = RunViaBash($"echo {fileArgs}", logFile);

        foreach (var f in files) {
            Assert.Contains(Path.GetFileName(f), output);
        }
    }

    // ── Windows: cmd.exe double-quote quoting ────────────────────────

    [Fact]
    public void Cmd_QuoteFileArgsWindows_FilesWithSpaces_Work()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Only supported on Windows");
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        using var _2 = TempUtil.GetTempFileName(out var logFile);

        var file1 = Path.Combine(dir, "Bus Monitor.exe");
        var file2 = Path.Combine(dir, "My App.exe");
        File.WriteAllText(file1, "");
        File.WriteAllText(file2, "");

        var fileArgs = CodeSign.QuoteFileArgsWindows([file1, file2]);
        var output = RunViaCmd($"echo {fileArgs}", logFile);

        Assert.Contains("Bus Monitor.exe", output);
        Assert.Contains("My App.exe", output);
    }

    [Fact]
    public void Cmd_QuoteFileArgsWindows_SingleFileNoSpaces_Works()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Only supported on Windows");
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        using var _2 = TempUtil.GetTempFileName(out var logFile);

        var file1 = Path.Combine(dir, "simple.exe");
        File.WriteAllText(file1, "");

        var fileArgs = CodeSign.QuoteFileArgsWindows([file1]);
        var output = RunViaCmd($"echo {fileArgs}", logFile);

        Assert.Contains("simple.exe", output);
    }

    [Fact]
    public void Cmd_QuoteFileArgsWindows_SignTemplate_WorksEndToEnd()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Only supported on Windows");
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        using var _2 = TempUtil.GetTempFileName(out var logFile);

        var file1 = Path.Combine(dir, "My Spaced App.dll");
        File.WriteAllText(file1, "");

        var signTemplate = "echo signing {{file}}";
        var fileArgs = CodeSign.QuoteFileArgsWindows([file1]);
        var command = signTemplate.Replace("{{file}}", fileArgs);
        var output = RunViaCmd(command, logFile);

        Assert.Contains("My Spaced App.dll", output);
    }

    [Fact]
    public void Cmd_MultipleSpacedFiles_AllPreserved()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Only supported on Windows");
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        using var _2 = TempUtil.GetTempFileName(out var logFile);

        var files = new[] {
            Path.Combine(dir, "PcanView.exe"),
            Path.Combine(dir, "Bus Monitor.exe"),
            Path.Combine(dir, "Squirrel.exe"),
            Path.Combine(dir, "Bus Monitor_ExecutionStub.exe"),
        };
        foreach (var f in files) File.WriteAllText(f, "");

        var fileArgs = CodeSign.QuoteFileArgsWindows(files);
        var output = RunViaCmd($"echo {fileArgs}", logFile);

        foreach (var f in files) {
            Assert.Contains(Path.GetFileName(f), output);
        }
    }

    // ── BuildShellArgs: end-to-end through the actual shell ──────────

    [Fact]
    public void BuildShellArgs_SpacedFiles_RunCorrectly()
    {
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        using var _2 = TempUtil.GetTempFileName(out var logFile);

        var file1 = Path.Combine(dir, "My File.exe");
        File.WriteAllText(file1, "");

        string fileArgs;
        if (VelopackRuntimeInfo.IsWindows) {
            fileArgs = CodeSign.QuoteFileArgsWindows([file1]);
        } else {
            fileArgs = CodeSign.QuoteFileArgsBash([file1]);
        }

        var command = $"echo {fileArgs}";
        var (exitCode, output) = RunViaShellArgs(command, logFile);

        Assert.Equal(0, exitCode);
        Assert.Contains("My File.exe", output);
    }

    [Fact]
    public void BuildShellArgs_SignTemplate_RunsCorrectly()
    {
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        using var _2 = TempUtil.GetTempFileName(out var logFile);

        var file1 = Path.Combine(dir, "Spaced Name.dll");
        var file2 = Path.Combine(dir, "Another File.exe");
        File.WriteAllText(file1, "");
        File.WriteAllText(file2, "");

        string fileArgs;
        if (VelopackRuntimeInfo.IsWindows) {
            fileArgs = CodeSign.QuoteFileArgsWindows([file1, file2]);
        } else {
            fileArgs = CodeSign.QuoteFileArgsBash([file1, file2]);
        }

        var signTemplate = "echo template-signing {{file}}";
        var command = signTemplate.Replace("{{file}}", fileArgs);
        var (exitCode, output) = RunViaShellArgs(command, logFile);

        Assert.Equal(0, exitCode);
        Assert.Contains("Spaced Name.dll", output);
        Assert.Contains("Another File.exe", output);
    }

    [Fact]
    public void BuildShellArgs_LogFileWithSpaces_WritesCorrectly()
    {
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var logFile = Path.Combine(dir, "my log file.txt");

        var command = "echo hello-from-shell";
        var (exitCode, output) = RunViaShellArgs(command, logFile);

        Assert.Equal(0, exitCode);
        Assert.Contains("hello-from-shell", output);
    }

    // ── CodeSign.Sign() integration tests ────────────────────────────

    [Fact]
    public void Sign_SingleFileTemplate_FilesWithSpaces_PassedCorrectly()
    {
        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var file1 = Path.Combine(dir, "Bus Monitor.exe");
        var file2 = Path.Combine(dir, "My App.exe");
        File.WriteAllText(file1, "dummy");
        File.WriteAllText(file2, "dummy");

        // {{file}} = single-file template, parallelism=1 (one file at a time)
        signer.Sign([file1, file2], "echo {{file}}", 1, _ => { }, true);

        var signOutput = logger.Entries
            .Where(e => e.Message != null && e.Message.Contains("SignTool Output"))
            .Select(e => e.Message)
            .FirstOrDefault();

        Assert.NotNull(signOutput);
        Assert.Contains("Bus Monitor.exe", signOutput);
        Assert.Contains("My App.exe", signOutput);
    }

    [Fact]
    public void Sign_MultiFileTemplate_FilesWithSpaces_PassedCorrectly()
    {
        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var files = new[] {
            Path.Combine(dir, "PcanView.exe"),
            Path.Combine(dir, "Bus Monitor.exe"),
            Path.Combine(dir, "Squirrel.exe"),
            Path.Combine(dir, "Bus Monitor_ExecutionStub.exe"),
        };
        foreach (var f in files) File.WriteAllText(f, "dummy");

        // {{file...}} = multi-file template, all files passed at once
        signer.Sign(files, "echo {{file...}}", 10, _ => { }, true);

        var signOutput = logger.Entries
            .Where(e => e.Message != null && e.Message.Contains("SignTool Output"))
            .Select(e => e.Message)
            .FirstOrDefault();

        Assert.NotNull(signOutput);
        foreach (var f in files) {
            Assert.Contains(Path.GetFileName(f), signOutput);
        }
    }

    [Fact]
    public void Sign_SingleFileTemplate_NoSpaces_Works()
    {
        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var file1 = Path.Combine(dir, "simple.exe");
        File.WriteAllText(file1, "dummy");

        signer.Sign([file1], "echo {{file}}", 1, _ => { }, true);

        var signOutput = logger.Entries
            .Where(e => e.Message != null && e.Message.Contains("SignTool Output"))
            .Select(e => e.Message)
            .FirstOrDefault();

        Assert.NotNull(signOutput);
        Assert.Contains("simple.exe", signOutput);
    }

    [Fact]
    public void Sign_TemplateWithExtraArgs_FilesWithSpaces_Work()
    {
        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var file1 = Path.Combine(dir, "My Spaced App.dll");
        File.WriteAllText(file1, "dummy");

        // Template with extra arguments before the file placeholder
        signer.Sign([file1], "echo --flag value {{file}}", 1, _ => { }, true);

        var signOutput = logger.Entries
            .Where(e => e.Message != null && e.Message.Contains("SignTool Output"))
            .Select(e => e.Message)
            .FirstOrDefault();

        Assert.NotNull(signOutput);
        Assert.Contains("My Spaced App.dll", signOutput);
        Assert.Contains("--flag", signOutput);
    }

    [Fact]
    public void Sign_ReportsProgress()
    {
        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var file1 = Path.Combine(dir, "a.exe");
        var file2 = Path.Combine(dir, "b.exe");
        File.WriteAllText(file1, "dummy");
        File.WriteAllText(file2, "dummy");

        var progressValues = new List<int>();
        signer.Sign([file1, file2], "echo {{file}}", 1, p => progressValues.Add(p), true);

        // Two files signed one at a time should produce two progress updates
        Assert.Equal(2, progressValues.Count);
        Assert.Equal(100, progressValues.Last());
    }

    [Fact]
    public void Sign_FilesWithParentheses()
    {
        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var file1 = Path.Combine(dir, "PcanView (x64).exe");
        var file2 = Path.Combine(dir, "Setup (1).exe");
        File.WriteAllText(file1, "dummy");
        File.WriteAllText(file2, "dummy");

        signer.Sign([file1, file2], "echo {{file}}", 1, _ => { }, true);

        var signOutput = GetSignToolOutput(logger);
        Assert.Contains("PcanView (x64).exe", signOutput);
        Assert.Contains("Setup (1).exe", signOutput);
    }

    [Fact]
    public void Sign_FilesWithAmpersand()
    {
        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var file1 = Path.Combine(dir, "Tom & Jerry.exe");
        File.WriteAllText(file1, "dummy");

        signer.Sign([file1], "echo {{file}}", 1, _ => { }, true);

        var signOutput = GetSignToolOutput(logger);
        Assert.Contains("Tom & Jerry.exe", signOutput);
    }

    [Fact]
    public void Sign_MultiFileTemplate_MixOfSpacedAndSimple()
    {
        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var files = new[] {
            Path.Combine(dir, "simple.exe"),
            Path.Combine(dir, "Bus Monitor.exe"),
            Path.Combine(dir, "My App (x64).exe"),
            Path.Combine(dir, "Tom & Jerry.dll"),
        };
        foreach (var f in files) File.WriteAllText(f, "dummy");

        signer.Sign(files, "echo {{file...}}", 10, _ => { }, true);

        var signOutput = GetSignToolOutput(logger);
        foreach (var f in files) {
            Assert.Contains(Path.GetFileName(f), signOutput);
        }
    }

    [Fact]
    public void Sign_TemplateWithComplexArgs()
    {
        // Simulates a realistic signing command with multiple flags
        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var file1 = Path.Combine(dir, "My App.exe");
        File.WriteAllText(file1, "dummy");

        var template = "echo --storetype TRUSTEDSIGNING --keystore https://weu.codesigning.azure.net --alias MyOrg/MyProfile {{file}}";
        signer.Sign([file1], template, 1, _ => { }, true);

        var signOutput = GetSignToolOutput(logger);
        Assert.Contains("My App.exe", signOutput);
        Assert.Contains("TRUSTEDSIGNING", signOutput);
        Assert.Contains("MyOrg/MyProfile", signOutput);
    }

    private static string GetSignToolOutput(ICacheLogger logger)
    {
        var signOutput = logger.Entries
            .Where(e => e.Message != null && e.Message.Contains("SignTool Output"))
            .Select(e => e.Message)
            .FirstOrDefault();
        Assert.NotNull(signOutput);
        return signOutput;
    }

    // ── --signTemplate end-to-end tests ──────────────────────────────
    // These tests exercise the same code path as `vpk pack --signTemplate ...`:
    // CodeSign.Sign -> BuildShellArgs -> Process.Start. The template actually
    // copies {{file}} to a destination path and the test reads that file back,
    // which proves the file argument made it through the shell unmolested.

    [Fact]
    public void Sign_Template_CopiesFileToDestination_CrossPlatform()
    {
        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var srcFile = Path.Combine(dir, "App With Spaces.exe");
        File.WriteAllText(srcFile, "binary contents");
        var destFile = Path.Combine(dir, "destination.bin");

        var template = VelopackRuntimeInfo.IsWindows
            ? $"copy /Y {{{{file}}}} \"{destFile}\""
            : $"cp {{{{file}}}} \"{destFile}\"";

        signer.Sign([srcFile], template, 1, _ => { }, true);

        Assert.True(File.Exists(destFile), $"Expected destination file at {destFile} (signTemplate should have copied the file).");
        Assert.Equal("binary contents", File.ReadAllText(destFile));
    }

    [Fact]
    public void Sign_Template_BashDollarSignInTemplate_NotExpanded()
    {
        // Regression coverage for shell escaping in the --signTemplate flow.
        // If EscapeForBash is bypassed, $HOME gets expanded by bash and the
        // file is written to the wrong path (or fails outright).
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var srcFile = Path.Combine(dir, "app.exe");
        File.WriteAllText(srcFile, "data");
        var destFile = Path.Combine(dir, "with_$HOME_literal.bin");

        var template = $"cp {{{{file}}}} \"{destFile}\"";
        signer.Sign([srcFile], template, 1, _ => { }, true);

        Assert.True(File.Exists(destFile), "Destination must exist literally; if $HOME was expanded the file is elsewhere.");
    }

    [Fact]
    public void Sign_Template_BashBacktickInTemplate_NotExpanded()
    {
        // If the backtick weren't escaped, bash would run `whoami` and substitute
        // its output into the destination path.
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var srcFile = Path.Combine(dir, "app.exe");
        File.WriteAllText(srcFile, "data");
        var destFile = Path.Combine(dir, "with_`whoami`_literal.bin");

        var template = $"cp {{{{file}}}} \"{destFile}\"";
        signer.Sign([srcFile], template, 1, _ => { }, true);

        Assert.True(File.Exists(destFile), "Destination must exist literally; if `whoami` was expanded the file is elsewhere.");
    }

    [Fact]
    public void Sign_Template_BashSemicolonAndAmpersand_DoNotChainCommands()
    {
        // If template escaping is broken, ";rm -rf ..." or "&& rm ..." could
        // execute. Putting these chars inside a quoted dest path proves the
        // template is passed as one unit and not re-interpreted by the shell.
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var srcFile = Path.Combine(dir, "app.exe");
        File.WriteAllText(srcFile, "data");
        var destFile = Path.Combine(dir, "name;with&meta.bin");

        var template = $"cp {{{{file}}}} \"{destFile}\"";
        signer.Sign([srcFile], template, 1, _ => { }, true);

        Assert.True(File.Exists(destFile));
    }

    [Fact]
    public void Sign_Template_BashSourceFileWithDollarSign_IsPassedLiterally()
    {
        // Bash uses single-quoted file paths via QuoteFileArgsBash, so $ in
        // the file name itself must not be expanded either.
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var srcFile = Path.Combine(dir, "name with $HOME spaces.exe");
        File.WriteAllText(srcFile, "data");
        var destFile = Path.Combine(dir, "dest.bin");

        var template = $"cp {{{{file}}}} \"{destFile}\"";
        signer.Sign([srcFile], template, 1, _ => { }, true);

        Assert.True(File.Exists(destFile));
        Assert.Equal("data", File.ReadAllText(destFile));
    }

    [Fact]
    public void Sign_Template_WindowsCmdMetacharactersInDestination_Work()
    {
        // Windows: cmd-meaningful chars (parens, &, %) inside the quoted dest
        // path should pass through correctly.
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var srcFile = Path.Combine(dir, "Bus Monitor.exe");
        File.WriteAllText(srcFile, "data");
        var destFile = Path.Combine(dir, "name (x64) and amp.bin");

        var template = $"copy /Y {{{{file}}}} \"{destFile}\"";
        signer.Sign([srcFile], template, 1, _ => { }, true);

        Assert.True(File.Exists(destFile));
        Assert.Equal("data", File.ReadAllText(destFile));
    }

    [Fact]
    public void Sign_Template_WindowsSourceFileWithParensAndAmpersand_PassedCorrectly()
    {
        // Windows: file paths get wrapped in double quotes by QuoteFileArgsWindows,
        // so parens, &, etc in source filenames must pass through to cmd.exe intact.
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "Windows only");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var srcFile = Path.Combine(dir, "Tom & Jerry (x64).exe");
        File.WriteAllText(srcFile, "data");
        var destFile = Path.Combine(dir, "dest.bin");

        var template = $"copy /Y {{{{file}}}} \"{destFile}\"";
        signer.Sign([srcFile], template, 1, _ => { }, true);

        Assert.True(File.Exists(destFile));
        Assert.Equal("data", File.ReadAllText(destFile));
    }
}
