#nullable enable
using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using Velopack.Chunks;
using Velopack.Sources;
using Velopack.TestCommon;
using Velopack.Util;

namespace Velopack.Tests.Chunks;

/// <summary>
/// <see cref="HttpRangeReader"/> behaviour that the fetcher tests do not pin: timeouts and cancellation of a body read on
/// a response stream that ignores its token (as .NET Framework's does), and which requests carry the source's
/// <c>Authorization</c> header across redirects (§5.5).
/// </summary>
public class HttpRangeReaderTests
{
    private readonly ITestOutputHelper _output;

    public HttpRangeReaderTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// A body stream like .NET Framework's <c>ConnectStream</c>: it delivers <c>prefix</c> bytes, then blocks every read
    /// until it is disposed, never observing a cancellation token.
    /// </summary>
    private sealed class TokenIgnoringStream : Stream
    {
        private readonly TaskCompletionSource<int> _disposed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _prefix;

        public TokenIgnoringStream(int prefix)
        {
            _prefix = prefix;
        }

        public bool IsDisposed => _disposed.Task.IsCompleted;

        public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        {
            if (_prefix > 0) {
                int n = Math.Min(count, _prefix);
                _prefix -= n;
                return Task.FromResult(n);
            }
            return _disposed.Task.ContinueWith<int>(_ => throw new IOException("The connection was aborted."), TaskScheduler.Default);
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            return new ValueTask<int>(ReadAsync(new byte[buffer.Length], 0, buffer.Length, CancellationToken.None));
        }

        public override int Read(byte[] buffer, int offset, int count) => ReadAsync(buffer, offset, count, CancellationToken.None).GetAwaiter().GetResult();

        protected override void Dispose(bool disposing)
        {
            _disposed.TrySetResult(0);
            base.Dispose(disposing);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Answers every request with a valid 206 whose body stalls after <c>prefix</c> bytes.</summary>
    private sealed class StallingHandler : HttpMessageHandler
    {
        private readonly int _prefix;
        public readonly List<TokenIgnoringStream> Streams = new();

        public StallingHandler(int prefix)
        {
            _prefix = prefix;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var range = request.Headers.Range!.Ranges.Single();
            long length = range.To!.Value - range.From!.Value + 1;
            var stream = new TokenIgnoringStream(_prefix);
            lock (Streams) Streams.Add(stream);
            var content = new StreamContent(stream);
            content.Headers.ContentLength = length;
            content.Headers.ContentRange = new ContentRangeHeaderValue(range.From.Value, range.To.Value, 1_000_000);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.PartialContent) { Content = content, RequestMessage = request });
        }
    }

    [Fact]
    public async Task StallTimeoutAbortsReadThatIgnoresItsToken()
    {
        var handler = new StallingHandler(prefix: 10);
        using var reader = new HttpRangeReader(new HttpClient(handler), "http://example.invalid/pkg.nupkg", null, 0) {
            ReadStallTimeout = TimeSpan.FromMilliseconds(300),
        };

        var sw = Stopwatch.StartNew();
        var ex = await Assert.ThrowsAsync<RangeTransientException>(() => reader.ReadExactAsync(0, new byte[100], 0, 100, CancellationToken.None));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"the stalled read took {sw.Elapsed}");
        Assert.Contains("timed out", ex.Message);
        // the connection was torn down rather than left blocked
        Assert.True(handler.Streams.Single().IsDisposed);
        Assert.Equal(RetryClass.Transient, RetryPolicy.Classify(ex, CancellationToken.None));
    }

    [Fact]
    public async Task CallerCancelAbortsReadThatIgnoresItsToken()
    {
        var handler = new StallingHandler(prefix: 10);
        using var reader = new HttpRangeReader(new HttpClient(handler), "http://example.invalid/pkg.nupkg", null, 0) {
            ReadStallTimeout = TimeSpan.FromMinutes(5),
        };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => reader.ReadExactAsync(0, new byte[100], 0, 100, cts.Token));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"the cancel took {sw.Elapsed}");
        Assert.True(handler.Streams.Single().IsDisposed);
    }

    [Fact]
    public async Task AuthorizationOnlySentToTheOriginalOrigin()
    {
        using var _ = TempUtil.GetTempDirectory(out var dir);
        using var logger = _output.BuildLoggerFor<HttpRangeReaderTests>();
        var bytes = new byte[300_000];
        new Random(7).NextBytes(bytes);
        File.WriteAllBytes(Path.Combine(dir, "pkg.nupkg"), bytes);
        // "api" resolves downloads with a redirect, either to itself (same origin) or to "cdn" (another port: another origin)
        using var api = new StaticFileServer(dir, logger);
        using var cdn = new StaticFileServer(dir, logger);
        api.Redirect("/cross/pkg", n => cdn.BaseUrl + $"pkg.nupkg?sig={n}");
        api.Redirect("/same/pkg", n => api.BaseUrl + $"pkg.nupkg?sig={n}");
        // the first signed cdn url "expires" after a few reads, forcing a re-resolution through api
        cdn.Faults.AddRule((req, ordinal) => req.PathAndQuery.EndsWith("?sig=1") && ordinal == 3 ? Fault.Status(403) : Fault.None);

        var headers = new Dictionary<string, string> { ["Authorization"] = "Bearer secret", ["X-Test"] = "1" };
        var downloader = new HttpClientFileDownloader();

        using (var reader = downloader.OpenRangeReader(api.BaseUrl + "cross/pkg", headers, 30)) {
            var buffer = new byte[1000];
            for (int i = 0; i < 6; i++) {
                try {
                    await reader.ReadExactAsync(i * 1000, buffer, 0, buffer.Length, CancellationToken.None);
                } catch (RangeRefreshException) {
                    await reader.ReadExactAsync(i * 1000, buffer, 0, buffer.Length, CancellationToken.None);
                }
                Assert.Equal(bytes.Skip(i * 1000).Take(1000), buffer);
            }
        }
        // every resolution went to api with the token; nothing that reached cdn (first hop, cached url, refreshed url) had it
        Assert.Equal(2, api.RedirectHits("/cross/pkg"));
        Assert.All(api.Requests, r => Assert.Equal("Bearer secret", r.Headers["Authorization"]));
        Assert.Contains(cdn.Requests, r => r.PathAndQuery.EndsWith("?sig=1"));
        Assert.Contains(cdn.Requests, r => r.PathAndQuery.EndsWith("?sig=2"));
        Assert.All(cdn.Requests, r => {
            Assert.False(r.Headers.ContainsKey("Authorization"), $"{r.PathAndQuery} carried Authorization");
            Assert.Equal("1", r.Headers["X-Test"]);
        });

        // a redirect within the same origin keeps the token on every request
        int before = api.Requests.Count;
        using (var reader = downloader.OpenRangeReader(api.BaseUrl + "same/pkg", headers, 30)) {
            var buffer = new byte[1000];
            for (int i = 0; i < 3; i++) await reader.ReadExactAsync(i * 1000, buffer, 0, buffer.Length, CancellationToken.None);
        }
        var sameOrigin = api.Requests.Skip(before).ToList();
        Assert.Equal(4, sameOrigin.Count);
        Assert.All(sameOrigin, r => {
            Assert.Equal("Bearer secret", r.Headers["Authorization"]);
            Assert.Equal("1", r.Headers["X-Test"]);
        });
    }
}
