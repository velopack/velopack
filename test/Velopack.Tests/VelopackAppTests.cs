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
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger);

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
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger);

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
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger);

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
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger);

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
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger);

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
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger);

        var fileReceived = new List<string>();
        VelopackApp.Build()
            .SetLocator(locator)
            .SetArgs(new[] { "--veloapp-urlproto", "myapp://x" })
            .OnFileAssoc(fileReceived.Add)
            .Run();

        Assert.Empty(fileReceived);
        Assert.Null(locator.ExitCode);
    }

    [Fact]
    public void RunIgnoresEmptyAssociationValue()
    {
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger);

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
