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
    [InlineData(@"""C:\Apps\MyApp\current\MyApp.exe"" --veloapp-file-association ""C:\Users\me\My Documents\file.myproj""",
        new[] { "--veloapp-file-association", @"C:\Users\me\My Documents\file.myproj" }, @"C:\Users\me\My Documents\file.myproj")]
    // URL with embedded quotes: argv would collapse the quotes, the raw command line keeps them
    [InlineData(@"""C:\Apps\MyApp\current\MyApp.exe"" --veloapp-url-association ""myapp://open?q=""quoted"" value""",
        new[] { "--veloapp-url-association", "myapp://open?q=quoted value" }, @"myapp://open?q=""quoted"" value")]
    // unquoted value
    [InlineData(@"""C:\Apps\MyApp\current\MyApp.exe"" --veloapp-url-association myapp://plain",
        new[] { "--veloapp-url-association", "myapp://plain" }, "myapp://plain")]
    // extra whitespace around the flag and value
    [InlineData("  \"C:\\Apps\\MyApp\\current\\MyApp.exe\"   --veloapp-url-association \t  \"myapp://spaced\"   ",
        new[] { "--veloapp-url-association", "myapp://spaced" }, "myapp://spaced")]
    // flag casing is not significant, like the other veloapp hooks
    [InlineData(@"MyApp.exe --VeloApp-Url-Association ""myapp://case""",
        new[] { "--VeloApp-Url-Association", "myapp://case" }, "myapp://case")]
    // unquoted path with spaces (argv would have split it); everything after the flag is the value
    [InlineData(@"MyApp.exe --veloapp-file-association C:\Users\me\My Documents\file.myproj",
        new[] { "--veloapp-file-association", @"C:\Users\me\My", @"Documents\file.myproj" }, @"C:\Users\me\My Documents\file.myproj")]
    // the exe path containing the flag text must not be mistaken for the flag token
    [InlineData(@"""C:\--veloapp-url-association\MyApp.exe"" --veloapp-url-association ""myapp://x""",
        new[] { "--veloapp-url-association", "myapp://x" }, "myapp://x")]
    // dotnet-hosted launch: the host and dll come first
    [InlineData(@"""C:\Program Files\dotnet\dotnet.exe"" ""C:\Apps\MyApp\MyApp.dll"" --veloapp-url-association ""myapp://hosted""",
        new[] { "--veloapp-url-association", "myapp://hosted" }, "myapp://hosted")]
    public void RecoversValueFromRawCommandLine(string rawCommandLine, string[] args, string expected)
    {
        Assert.True(VelopackApp.TryParseAssociationArgs(args, rawCommandLine, out var flag, out var value));
        Assert.Equal(args[0].ToLowerInvariant(), flag);
        Assert.Equal(expected, value);
    }

    [Fact]
    public void RawCommandLineProgramPathIsSkipped()
    {
        // mirrors the Rust test: the flag as a separate word inside the program path, or as a prefix of a longer token,
        // is not the flag. Only the program path (first token) is skipped, so the real flag later on is still found.
        var raw = @"""C:\dir --veloapp-url-association \MyApp.exe"" --veloapp-url-association-extra 1 --veloapp-url-association ""myapp://y""";
        var args = new[] { "--veloapp-url-association", "myapp://fallback" };
        Assert.Equal("myapp://y", VelopackApp.RecoverAssociationValue(VelopackApp.HookCliUrlAssociation, args, raw));

        // an unquoted program path runs to the first whitespace
        raw = @"MyApp.exe --veloapp-url-association ""myapp://z""";
        Assert.Equal("myapp://z", VelopackApp.RecoverAssociationValue(VelopackApp.HookCliUrlAssociation, args, raw));
    }

    [Fact]
    public void RecoversValueFromArgsWhenNoRawCommandLine()
    {
        // SetArgs override (or non-Windows / restarted): argv is authoritative, args[1..] joined with a space
        var args = new[] { "--veloapp-url-association", @"myapp://open?q=""quoted"" value" };
        Assert.True(VelopackApp.TryParseAssociationArgs(args, null, out var flag, out var value));
        Assert.Equal(VelopackApp.HookCliUrlAssociation, flag);
        Assert.Equal(@"myapp://open?q=""quoted"" value", value);

        args = new[] { "--veloapp-file-association", @"C:\Users\me\My", @"Documents\file.myproj" };
        Assert.True(VelopackApp.TryParseAssociationArgs(args, null, out flag, out value));
        Assert.Equal(VelopackApp.HookCliFileAssociation, flag);
        Assert.Equal(@"C:\Users\me\My Documents\file.myproj", value);
    }

    [Fact]
    public void FallsBackToArgsWhenFlagMissingFromRawCommandLine()
    {
        var args = new[] { "--veloapp-url-association", "myapp://fallback" };
        Assert.True(VelopackApp.TryParseAssociationArgs(args, @"""C:\Apps\MyApp.exe"" --something-else", out _, out var value));
        Assert.Equal("myapp://fallback", value);
    }

    [Fact]
    public void RecoversEmptyValueWhenNothingFollowsFlag()
    {
        Assert.True(VelopackApp.TryParseAssociationArgs(new[] { "--veloapp-url-association" }, null, out _, out var value));
        Assert.Equal("", value);
        Assert.True(
            VelopackApp.TryParseAssociationArgs(new[] { "--veloapp-url-association" }, @"MyApp.exe --veloapp-url-association ", out _, out value));
        Assert.Equal("", value);
    }

    [Theory]
    [InlineData((object) new string[0])]
    [InlineData((object) new[] { "--veloapp-install", "1.0.0" })]
    [InlineData((object) new[] { "myapp://not-a-flag" })]
    [InlineData((object) new[] { "C:\\file.myproj", "--veloapp-file-association" })]
    public void IgnoresNonAssociationArgs(string[] args)
    {
        Assert.False(VelopackApp.TryParseAssociationArgs(args, "MyApp.exe --veloapp-file-association x", out _, out _));
    }

    [Fact]
    public void RunInvokesUrlAssociationCallbackAndContinues()
    {
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger);

        var received = new List<string>();
        var fileReceived = new List<string>();
        VelopackApp.Build()
            .SetLocator(locator)
            .SetArgs(new[] { "--veloapp-url-association", "myapp://open?id=42" })
            .OnFileAssociation(fileReceived.Add)
            .OnUrlAssociation(received.Add)
            .Run();

        Assert.Equal(new[] { "myapp://open?id=42" }, received);
        Assert.Empty(fileReceived);
        Assert.Null(locator.ExitCode); // not a fast-exit hook, startup continues
    }

    [Fact]
    public void RunInvokesFileAssociationCallback()
    {
        var logger = new ListLogger(_output);
        using var _1 = TempUtil.GetTempDirectory(out var tempPath);
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger);

        var received = new List<string>();
        VelopackApp.Build()
            .SetLocator(locator)
            .SetArgs(new[] { "--veloapp-file-association", @"C:\Users\me\My Documents\file.myproj" })
            .OnFileAssociation(received.Add)
            .Run();

        Assert.Equal(new[] { @"C:\Users\me\My Documents\file.myproj" }, received);
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
                .SetArgs(new[] { "--veloapp-url-association", "myapp://x" })
                .OnFirstRun(_ => order.Add("firstrun"))
                .OnUrlAssociation(_ => order.Add("url"))
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
            .SetArgs(new[] { "--veloapp-url-association", "myapp://boom" })
            .OnUrlAssociation(_ => throw new InvalidOperationException("boom"))
            .Run();

        Assert.Null(locator.ExitCode);
        Assert.Contains(logger.Entries, e => e.Contains("--veloapp-url-association") && e.Contains("boom"));
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
            .SetArgs(new[] { "--veloapp-url-association", "myapp://x" })
            .OnFileAssociation(fileReceived.Add)
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
            .SetArgs(new[] { "--veloapp-url-association" })
            .OnUrlAssociation(received.Add)
            .Run();

        Assert.Empty(received);
    }
}
