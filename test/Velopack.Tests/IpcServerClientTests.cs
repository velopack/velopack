#nullable enable
using System.Diagnostics;
using Velopack.Ipc;
using Velopack.Logging;

namespace Velopack.Tests;

public class IpcServerClientTests
{
    private sealed class ListLogger : IVelopackLogger
    {
        public List<string> Entries { get; } = new();

        public void Log(VelopackLogLevel logLevel, string? message, Exception? exception)
        {
            lock (Entries) Entries.Add($"[{logLevel}] {message} {exception}".TrimEnd());
        }
    }

    internal static IpcEndpoint RandomEndpoint(IVelopackLogger log)
    {
        var ep = IpcEndpoint.FromKey("test-" + Guid.NewGuid().ToString("N").Substring(0, 12), log);
        Assert.NotNull(ep);
        return ep;
    }

    internal static IpcConnection ConnectWithRetries(IpcEndpoint ep, TimeSpan? window = null)
    {
        var timer = Stopwatch.StartNew();
        while (true) {
            var conn = IpcClient.TryConnect(ep);
            if (conn != null) {
                return conn;
            }

            Assert.True(timer.Elapsed < (window ?? TimeSpan.FromSeconds(5)), "no server at " + ep.Address);
            Thread.Sleep(20);
        }
    }

    private static IpcMessage Association(string value) => new(IpcProtocol.TypeAssociation) {
        Items = new List<IpcItem> { new(VelopackApp.HookCliUrlProto, value) },
    };

    [Fact]
    public void ServerClientRoundTrip300Times()
    {
        var log = new ListLogger();
        var ep = RandomEndpoint(log);
        var received = new List<string>();
        var started = IpcServer.Start(
            ep,
            null,
            conn => {
                using (conn) {
                    conn.ServeRequest(msg => {
                        lock (received) received.AddRange(msg.Items!.Select(i => i.Value));
                        return IpcMessage.Ack().ReplyTo(msg);
                    }, log);
                }
            },
            log);
        Assert.True(started, string.Join("\n", log.Entries));

        // a lost ack from a bad close sequence shows up as a timeout somewhere in the loop
        for (var i = 0; i < 300; i++) {
            using var conn = ConnectWithRetries(ep);
            var reply = conn.Request(Association("myapp://" + i), IpcProtocol.ExchangeTimeout);
            Assert.True(reply.IsAck, $"round {i}: {reply.Type} {reply.Message}");
        }

        lock (received) {
            Assert.Equal(300, received.Count);
            Assert.Equal("myapp://299", received[299]);
        }
    }

    [Fact]
    public void ConnectWithoutServerIsNull()
    {
        var ep = RandomEndpoint(NullVelopackLogger.Instance);
        Assert.Null(IpcClient.TryConnect(ep));
    }

    [Fact]
    public void ReceiveTimesOutWhenPeerIsSilent()
    {
        var log = new ListLogger();
        var ep = RandomEndpoint(log);
        // the server holds every connection open for a while without writing anything
        Assert.True(IpcServer.Start(ep, null, conn => { Thread.Sleep(3000); conn.Dispose(); }, log));
        using var client = ConnectWithRetries(ep);
        client.SetTimeout(TimeSpan.FromMilliseconds(300));
        var timer = Stopwatch.StartNew();
        Assert.Throws<TimeoutException>(() => client.Receive());
        Assert.True(timer.Elapsed < TimeSpan.FromSeconds(2), $"took {timer.Elapsed}");
    }

    [Fact]
    public void ConnectRefusesSquattedPipe()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "named pipes are a Windows feature");
        // a pipe only SYSTEM may open: our own CreateFile fails with access denied, which must be an error (never
        // "nobody listening", which would make the caller retry and eventually handle in-process)
        var ep = RandomEndpoint(NullVelopackLogger.Instance);
        var descriptor = IpcNative.CreateSecurityDescriptor("D:P(A;;GA;;;SY)");
        var squatter = IpcNative.CreatePipeInstance(ep.Address, descriptor, true);
        try {
            var ex = Assert.Throws<UnauthorizedAccessException>(() => IpcClient.TryConnect(ep));
            Assert.Contains("access denied", ex.Message);
        } finally {
            IpcNative.CloseHandle(squatter);
            IpcNative.LocalFree(descriptor);
        }
    }

    [Fact]
    public void LockFileReportsContentionAndToleratesSharedOpens()
    {
        using var _1 = Velopack.Util.TempUtil.GetTempDirectory(out var tempPath);
        var path = Path.Combine(tempPath, "test.lock");
        using var holder = new Velopack.Util.LockFile(path);
        Assert.Equal(Velopack.Util.LockAttempt.Acquired, holder.TryLockOnce());
        Assert.True(holder.IsLocked);

        if (VelopackRuntimeInfo.IsWindows) {
            // a plain open by a third party (indexer, antivirus) must not disturb the holder, and only a real byte-range
            // lock conflict counts as contention
            using (File.Open(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite | FileShare.Delete)) {
                using var second = new Velopack.Util.LockFile(path);
                Assert.Equal(Velopack.Util.LockAttempt.Held, second.TryLockOnce());
            }
        }

        holder.Dispose();
        using var third = new Velopack.Util.LockFile(path);
        Assert.Equal(Velopack.Util.LockAttempt.Acquired, third.TryLockOnce());
    }

    [Fact]
    public void TokenSidsOfCurrentProcess()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "token SIDs are a Windows feature");
        var (user, level) = IpcNative.TokenSids(IpcNative.GetCurrentProcess());
        Assert.StartsWith("S-1-5-", user);
        Assert.StartsWith("S-1-16-", level);
    }
}
