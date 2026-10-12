using System.Diagnostics;
using System.Text;
using Velopack.Ipc;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Tests;

// The C# and Rust libraries must derive the identical IPC endpoint and speak the identical protocol: a C# app
// server serves a Rust client and vice versa. The Rust side is the `ipc-*` commands of the Rust test app
// (src/bins/src/testapp.rs), which also give the only cross-process coverage of the Unix (fcntl) election.
public class IpcInteropTests
{
    private const string AppId = "InteropApp";

    private readonly ITestOutputHelper _output;

    public IpcInteropTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private sealed class ListLogger : IVelopackLogger
    {
        private readonly ITestOutputHelper _output;
        public List<string> Entries { get; } = new();

        public ListLogger(ITestOutputHelper output) => _output = output;

        public void Log(VelopackLogLevel logLevel, string message, Exception exception)
        {
            var line = $"[{logLevel}] {message} {exception}".TrimEnd();
            lock (Entries) Entries.Add(line);
            try { _output.WriteLine(line); } catch { }
        }
    }

    /// <summary> The Rust test app, built by a plain `cargo build`. Missing locally = skip, missing in CI = fail. </summary>
    private static string RustTestApp()
    {
        var path = PathHelper.GetRustAsset(VelopackRuntimeInfo.IsWindows ? "testapp.exe" : "testapp");
        Assert.SkipWhen(!File.Exists(path) && !PathHelper.IsCI, "build the rust testapp: cargo build");
        Assert.True(File.Exists(path), $"rust testapp missing: {path}");
        return path;
    }

    /// <summary>
    /// A fresh install root per test: the in-process C# servers live (holding their endpoint lock, keyed on the root)
    /// for the whole test process, so no root may ever be reused.
    /// </summary>
    private static string UniqueRoot(string tempPath)
    {
        var root = Path.Combine(tempPath, "Interop Root " + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Path.Combine(root, "packages"));
        return root;
    }

    private static TestVelopackLocator Locator(string root, IVelopackLogger log) =>
        new(AppId, "1.0.0", Path.Combine(root, "packages"), null, root, null, logger: log) { AssociationSingleInstance = true };

    /// <summary> The real C# server (election + association handler) with recording callbacks. </summary>
    private static List<(string Flag, string Value)> StartCSharpServer(string root, IVelopackLogger log)
    {
        var received = new List<(string Flag, string Value)>();
        var app = VelopackApp.Build()
            .OnFileAssoc(v => { lock (received) received.Add((VelopackApp.HookCliFileAssoc, v)); })
            .OnUrlProto(v => { lock (received) received.Add((VelopackApp.HookCliUrlProto, v)); });
        Assert.Equal(Election.Server, SingleInstance.Elect(Locator(root, log), new(), log, out var server));
        SingleInstance.Start(server!, app, log);
        Assert.Contains(((ListLogger) log).Entries, e => e.Contains("serving association activations"));
        return received;
    }

    private static IpcEndpoint Endpoint(string root, IVelopackLogger log)
    {
        var ep = IpcEndpoint.ForApp(AppId, root, log);
        Assert.NotNull(ep);
        return ep;
    }

    private static IpcMessage Association(string flag, string value) => new(IpcProtocol.TypeAssociation) {
        Items = new List<IpcItem> { new(flag, value) },
    };

    private static void WaitFor(Func<bool> condition, string what)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(10);
        while (!condition() && DateTime.UtcNow < deadline) {
            Thread.Sleep(20);
        }

        Assert.True(condition(), $"{what} not observed within 10 s");
    }

    /// <summary> The Rust test app with its stdout captured line by line and its stdin held open until Stop(). </summary>
    private sealed class RustProcess : IDisposable
    {
        private readonly Process _process;
        private readonly List<string> _stdout = new();
        private readonly StringBuilder _stderr = new();

        public RustProcess(ITestOutputHelper output, params string[] args)
        {
            var psi = new ProcessStartInfo(RustTestApp()) {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetTempPath(),
            };
            foreach (var a in args) psi.ArgumentList.Add(a);
            output.WriteLine("TEST: starting testapp " + string.Join(" ", args));
            _process = Process.Start(psi)!;
            _process.OutputDataReceived += (_, e) => {
                if (e.Data != null) lock (_stdout) _stdout.Add(e.Data);
            };
            _process.ErrorDataReceived += (_, e) => {
                if (e.Data != null) {
                    lock (_stderr) _stderr.AppendLine(e.Data);
                    try { output.WriteLine("testapp: " + e.Data); } catch { }
                }
            };
            _process.BeginOutputReadLine();
            _process.BeginErrorReadLine();
        }

        public string[] Stdout {
            get { lock (_stdout) return _stdout.ToArray(); }
        }

        public string Describe()
        {
            lock (_stderr) return $"stdout: [{string.Join(" | ", Stdout)}] stderr: {_stderr}";
        }

        /// <summary> Waits for <paramref name="line"/> on stdout (an early exit fails with the captured output). </summary>
        public void WaitForLine(string line)
        {
            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(15);
            while (DateTime.UtcNow < deadline) {
                if (Stdout.Contains(line)) {
                    return;
                }

                if (_process.HasExited) {
                    _process.WaitForExit(); // flushes the async readers
                    Assert.True(Stdout.Contains(line), $"testapp exited ({_process.ExitCode}) before printing '{line}'. {Describe()}");
                    return;
                }

                Thread.Sleep(20);
            }

            Assert.Fail($"testapp did not print '{line}' within 15 s. {Describe()}");
        }

        /// <summary> Closes stdin (the servers' exit signal) and returns the exit code. </summary>
        public int Stop()
        {
            try { _process.StandardInput.Close(); } catch { }
            if (!_process.WaitForExit(15_000)) {
                _process.Kill();
                _process.WaitForExit();
                Assert.Fail("testapp did not exit after stdin was closed. " + Describe());
            }

            // lets the async readers deliver the last lines
            _process.WaitForExit();
            return _process.ExitCode;
        }

        public void Dispose()
        {
            if (!_process.HasExited) {
                try { _process.Kill(); } catch { }
            }

            _process.Dispose();
        }
    }

    private (int ExitCode, string[] Stdout) RunRust(params string[] args)
    {
        using var p = new RustProcess(_output, args);
        var code = p.Stop();
        _output.WriteLine($"TEST: testapp exited with {code}: {p.Describe()}");
        return (code, p.Stdout);
    }

    [Fact]
    public void EndpointMatchesRust()
    {
        var log = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = Path.Combine(tempPath, "Mixed Case Root");
        Directory.CreateDirectory(root);
        root += Path.DirectorySeparatorChar;

        var ep = Endpoint(root, log);
        var (code, lines) = RunRust("ipc-endpoint", AppId, root);
        Assert.Equal(0, code);
        Assert.Equal(3, lines.Length);
        var comparer = VelopackRuntimeInfo.IsWindows ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        Assert.Equal(ep.Key, lines[0]);
        Assert.Equal(ep.Address, lines[1], comparer);
        Assert.Equal(ep.LockPath, lines[2], comparer);
    }

    [Fact]
    public void CSharpServerRustClient()
    {
        var log = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);
        var received = StartCSharpServer(root, log);

        var value = "myapp://a b?c=\"d\"";
        var (code, lines) = RunRust("ipc-send", AppId, root, VelopackApp.HookCliUrlProto, value);
        Assert.Equal(0, code);
        Assert.Equal("ack", lines.Last().Trim());
        lock (received) {
            Assert.Equal(new[] { (VelopackApp.HookCliUrlProto, value) }, received);
        }
    }

    [Fact]
    public void RustServerCSharpClient()
    {
        var log = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);
        var outFile = Path.Combine(tempPath, "received.txt");

        using var server = new RustProcess(_output, "ipc-serve", AppId, root, outFile);
        server.WaitForLine("ready");

        var path = Path.Combine(root, "my dócument ü \"quoted\".txt");
        using (var conn = IpcServerClientTests.ConnectWithRetries(Endpoint(root, log))) {
            var reply = conn.Request(Association(VelopackApp.HookCliFileAssoc, path), IpcProtocol.ExchangeTimeout);
            Assert.True(reply.IsAck, $"{reply.Type} {reply.Message}");
        }

        Assert.Equal($"{VelopackApp.HookCliFileAssoc} {path}\n", File.ReadAllText(outFile));
        Assert.Equal(0, server.Stop());
    }

    [Fact]
    public void RustServerRejectsUnknownType()
    {
        var log = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);
        var outFile = Path.Combine(tempPath, "received.txt");

        using var server = new RustProcess(_output, "ipc-serve", AppId, root, outFile);
        server.WaitForLine("ready");

        using (var conn = IpcServerClientTests.ConnectWithRetries(Endpoint(root, log))) {
            var reply = conn.Request(new IpcMessage("bogus"), IpcProtocol.ExchangeTimeout);
            Assert.Equal(IpcProtocol.TypeError, reply.Type);
            Assert.Equal(IpcProtocol.ErrUnknownMessageType, reply.Message);
        }

        Assert.False(File.Exists(outFile));
        Assert.Equal(0, server.Stop());
    }

    [Fact]
    public void CSharpServerRejectsUnknownType()
    {
        var log = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);
        var received = StartCSharpServer(root, log);

        var (code, lines) = RunRust("ipc-send", AppId, root, VelopackApp.HookCliUrlProto, "myapp://x", "bogus");
        Assert.Equal(1, code);
        Assert.Equal($"error {IpcProtocol.ErrUnknownMessageType}", lines.Last().Trim());
        lock (received) Assert.Empty(received);
    }

    [Fact]
    public void CSharpServerRustElectForwards()
    {
        var log = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);
        var received = StartCSharpServer(root, log);

        var (code, lines) = RunRust("ipc-elect", AppId, root, Path.Combine(tempPath, "unused.txt"), VelopackApp.HookCliUrlProto, "myapp://x");
        Assert.Equal(0, code);
        Assert.Equal(new[] { "Forwarded" }, lines);
        lock (received) {
            Assert.Equal(new[] { (VelopackApp.HookCliUrlProto, "myapp://x") }, received);
        }
    }

    [Fact]
    public void RustServerCSharpElectForwards()
    {
        var log = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);
        var outFile = Path.Combine(tempPath, "received.txt");

        using var server = new RustProcess(_output, "ipc-elect", AppId, root, outFile);
        server.WaitForLine("Server");
        server.WaitForLine("ready");

        var associations = new List<(string Flag, string Value)> { (VelopackApp.HookCliUrlProto, "myapp://y") };
        Assert.Equal(Election.Forwarded, SingleInstance.Elect(Locator(root, log), associations, log, out var none));
        Assert.Null(none);

        // the ack is sent after the hook ran, so the line is already there
        Assert.Equal($"{VelopackApp.HookCliUrlProto} myapp://y\n", File.ReadAllText(outFile));
        Assert.Equal(0, server.Stop());
    }

    [Fact]
    public void StaleSocketIsReplaced()
    {
        Assert.SkipWhen(VelopackRuntimeInfo.IsWindows, "stale socket files are a Unix domain socket concern");
        var log = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);
        var outFile = Path.Combine(tempPath, "received.txt");

        // a socket file left behind by a crashed server
        var (code, lines) = RunRust("ipc-endpoint", AppId, root);
        Assert.Equal(0, code);
        File.WriteAllText(lines[1], "");

        using var server = new RustProcess(_output, "ipc-elect", AppId, root, outFile);
        server.WaitForLine("Server");
        server.WaitForLine("ready");

        using (var conn = IpcServerClientTests.ConnectWithRetries(Endpoint(root, log))) {
            var reply = conn.Request(Association(VelopackApp.HookCliUrlProto, "myapp://z"), IpcProtocol.ExchangeTimeout);
            Assert.True(reply.IsAck, $"{reply.Type} {reply.Message}");
        }

        Assert.Equal($"{VelopackApp.HookCliUrlProto} myapp://z\n", File.ReadAllText(outFile));
        Assert.Equal(0, server.Stop());
    }

    [Fact]
    public void RustServerCSharpPlainLaunchContinues()
    {
        var log = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);
        var outFile = Path.Combine(tempPath, "received.txt");

        using var server = new RustProcess(_output, "ipc-elect", AppId, root, outFile);
        server.WaitForLine("Server");
        server.WaitForLine("ready");

        // a plain launch while another instance holds the lock is just another instance: no forwarding, no exit, no wait
        var locator = Locator(root, log);
        var timer = Stopwatch.StartNew();
        VelopackApp.Build()
            .SetLocator(locator)
            .SetArgs(new[] { "--not-an-association", "x" })
            .OnUrlProto(_ => Assert.Fail("no activation was launched"))
            .Run();
        Assert.Null(locator.ExitCode);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(4), $"a plain launch must not wait for the connect window ({timer.Elapsed})");
        Assert.DoesNotContain(log.Entries, e => e.Contains("serving association activations"));

        Assert.False(File.Exists(outFile));
        Assert.Equal(0, server.Stop());
    }

    [Fact]
    public void RustClientPlainLaunchIsStandalone()
    {
        var log = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);
        var received = StartCSharpServer(root, log);

        var timer = Stopwatch.StartNew();
        var (code, lines) = RunRust("ipc-elect", AppId, root, Path.Combine(tempPath, "unused.txt"));
        Assert.Equal(0, code);
        Assert.Equal(new[] { "Standalone" }, lines);
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(4), $"a plain launch must not wait for the connect window ({timer.Elapsed})");
        lock (received) Assert.Empty(received);
    }
}
