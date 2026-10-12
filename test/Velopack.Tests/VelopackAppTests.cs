using Velopack.Ipc;
using Velopack.Locators;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Tests;

public class VelopackAppTests
{
    // VelopackApp.Run() caches the locator process-wide, so a later Run() logs its "called more than once" error to the
    // previous test's locator. This logger never throws, unlike the xunit output logger once its test has finished.
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

    private readonly ITestOutputHelper _output;

    public VelopackAppTests(ITestOutputHelper output)
    {
        _output = output;
    }

    [Theory]
    // simple quoted path, as the shell substitutes %1
    [InlineData(@"""C:\Apps\MyApp\current\MyApp.exe"" --veloapp-fileassoc ""C:\Users\me\My Documents\file.myproj""",
        new[] { "--veloapp-fileassoc", @"C:\Users\me\My Documents\file.myproj" }, @"C:\Users\me\My Documents\file.myproj")]
    // URL with embedded quotes: argv would collapse the quotes, the raw command line keeps them
    [InlineData(@"""C:\Apps\MyApp\current\MyApp.exe"" --veloapp-urlproto ""myapp://open?q=""quoted"" value""",
        new[] { "--veloapp-urlproto", "myapp://open?q=quoted value" }, @"myapp://open?q=""quoted"" value")]
    // unquoted value
    [InlineData(@"""C:\Apps\MyApp\current\MyApp.exe"" --veloapp-urlproto myapp://plain",
        new[] { "--veloapp-urlproto", "myapp://plain" }, "myapp://plain")]
    // extra whitespace around the flag and value
    [InlineData("  \"C:\\Apps\\MyApp\\current\\MyApp.exe\"   --veloapp-urlproto \t  \"myapp://spaced\"   ",
        new[] { "--veloapp-urlproto", "myapp://spaced" }, "myapp://spaced")]
    // flag casing is not significant, like the other veloapp hooks
    [InlineData(@"MyApp.exe --VeloApp-UrlProto ""myapp://case""",
        new[] { "--VeloApp-UrlProto", "myapp://case" }, "myapp://case")]
    // unquoted path with spaces (argv would have split it); everything after the flag is the value
    [InlineData(@"MyApp.exe --veloapp-fileassoc C:\Users\me\My Documents\file.myproj",
        new[] { "--veloapp-fileassoc", @"C:\Users\me\My", @"Documents\file.myproj" }, @"C:\Users\me\My Documents\file.myproj")]
    // the exe path containing the flag text must not be mistaken for the flag token
    [InlineData(@"""C:\--veloapp-urlproto\MyApp.exe"" --veloapp-urlproto ""myapp://x""",
        new[] { "--veloapp-urlproto", "myapp://x" }, "myapp://x")]
    // dotnet-hosted launch: the host and dll come first
    [InlineData(@"""C:\Program Files\dotnet\dotnet.exe"" ""C:\Apps\MyApp\MyApp.dll"" --veloapp-urlproto ""myapp://hosted""",
        new[] { "--veloapp-urlproto", "myapp://hosted" }, "myapp://hosted")]
    public void RecoversValueFromRawCommandLine(string rawCommandLine, string[] args, string expected)
    {
        var associations = VelopackApp.ParseAssociationArgs(args, rawCommandLine);
        Assert.Equal(new[] { (args[0].ToLowerInvariant(), expected) }, associations);
    }

    [Fact]
    public void RawCommandLineProgramPathIsSkipped()
    {
        // mirrors the Rust test: the flag as a separate word inside the program path, or as a prefix of a longer token,
        // is not the flag. Only the program path (first token) is skipped, so the real flag later on is still found.
        var raw = @"""C:\dir --veloapp-urlproto \MyApp.exe"" --veloapp-urlproto-extra 1 --veloapp-urlproto ""myapp://y""";
        Assert.Equal("myapp://y", VelopackApp.RecoverValueFromRawCommandLine(VelopackApp.HookCliUrlProto, raw));

        // an unquoted program path runs to the first whitespace
        raw = @"MyApp.exe --veloapp-urlproto ""myapp://z""";
        Assert.Equal("myapp://z", VelopackApp.RecoverValueFromRawCommandLine(VelopackApp.HookCliUrlProto, raw));

        // flag absent as a token: null, so the caller falls back to argv
        Assert.Null(VelopackApp.RecoverValueFromRawCommandLine(VelopackApp.HookCliUrlProto, @"""C:\Apps\MyApp.exe"" --something-else"));
    }

    [Fact]
    public void ReadsPairsFromArgsWhenNoRawCommandLine()
    {
        // SetArgs override (or non-Windows / restarted): argv is authoritative and read as [flag, value] pairs
        var args = new[] { "--veloapp-urlproto", @"myapp://open?q=""quoted"" value" };
        Assert.Equal(
            new[] { (VelopackApp.HookCliUrlProto, @"myapp://open?q=""quoted"" value") },
            VelopackApp.ParseAssociationArgs(args, null));

        // several pairs of mixed kinds, eg. a macOS launch forwarded through an update
        args = new[] {
            "--veloapp-fileassoc", "/Users/me/a.myproj", "--VELOAPP-fileassoc", "/Users/me/b c.myproj", "--veloapp-urlproto", "myapp://x"
        };
        Assert.Equal(
            new[] {
                (VelopackApp.HookCliFileAssoc, "/Users/me/a.myproj"),
                (VelopackApp.HookCliFileAssoc, "/Users/me/b c.myproj"),
                (VelopackApp.HookCliUrlProto, "myapp://x"),
            },
            VelopackApp.ParseAssociationArgs(args, null));

        // parsing stops at the first element which is not a flag; the rest is left to the app
        args = new[] { "--veloapp-urlproto", "myapp://x", "other", "--veloapp-urlproto", "myapp://y" };
        Assert.Equal(new[] { (VelopackApp.HookCliUrlProto, "myapp://x") }, VelopackApp.ParseAssociationArgs(args, null));

        // a flag with nothing, or an empty string, after it is dropped
        args = new[] { "--veloapp-urlproto", "", "--veloapp-fileassoc", @"C:\x.myproj" };
        Assert.Equal(new[] { (VelopackApp.HookCliFileAssoc, @"C:\x.myproj") }, VelopackApp.ParseAssociationArgs(args, null));
    }

    [Fact]
    public void FallsBackToArgsWhenFlagMissingFromRawCommandLine()
    {
        // SetArgs with synthetic args on a direct Windows launch: the raw command line does not carry the flag
        var args = new[] { "--veloapp-urlproto", "myapp://fallback" };
        Assert.Equal(
            new[] { (VelopackApp.HookCliUrlProto, "myapp://fallback") },
            VelopackApp.ParseAssociationArgs(args, @"""C:\Apps\MyApp.exe"" --something-else"));

        // ... and the OS never passes more than one value, so only the first pair is taken (several pairs are only
        // ever forwarded by a restart, which reads argv without a raw command line)
        args = new[] { "--veloapp-urlproto", "myapp://first", "--veloapp-fileassoc", @"C:\second.myproj" };
        Assert.Equal(
            new[] { (VelopackApp.HookCliUrlProto, "myapp://first") },
            VelopackApp.ParseAssociationArgs(args, @"""C:\Apps\MyApp.exe"" --something-else"));
        Assert.Equal(2, VelopackApp.ParseAssociationArgs(args, null).Count);
    }

    [Fact]
    public void IgnoresEmptyValueAfterFlag()
    {
        Assert.Empty(VelopackApp.ParseAssociationArgs(new[] { "--veloapp-urlproto" }, null));
        Assert.Empty(VelopackApp.ParseAssociationArgs(new[] { "--veloapp-urlproto" }, @"MyApp.exe --veloapp-urlproto "));
        Assert.Empty(VelopackApp.ParseAssociationArgs(new[] { "--veloapp-urlproto", "" }, @"MyApp.exe --veloapp-urlproto """""));
    }

    [Theory]
    [InlineData((object) new string[0])]
    [InlineData((object) new[] { "--veloapp-install", "1.0.0" })]
    [InlineData((object) new[] { "myapp://not-a-flag" })]
    [InlineData((object) new[] { "C:\\file.myproj", "--veloapp-fileassoc" })]
    public void IgnoresNonAssociationArgs(string[] args)
    {
        Assert.Empty(VelopackApp.ParseAssociationArgs(args, "MyApp.exe --veloapp-fileassoc x"));
        Assert.Empty(VelopackApp.ParseAssociationArgs(args, null));
    }

    [Fact]
    public void RunInvokesUrlProtoCallbackAndContinues()
    {
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var locator = InstalledLocator(tempPath, logger);

        var received = new List<string>();
        var fileReceived = new List<string>();
        VelopackApp.Build()
            .SetLocator(locator)
            .SetArgs(new[] { "--veloapp-urlproto", "myapp://open?id=42" })
            .OnFileAssoc(fileReceived.Add)
            .OnUrlProto(received.Add)
            .Run();

        Assert.Equal(new[] { "myapp://open?id=42" }, received);
        Assert.Empty(fileReceived);
        Assert.Null(locator.ExitCode); // not a fast-exit hook, startup continues
    }

    private static readonly string[] TwoFilePairs = {
        "--veloapp-fileassoc", @"C:\Users\me\My Documents\file.myproj", "--veloapp-fileassoc", @"C:\other.myproj"
    };

    [Fact]
    public void RunInvokesFileAssocCallbackOncePerPairWhenRestarted()
    {
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var locator = InstalledLocator(tempPath, logger);

        // several pairs are only ever forwarded by a Velopack restart (Run() clears the variable again)
        var received = new List<string>();
        var restarted = false;
        Environment.SetEnvironmentVariable("VELOPACK_RESTART", "true");
        try {
            VelopackApp.Build()
                .SetLocator(locator)
                .SetArgs(TwoFilePairs)
                .OnRestarted(_ => restarted = true)
                .OnFileAssoc(received.Add)
                .Run();
        } finally {
            Environment.SetEnvironmentVariable("VELOPACK_RESTART", null);
        }

        Assert.True(restarted);
        Assert.Equal(new[] { @"C:\Users\me\My Documents\file.myproj", @"C:\other.myproj" }, received);
        Assert.Null(locator.ExitCode);
    }

    [Fact]
    public void RunDirectLaunchTakesAtMostOnePairOnWindows()
    {
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var locator = InstalledLocator(tempPath, logger);

        // not restarted: on Windows the real command line (the test host's, without the flag) is consulted and the OS
        // never passes more than one value, so only the first pair of the synthetic args counts. Elsewhere argv is exact.
        var received = new List<string>();
        VelopackApp.Build()
            .SetLocator(locator)
            .SetArgs(TwoFilePairs)
            .OnFileAssoc(received.Add)
            .Run();

        if (VelopackRuntimeInfo.IsWindows) {
            Assert.Equal(new[] { @"C:\Users\me\My Documents\file.myproj" }, received);
        } else {
            Assert.Equal(new[] { @"C:\Users\me\My Documents\file.myproj", @"C:\other.myproj" }, received);
        }

        Assert.Null(locator.ExitCode);
    }

    [Fact]
    public void RunInvokesCallbackAfterFirstRunHook()
    {
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var locator = InstalledLocator(tempPath, logger);

        var order = new List<string>();
        Environment.SetEnvironmentVariable("VELOPACK_FIRSTRUN", "true");
        try {
            VelopackApp.Build()
                .SetLocator(locator)
                .SetArgs(new[] { "--veloapp-urlproto", "myapp://x" })
                .OnFirstRun(_ => order.Add("firstrun"))
                .OnUrlProto(_ => order.Add("url"))
                .Run();
        } finally {
            Environment.SetEnvironmentVariable("VELOPACK_FIRSTRUN", null);
        }

        Assert.Equal(new[] { "firstrun", "url" }, order);
        Assert.Null(Environment.GetEnvironmentVariable("VELOPACK_FIRSTRUN"));
    }

    [Fact]
    public void RunSwallowsCallbackExceptions()
    {
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var locator = InstalledLocator(tempPath, logger);

        VelopackApp.Build()
            .SetLocator(locator)
            .SetArgs(new[] { "--veloapp-urlproto", "myapp://boom" })
            .OnUrlProto(_ => throw new InvalidOperationException("boom"))
            .Run();

        Assert.Null(locator.ExitCode);
        Assert.Contains(logger.Entries, e => e.Contains("--veloapp-urlproto") && e.Contains("boom"));
    }

    [Fact]
    public void RunWithoutCallbackLeavesAssociationArgsAlone()
    {
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var locator = InstalledLocator(tempPath, logger);

        var fileReceived = new List<string>();
        VelopackApp.Build()
            .SetLocator(locator)
            .SetArgs(new[] { "--veloapp-urlproto", "myapp://x" })
            .OnFileAssoc(fileReceived.Add)
            .Run();

        Assert.Empty(fileReceived);
        Assert.Null(locator.ExitCode);
    }

    /// <summary>
    /// A root for one SingleInstance test. The IPC servers these tests start live for the whole test process, holding
    /// the lock of their endpoint (keyed on the root path), and <see cref="TempUtil.GetTempDirectory"/> reuses its
    /// <c>temp.N</c> names once disposed, so every test needs a root path no other test can get again.
    /// </summary>
    private static string UniqueRoot(string tempPath)
    {
        var root = Path.Combine(tempPath, "Root " + Guid.NewGuid().ToString("N").Substring(0, 8));
        Directory.CreateDirectory(Path.Combine(root, "packages"));
        return root;
    }

    /// <summary>
    /// An installed-looking locator with its packages and root dir at <paramref name="tempPath"/>: an installed app
    /// always has a root dir, which the macOS startup reads to register the association helper.
    /// </summary>
    private static TestVelopackLocator InstalledLocator(string tempPath, IVelopackLogger logger)
    {
        return new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, null, tempPath, null, logger: logger);
    }

    /// <summary> An installed-looking locator whose manifest says SingleInstance, rooted at <paramref name="root"/>. </summary>
    private static TestVelopackLocator SingleInstanceLocator(string root, IVelopackLogger logger)
    {
        var packages = Path.Combine(root, "packages");
        return new TestVelopackLocator("MyCoolApp", "1.0.0", packages, null, root, null, logger: logger) { AssociationSingleInstance = true };
    }

    private static IpcMessage UrlActivation(string value) => new(IpcProtocol.TypeAssociation) {
        Items = new List<IpcItem> { new(VelopackApp.HookCliUrlProto, value) },
    };

    private static void WaitFor(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition() && DateTime.UtcNow < deadline) {
            Thread.Sleep(20);
        }

        Assert.True(condition(), "condition not met within 5 s");
    }

    [Fact]
    public void RunSingleInstanceBecomesServerAndReceivesForwarded()
    {
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);
        var locator = SingleInstanceLocator(root, logger);

        var received = new List<(string Value, int ThreadId)>();
        VelopackApp.Build()
            .SetLocator(locator)
            .SetArgs(new[] { "--veloapp-urlproto", "myapp://a" })
            .OnUrlProto(v => { lock (received) received.Add((v, Environment.CurrentManagedThreadId)); })
            .Run();

        // the first instance handles its own activation inline and becomes the server
        Assert.Null(locator.ExitCode);
        Assert.Equal(new[] { ("myapp://a", Environment.CurrentManagedThreadId) }, received);
        Assert.Contains(logger.Entries, e => e.Contains("serving association activations"));

        // a forwarded activation is acked after the callback ran, on a Velopack IPC thread
        var ep = IpcEndpoint.ForApp("MyCoolApp", root, logger);
        Assert.NotNull(ep);
        using (var conn = IpcServerClientTests.ConnectWithRetries(ep)) {
            Assert.True(conn.Request(UrlActivation("myapp://b"), IpcProtocol.ExchangeTimeout).IsAck);
        }

        WaitFor(() => { lock (received) return received.Count == 2; });
        Assert.Equal("myapp://b", received[1].Value);
        Assert.NotEqual(Environment.CurrentManagedThreadId, received[1].ThreadId);
        Assert.DoesNotContain(logger.Entries, e => e.Contains("myapp://b")); // values never reach the log

        // an unregistered flag is refused (and nothing is invoked)
        using (var conn = IpcServerClientTests.ConnectWithRetries(ep)) {
            var reply = conn.Request(
                new IpcMessage(IpcProtocol.TypeAssociation) { Items = new List<IpcItem> { new(VelopackApp.HookCliFileAssoc, @"C:\a.myproj") } },
                IpcProtocol.ExchangeTimeout);
            Assert.Equal((IpcProtocol.TypeError, IpcProtocol.ErrUnhandledAssociation), (reply.Type, reply.Message));
        }

        using (var conn = IpcServerClientTests.ConnectWithRetries(ep)) {
            var reply = conn.Request(new IpcMessage("bogus"), IpcProtocol.ExchangeTimeout);
            Assert.Equal((IpcProtocol.TypeError, IpcProtocol.ErrUnknownMessageType), (reply.Type, reply.Message));
        }

        Assert.Equal(2, received.Count);
    }

    [Fact]
    public void RunSingleInstanceServerIsUpBeforeFirstRunHook()
    {
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);
        var locator = SingleInstanceLocator(root, logger);

        var received = new List<string>();
        var firstRunEntered = new ManualResetEventSlim();
        var releaseFirstRun = new ManualResetEventSlim();
        Environment.SetEnvironmentVariable("VELOPACK_FIRSTRUN", "true");
        var runThread = new Thread(() => {
            try {
                VelopackApp.Build()
                    .SetLocator(locator)
                    .SetArgs(new string[0])
                    .OnFirstRun(_ => {
                        firstRunEntered.Set();
                        releaseFirstRun.Wait(TimeSpan.FromSeconds(10));
                    })
                    .OnUrlProto(v => { lock (received) received.Add(v); })
                    .Run();
            } finally {
                Environment.SetEnvironmentVariable("VELOPACK_FIRSTRUN", null);
            }
        });
        runThread.Start();

        try {
            Assert.True(firstRunEntered.Wait(TimeSpan.FromSeconds(10)));
            // the server accepts while the first-run hook (arbitrary user code) is still blocking the main thread
            var ep = IpcEndpoint.ForApp("MyCoolApp", root, logger);
            Assert.NotNull(ep);
            using var conn = IpcServerClientTests.ConnectWithRetries(ep);
            Assert.True(conn.Request(UrlActivation("myapp://early"), IpcProtocol.ExchangeTimeout).IsAck);
            Assert.False(releaseFirstRun.IsSet);
            Assert.Equal(new[] { "myapp://early" }, received);
        } finally {
            releaseFirstRun.Set();
            runThread.Join();
        }

        Assert.Null(locator.ExitCode);
    }

    [Fact]
    public void RunSingleInstanceForwardsAndExits()
    {
        // POSIX record locks are per process, so a second election in the same process cannot observe a held lock
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "per-process POSIX locks");
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);

        // instance A: elected server, serving the url callback
        var locatorA = SingleInstanceLocator(root, logger);
        var receivedA = new List<string>();
        var appA = VelopackApp.Build().OnUrlProto(v => { lock (receivedA) receivedA.Add(v); });
        Assert.Equal(Election.Server, SingleInstance.Elect(locatorA, new(), logger, out var server));
        SingleInstance.Start(server!, appA, logger);

        // instance B: launched for an association, forwards it and exits with 0 before anything else
        var locatorB = SingleInstanceLocator(root, logger);
        var receivedB = new List<string>();
        var value = "myapp://open?q=\"a b\"";
        VelopackApp.Build()
            .SetLocator(locatorB)
            .SetArgs(new[] { "--veloapp-urlproto", value })
            .OnUrlProto(receivedB.Add)
            .Run();

        Assert.Equal(0, locatorB.ExitCode);
        Assert.Empty(receivedB);
        Assert.Equal(new[] { value }, receivedA);
        Assert.Contains(logger.Entries, e => e.Contains("Forwarded 1 association(s)"));
    }

    [Fact]
    public void RunSingleInstancePlainLaunchContinues()
    {
        // POSIX record locks are per process, so a second election in the same process cannot observe a held lock;
        // IpcInteropTests.RustServerCSharpPlainLaunchContinues covers Unix cross-process
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "per-process POSIX locks");
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);

        // another instance holds the lock
        var locatorA = SingleInstanceLocator(root, logger);
        Assert.Equal(Election.Server, SingleInstance.Elect(locatorA, new(), logger, out var server));
        SingleInstance.Start(server!, VelopackApp.Build().OnUrlProto(_ => { }), logger);

        var locatorB = SingleInstanceLocator(root, logger);
        VelopackApp.Build()
            .SetLocator(locatorB)
            .SetArgs(new[] { "--not-an-association", "x" })
            .OnUrlProto(_ => { })
            .Run();

        Assert.Null(locatorB.ExitCode);
    }

    [Fact]
    public void RunSingleInstanceForwardsOnlyItemsWithACallback()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "per-process POSIX locks");
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);

        // instance A: url callback only (the server refuses a message carrying any item it has no callback for)
        var locatorA = SingleInstanceLocator(root, logger);
        var receivedA = new List<string>();
        Assert.Equal(Election.Server, SingleInstance.Elect(locatorA, new(), logger, out var server));
        SingleInstance.Start(server!, VelopackApp.Build().OnUrlProto(v => { lock (receivedA) receivedA.Add(v); }), logger);

        // instance B (the same build): a mixed launch, eg. the macOS helper coalescing a file and a url, forwards only
        // the url and exits; the file item has no callback here either and is dropped
        var locatorB = SingleInstanceLocator(root, logger);
        Environment.SetEnvironmentVariable("VELOPACK_RESTART", "true"); // several pairs are only read for restarted launches
        try {
            VelopackApp.Build()
                .SetLocator(locatorB)
                .SetArgs(new[] { "--veloapp-fileassoc", @"C:\x.myproj", "--veloapp-urlproto", "myapp://a" })
                .OnUrlProto(_ => Assert.Fail("B must not handle the url itself"))
                .Run();
        } finally {
            Environment.SetEnvironmentVariable("VELOPACK_RESTART", null);
        }

        Assert.Equal(0, locatorB.ExitCode);
        Assert.Equal(new[] { "myapp://a" }, receivedA);
        Assert.Contains(logger.Entries, e => e.Contains("Forwarded 1 association(s)"));
    }

    [Fact]
    public void RunSingleInstanceInvalidUnicodeIsHandledInProcess()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "per-process POSIX locks");
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var root = UniqueRoot(tempPath);

        var locatorA = SingleInstanceLocator(root, logger);
        var receivedA = new List<string>();
        Assert.Equal(Election.Server, SingleInstance.Elect(locatorA, new(), logger, out var server));
        SingleInstance.Start(server!, VelopackApp.Build().OnFileAssoc(receivedA.Add), logger);

        // NTFS allows a lone surrogate in a file name; JSON cannot carry it, so it is handled in-process, not altered
        var lone = "C:\\Users\\me\\bad\uD800name.myproj";
        var locatorB = SingleInstanceLocator(root, logger);
        var receivedB = new List<string>();
        VelopackApp.Build()
            .SetLocator(locatorB)
            .SetArgs(new[] { "--veloapp-fileassoc", lone })
            .OnFileAssoc(receivedB.Add)
            .Run();

        Assert.Null(locatorB.ExitCode);
        Assert.Equal(new[] { lone }, receivedB);
        Assert.Empty(receivedA);
        Assert.Contains(logger.Entries, e => e.Contains("not valid Unicode"));
    }

    [Fact]
    public void RunIgnoresEmptyAssociationValue()
    {
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var locator = InstalledLocator(tempPath, logger);

        var received = new List<string>();
        VelopackApp.Build()
            .SetLocator(locator)
            .SetArgs(new[] { "--veloapp-urlproto" })
            .OnUrlProto(received.Add)
            .Run();

        Assert.Empty(received);
        Assert.Contains(logger.Entries, e => e.Contains("--veloapp-urlproto was passed without a value"));
    }
}
