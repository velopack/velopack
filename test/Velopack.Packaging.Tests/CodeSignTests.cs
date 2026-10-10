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

        var psi = new ProcessStartInfo {
            FileName = bash,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add($"exec >> {CodeSign.QuoteFileArgsBash([logFile])} 2>&1\n{command}");
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

    private static (int exitCode, string output) RunViaShellStartInfo(string command, string logFile)
    {
        var psi = CodeSign.BuildShellStartInfo(command, logFile);
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

    [Theory]
    [InlineData("it's.exe")]
    [InlineData("quote\"d.exe")]
    [InlineData("dollar $HOME.exe")]
    [InlineData("tick `whoami`.exe")]
    [InlineData("back\\slash.exe")]
    [InlineData("semi;amp&pipe|.exe")]
    public void Bash_QuoteFileArgsBash_SpecialCharacters_PassedLiterally(string fileName)
    {
        using var _ = TempUtil.GetTempFileName(out var logFile);

        var path = "/some dir/" + fileName;
        var output = RunViaBash($"printf '[%s]\\n' {CodeSign.QuoteFileArgsBash([path])}", logFile);

        Assert.Equal($"[{path}]", output);
    }

    [Theory]
    [InlineData("printf '[%s]\\n' {{file}}")]
    [InlineData("printf '[%s]\\n' \"{{file}}\"")]
    [InlineData("printf '[%s]\\n' '{{file}}'")]
    public void Bash_SubstituteFilesBash_PlaceholderInAnyQuoting_GivesOneWordPerFile(string template)
    {
        using var _ = TempUtil.GetTempFileName(out var logFile);

        string[] files = ["/a dir/it's.exe", "/b dir/\"q\" $x.exe"];
        var output = RunViaBash(CodeSign.SubstituteFilesBash(template, files), logFile);

        Assert.Equal($"[{files[0]}]\n[{files[1]}]", output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Bash_SubstituteFilesBash_PlaceholderInsideLargerQuotedString_KeepsSurroundingText()
    {
        using var _ = TempUtil.GetTempFileName(out var logFile);

        var template = "printf '[%s]\\n' \"--file={{file}}\" '--x {{file}} y'";
        var output = RunViaBash(CodeSign.SubstituteFilesBash(template, ["/a b/c.exe"]), logFile);

        Assert.Equal("[--file=/a b/c.exe]\n[--x /a b/c.exe y]", output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Bash_SubstituteFilesBash_EscapedQuote_DoesNotOpenString()
    {
        using var _ = TempUtil.GetTempFileName(out var logFile);

        var template = "printf '[%s]\\n' \\\"{{file}}";
        var output = RunViaBash(CodeSign.SubstituteFilesBash(template, ["/a b.exe"]), logFile);

        Assert.Equal("[\"/a b.exe]", output);
    }

    [Theory]
    [InlineData("printf '[%s]\\n' \"$(dirname {{file}})/out\"", "[/a dir/out]")]
    [InlineData("printf '[%s]\\n' \"`dirname {{file}}`/out\"", "[/a dir/out]")]
    [InlineData("printf '[%s]\\n' \"$(echo \"in {{file}}\")\"", "[in /a dir/x.exe]")]
    [InlineData("printf '[%s]\\n' $'it\\'s {{file}}'", "[it's /a dir/x.exe]")]
    [InlineData("printf '[%s]\\n' $((1+2)) {{file}}.signed", "[3]\n[/a dir/x.exe.signed]")]
    [InlineData("X=1\nprintf '[%s]\\n' \"$X\" --in={{file}}", "[1]\n[--in=/a dir/x.exe]")]
    [InlineData("X=1\r\nprintf '[%s]\\n' \"$X\" {{file}}\r\n", "[1]\n[/a dir/x.exe]")]
    [InlineData("# don't\nprintf '[%s]\\n' {{file}} # it's", "[/a dir/x.exe]")]
    [InlineData("printf '[%s]\\n' a#b\"{{file}}\"", "[a#b/a dir/x.exe]")]
    [InlineData("printf '[%s]\\n' \"$(echo \"it's {{file}}\")\"", "[it's /a dir/x.exe]")]
    public void Bash_SubstituteFilesBash_NestedAndAdjacentPlaceholders_Work(string template, string expected)
    {
        using var _ = TempUtil.GetTempFileName(out var logFile);

        var output = RunViaBash(CodeSign.SubstituteFilesBash(template, ["/a dir/x.exe"]), logFile);

        Assert.Equal(expected, output.ReplaceLineEndings("\n"));
    }

    [Fact]
    public void Bash_SubstituteFilesBash_TemplateIsInterpretedByBash()
    {
        // The template is a bash command: single quotes group words, variables expand and
        // command substitution runs, the same way cmd.exe interprets the template on Windows.
        using var _ = TempUtil.GetTempFileName(out var logFile);

        var template = "X=val; printf '[%s]\\n' 'a b' \"$X\" `echo sub` {{file}}";
        var output = RunViaBash(CodeSign.SubstituteFilesBash(template, ["/f.exe"]), logFile);

        Assert.Equal("[a b]\n[val]\n[sub]\n[/f.exe]", output.ReplaceLineEndings("\n"));
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

    // ── BuildShellStartInfo: end-to-end through the actual shell ─────

    [Fact]
    public void BuildShellStartInfo_SpacedFiles_RunCorrectly()
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
        var (exitCode, output) = RunViaShellStartInfo(command, logFile);

        Assert.Equal(0, exitCode);
        Assert.Contains("My File.exe", output);
    }

    [Fact]
    public void BuildShellStartInfo_SignTemplate_RunsCorrectly()
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
        var (exitCode, output) = RunViaShellStartInfo(command, logFile);

        Assert.Equal(0, exitCode);
        Assert.Contains("Spaced Name.dll", output);
        Assert.Contains("Another File.exe", output);
    }

    [Fact]
    public void BuildShellStartInfo_LogFileWithSpaces_WritesCorrectly()
    {
        using var _1 = TempUtil.GetTempDirectory(out var dir);
        var logFile = Path.Combine(dir, "my log file.txt");

        var command = "echo hello-from-shell";
        var (exitCode, output) = RunViaShellStartInfo(command, logFile);

        Assert.Equal(0, exitCode);
        Assert.Contains("hello-from-shell", output);
    }

    [Fact]
    public void BuildShellStartInfo_Bash_CapturesOutputOfEveryCommand()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");
        using var _ = TempUtil.GetTempFileName(out var logFile);

        var (exitCode, output) = RunViaShellStartInfo("echo first && echo second >&2; false", logFile);

        Assert.Equal(1, exitCode);
        Assert.Contains("first", output);
        Assert.Contains("second", output);
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
    public void Sign_NoFilesToSign_ReportsCompletion()
    {
        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var missing = Path.Combine(dir, "missing.exe");
        var progressValues = new List<int>();
        signer.Sign([missing], "echo {{file}}", 1, p => progressValues.Add(p), true);

        Assert.Equal([100], progressValues);
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
    // CodeSign.Sign -> BuildShellStartInfo -> Process.Start. The template actually
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
    public void Sign_Template_BashSingleQuotedDollarAndBacktick_AreLiteral()
    {
        // The template is a bash command, so single quotes keep $ and ` literal.
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var srcFile = Path.Combine(dir, "app.exe");
        File.WriteAllText(srcFile, "data");
        var destFile = Path.Combine(dir, "with_$HOME_and_`whoami`_literal.bin");

        var template = $"cp {{{{file}}}} '{destFile}'";
        signer.Sign([srcFile], template, 1, _ => { }, true);

        Assert.True(File.Exists(destFile), "Destination must exist literally; if $HOME or `whoami` was expanded the file is elsewhere.");
    }

    [Fact]
    public void Sign_Template_BashEnvironmentVariable_IsExpanded()
    {
        // Lets a template read secrets from the environment, like %VAR% does on Windows.
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var srcFile = Path.Combine(dir, "app.exe");
        File.WriteAllText(srcFile, "data");
        var destFile = Path.Combine(dir, "from env var.bin");

        var varName = "VELOPACK_TEST_SIGN_DEST_" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(varName, destFile);
        try {
            signer.Sign([srcFile], $"cp {{{{file}}}} \"${varName}\"", 1, _ => { }, true);
        } finally {
            Environment.SetEnvironmentVariable(varName, null);
        }

        Assert.Equal("data", File.ReadAllText(destFile));
    }

    [Fact]
    public void Sign_Template_BashSingleQuotedArgument_StaysOneWord()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var srcFile = Path.Combine(dir, "app.exe");
        File.WriteAllText(srcFile, "data");

        signer.Sign([srcFile], "printf '[%s]\\n' --pass 'a b' {{file}}", 1, _ => { }, true);

        var signOutput = GetSignToolOutput(logger);
        Assert.Contains("[a b]", signOutput);
        Assert.Contains($"[{srcFile}]", signOutput);
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
    public void Sign_Template_BashSourceFilesWithQuotes_ArePassedLiterally()
    {
        // ' and " can't appear in Windows file names, but are legal on Linux and macOS.
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var outDir = Path.Combine(dir, "out");
        Directory.CreateDirectory(outDir);
        string[] names = ["it's.exe", "say \"hi\".exe", "both ' and \".exe"];
        var files = names.Select(n => Path.Combine(dir, n)).ToArray();
        foreach (var f in files) File.WriteAllText(f, Path.GetFileName(f));

        signer.Sign(files, $"cp {{{{file...}}}} '{outDir}'", 2, _ => { }, true);

        foreach (var n in names) {
            Assert.Equal(n, File.ReadAllText(Path.Combine(outDir, n)));
        }
    }

    [Fact]
    public void Sign_Template_BashPlaceholderWithSuffix_Works()
    {
        // The usual shape for tools that can't sign in place, e.g. osslsigncode -in X -out X.signed.
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var srcFile = Path.Combine(dir, "it's an app.exe");
        File.WriteAllText(srcFile, "data");

        signer.Sign([srcFile], "cp {{file}} {{file}}.signed && mv {{file}}.signed {{file}}", 1, _ => { }, true);

        Assert.Equal("data", File.ReadAllText(srcFile));
        Assert.False(File.Exists(srcFile + ".signed"));
    }

    [Fact]
    public void Sign_Template_BashFailure_ReportsOutputOfEveryCommand()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var srcFile = Path.Combine(dir, "app.exe");
        File.WriteAllText(srcFile, "data");

        var ex = Assert.Throws<UserInfoException>(() => signer.Sign([srcFile], "echo early-output >&2 && false {{file}}", 1, _ => { }, true));

        Assert.Contains("early-output", ex.Message);
    }

    [Fact]
    public void Sign_Template_BashPlaceholderInDoubleQuotes_IsOneWord()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var srcFile = Path.Combine(dir, "App With Spaces.exe");
        File.WriteAllText(srcFile, "data");
        var destFile = Path.Combine(dir, "dest.bin");

        signer.Sign([srcFile], $"cp \"{{{{file}}}}\" '{destFile}'", 1, _ => { }, true);

        Assert.Equal("data", File.ReadAllText(destFile));
    }

    [Fact]
    public void Sign_Template_SingleFileTemplate_ManyFiles_EveryBatchIdentical()
    {
        // A {{file}} template pins parallelism to 1, so each file is its own batch, and
        // every batch must run the template exactly as written.
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var files = new[] {
            Path.Combine(dir, "one.exe"),
            Path.Combine(dir, "two.exe"),
            Path.Combine(dir, "three.exe"),
        };
        foreach (var f in files) File.WriteAllText(f, "dummy");

        signer.Sign(files, "echo 'marker_$V' \"q\\\"x\" {{file}}", 1, _ => { }, true);

        var signOutput = GetSignToolOutput(logger);
        foreach (var f in files) {
            Assert.Contains($"marker_$V q\"x {f}", signOutput);
        }
    }

    [Fact]
    public void Sign_Template_MultiFileTemplate_MoreFilesThanParallelism_EveryBatchIdentical()
    {
        // Same via the {{file...}} path, where batching happens once the file count
        // exceeds --signParallel.
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var files = new[] {
            Path.Combine(dir, "a.exe"),
            Path.Combine(dir, "b.exe"),
            Path.Combine(dir, "c.exe"),
            Path.Combine(dir, "d.exe"),
            Path.Combine(dir, "e.exe"),
        };
        foreach (var f in files) File.WriteAllText(f, "dummy");

        // 5 files at a parallelism of 2 gives batches of 2, 2 and 1.
        signer.Sign(files, "echo 'marker_$V' \"q\\\"x\" {{file...}}", 2, _ => { }, true);

        var signOutput = GetSignToolOutput(logger);
        Assert.Contains($"marker_$V q\"x {files[0]} {files[1]}", signOutput);
        Assert.Contains($"marker_$V q\"x {files[2]} {files[3]}", signOutput);
        Assert.Contains($"marker_$V q\"x {files[4]}", signOutput);
    }

    [Fact]
    public void Sign_Template_MultipleBatches_CopyToQuotedDestination_Succeeds()
    {
        // The end-to-end shape a real signing template has: a quoted argument that
        // must survive every batch, not just the first.
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "bash-only test");

        using var logger = _output.BuildLoggerFor<CodeSignTests>(LogLevel.Debug);
        var console = new BasicConsole(logger, new VelopackDefaults(true));
        var signer = new CodeSign(logger, console);

        using var _1 = TempUtil.GetTempDirectory(out var dir);

        var file1 = Path.Combine(dir, "first.exe");
        var file2 = Path.Combine(dir, "second.exe");
        File.WriteAllText(file1, "data-1");
        File.WriteAllText(file2, "data-2");

        var destFile = Path.Combine(dir, "dest_$V.bin");
        signer.Sign([file1, file2], $"cp {{{{file}}}} '{destFile}'", 1, _ => { }, true);

        // Both batches ran, so the destination holds the second file's contents.
        Assert.True(File.Exists(destFile), "Destination must exist with '$' taken literally.");
        Assert.Equal("data-2", File.ReadAllText(destFile));
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
