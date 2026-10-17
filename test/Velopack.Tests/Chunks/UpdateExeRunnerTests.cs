#nullable enable
using System.Diagnostics;
using Velopack.Chunks;
using Velopack.Core;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Tests.Chunks;

/// <summary>
/// <see cref="UpdateExeRunner"/> protocol parsing and process control, using tiny shell scripts in place of Update.exe.
/// </summary>
public class UpdateExeRunnerTests
{
    private readonly ITestOutputHelper _output;

    public UpdateExeRunnerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>A script that plays Update.exe: each line is a command; sleeps and stderr are abstracted per OS.</summary>
    private sealed class Script : IDisposable
    {
        private readonly IDisposable _tempDir;
        public string Exe { get; }
        public List<string> Args { get; } = new();

        public Script(IEnumerable<string> lines)
        {
            _tempDir = TempUtil.GetTempDirectory(out var dir);
            if (VelopackRuntimeInfo.IsWindows) {
                var path = Path.Combine(dir, "fake-update.cmd");
                File.WriteAllLines(path, new[] { "@echo off" }.Concat(lines.Select(ToCmd)));
                Exe = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe";
                Args.Add("/c");
                Args.Add(path);
            } else {
                var path = Path.Combine(dir, "fake-update.sh");
                File.WriteAllLines(path, new[] { "#!/bin/sh" }.Concat(lines.Select(ToSh)));
                Exe = "/bin/sh";
                Args.Add(path);
            }
        }

        public static string Echo(string text) => "echo " + text;
        public static string EchoErr(string text) => "echoerr " + text;
        public static string Exit(int code) => "exit " + code;
        public const string Sleep = "sleep";

        private static string ToCmd(string line)
        {
            if (line == Sleep) return "ping -n 31 127.0.0.1 >nul";
            if (line.StartsWith("echoerr ")) return "echo " + line.Substring(8) + " 1>&2";
            if (line.StartsWith("exit ")) return "exit /b " + line.Substring(5);
            return line;
        }

        private static string ToSh(string line)
        {
            if (line == Sleep) return "sleep 30";
            if (line.StartsWith("echoerr ")) return "echo " + line.Substring(8) + " >&2";
            return line;
        }

        public void Dispose() => _tempDir.Dispose();
    }

    private sealed class CapturingLogger : IVelopackLogger
    {
        public readonly List<(VelopackLogLevel level, string message)> Lines = new();

        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
        {
            lock (Lines) Lines.Add((logLevel, message ?? ""));
        }
    }

    private UpdateExeRunner CreateRunner(IVelopackLogger? log = null)
    {
        return new UpdateExeRunner(log ?? _output.BuildLoggerFor<UpdateExeRunnerTests>().ToVelopackLogger()) {
            PollInterval = TimeSpan.FromMilliseconds(50),
        };
    }

    [Fact]
    public async Task ParsesProtocolLines()
    {
        using var script = new Script(new[] {
            Script.Echo("VPK:PROGRESS 10"),
            Script.Echo("VPK:PROGRESS 50"),
            Script.Echo("some ordinary log line"),
            Script.Echo("VPK:PROGRESS 40"),
            Script.Echo("VPK:MISSING 3"),
            Script.Echo("VPK:ERROR chunks are missing"),
            Script.Exit(3),
        });
        var progress = new List<int>();
        var result = await CreateRunner().RunAsync(script.Exe, script.Args, progress.Add, CancellationToken.None);

        Assert.Equal(3, result.ExitCode);
        Assert.True(result.SawProtocolLine);
        Assert.Equal(3, result.MissingCount);
        Assert.Equal("chunks are missing", result.ErrorMessage);
        // progress is monotonic: the out-of-order 40 is dropped
        Assert.Equal(new[] { 10, 50 }, progress);
    }

    [Fact]
    public async Task ProtocolOnlyAtLineStart()
    {
        using var script = new Script(new[] {
            Script.Echo("  VPK:PROGRESS 99"),
            Script.Echo("log VPK:ERROR not really"),
            Script.Echo("VPK:PROGRESSX 5"),
            Script.Exit(0),
        });
        var progress = new List<int>();
        var result = await CreateRunner().RunAsync(script.Exe, script.Args, progress.Add, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.False(result.SawProtocolLine);
        Assert.Null(result.ErrorMessage);
        Assert.Null(result.MissingCount);
        Assert.Empty(progress);
    }

    [Fact]
    public async Task KillsOnCancel()
    {
        using var script = new Script(new[] { Script.Echo("VPK:PROGRESS 1"), Script.Sleep, Script.Echo("VPK:PROGRESS 100"), Script.Exit(0) });
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));
        var sw = Stopwatch.StartNew();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => CreateRunner().RunAsync(script.Exe, script.Args, null, cts.Token));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"the runner did not kill the process promptly: {sw.Elapsed}");
    }

    [Fact]
    public async Task KillsAfterInactivity()
    {
        using var script = new Script(new[] { Script.Echo("VPK:PROGRESS 1"), Script.Sleep, Script.Echo("VPK:PROGRESS 100"), Script.Exit(0) });
        var skew = TimeSpan.Zero;
        var runner = CreateRunner();
        runner.Clock = () => DateTime.UtcNow + skew;
        var progress = new List<int>();

        var run = runner.RunAsync(script.Exe, script.Args, progress.Add, CancellationToken.None);
        // once the first line has arrived, jump the clock past the inactivity cap
        var sw = Stopwatch.StartNew();
        while (progress.Count == 0 && sw.Elapsed < TimeSpan.FromSeconds(10)) await Task.Delay(20);
        Assert.Equal(new[] { 1 }, progress);
        skew = UpdateExeRunner.DefaultInactivityTimeout + TimeSpan.FromMinutes(1);

        var ex = await Assert.ThrowsAsync<AssembleFailedException>(() => run);
        Assert.Contains("no output", ex.Message);
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(15), $"the runner did not kill the process promptly: {sw.Elapsed}");
    }

    [Fact]
    public async Task ForwardsOtherLinesToLogger()
    {
        using var script = new Script(new[] {
            Script.Echo("hello from stdout"), Script.EchoErr("oops from stderr"), Script.Echo("VPK:PROGRESS 100"), Script.Exit(0),
        });
        var log = new CapturingLogger();
        var result = await CreateRunner(log).RunAsync(script.Exe, script.Args, null, CancellationToken.None);

        Assert.Equal(0, result.ExitCode);
        Assert.True(result.SawProtocolLine);
        Assert.Contains(log.Lines, l => l.level == VelopackLogLevel.Debug && l.message.Contains("hello from stdout"));
        Assert.Contains(log.Lines, l => l.level == VelopackLogLevel.Warning && l.message.Contains("oops from stderr"));
        Assert.DoesNotContain(log.Lines, l => l.message.Contains("VPK:PROGRESS"));
    }

    [Fact]
    public async Task ExitOneWithoutProtocolIsUnsupported()
    {
        // exactly what an updater that predates 'assemble' does: clap's ignore_errors message and exit 1
        using var script = new Script(new[] { Script.Echo("No known subcommand was used"), Script.Exit(1) });
        var result = await CreateRunner().RunAsync(script.Exe, script.Args, null, CancellationToken.None);

        Assert.Equal(1, result.ExitCode);
        Assert.False(result.SawProtocolLine);
    }
}
