#nullable enable
using System.Diagnostics;
using Velopack.Chunks;
using Velopack.Core;
using Velopack.Logging;
using Velopack.Sources;
using Velopack.TestCommon;
using Velopack.Util;

namespace Velopack.Tests.Chunks;

/// <summary>
/// Ranged fetch with fault injection (§12.3): a real <see cref="HttpClientFileDownloader"/> range reader against the
/// in-process <see cref="StaticFileServer"/>, with the retry policy's sleep and clock injected so nothing waits.
/// </summary>
public class ChunkFetcherTests
{
    private readonly ITestOutputHelper _output;

    public ChunkFetcherTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private const string PackageFile = "pkg.nupkg";

    /// <summary>A package served by the file server plus a fetcher wired to it.</summary>
    private sealed class Harness : IDisposable
    {
        private readonly IDisposable _tempDir;
        public readonly string Dir;
        public readonly TestPackage Pkg;
        public readonly StaticFileServer Server;
        public readonly HttpRangeReader Reader;
        public readonly RetryPolicy Policy;
        public readonly ChunkFetcher Fetcher;
        public readonly ChunkBlob Blob;
        public readonly List<TimeSpan> Sleeps = new();
        public DateTime Now = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Utc);
        public readonly ILogger Logger;
        public readonly IVelopackLogger VeloLog;

        public Harness(ITestOutputHelper output, int parallelism = 4, TestPackage? pkg = null, string requestPath = "/" + PackageFile,
            Action<StaticFileServer>? configureServer = null)
        {
            _tempDir = TempUtil.GetTempDirectory(out Dir);
            Logger = output.BuildLoggerFor<ChunkFetcherTests>();
            VeloLog = Logger.ToVelopackLogger();
            Pkg = pkg ?? DefaultPackage();
            var serveDir = Path.Combine(Dir, "serve");
            Directory.CreateDirectory(serveDir);
            File.WriteAllBytes(Path.Combine(serveDir, PackageFile), Pkg.Bytes);
            Server = new StaticFileServer(serveDir, Logger);
            configureServer?.Invoke(Server);
            Reader = (HttpRangeReader) new HttpClientFileDownloader().OpenRangeReader(Server.BaseUrl.TrimEnd('/') + requestPath, null, 30);
            Policy = new RetryPolicy(randomSeed: 1234) {
                Clock = () => Now,
                Sleep = (d, _) => {
                    lock (Sleeps) {
                        Sleeps.Add(d);
                        Now += d;
                    }
                    return Task.CompletedTask;
                },
            };
            Fetcher = new ChunkFetcher(VeloLog, Reader, Pkg.Bytes.Length, Policy) { Parallelism = parallelism };
            Blob = ChunkBlob.Open(Path.Combine(Dir, ChunkBlob.FileName));
        }

        public static TestPackage DefaultPackage()
        {
            // a handful of deflate files and one stored (incompressible) file; ~50 chunks of 64 KiB
            var files = new Dictionary<string, byte[]> {
                ["Squirrel.exe"] = TestPackageBuilder.Text(1, 300_000),
                ["App.exe"] = TestPackageBuilder.Text(2, 900_000),
                ["lib/Core.dll"] = TestPackageBuilder.Text(3, 1_200_000),
                ["data/blob.bin"] = TestPackageBuilder.Random(4, 500_000),
                ["readme.txt"] = TestPackageBuilder.Text(5, 20_000),
                ["empty.txt"] = Array.Empty<byte>(),
            };
            return TestPackageBuilder.Build(files);
        }

        public List<RangeRequest> PerChunkRequests() => Pkg.PerChunkRequests();

        /// <summary>Consecutive chunks grouped <paramref name="perRequest"/> at a time (contiguous data, so each group is one range).</summary>
        public List<RangeRequest> GroupedRequests(int perRequest)
        {
            var plan = ChunkPlan.Create(Pkg.Remote, null, _ => false, _ => false);
            var sorted = plan.Needed.OrderBy(c => c.Offset).ToList();
            var result = new List<RangeRequest>();
            for (int i = 0; i < sorted.Count; i++) {
                if (i % perRequest == 0) result.Add(new RangeRequest(sorted[i]));
                else result[^1].Add(sorted[i]);
            }
            return result;
        }

        public List<RangeRequest> CoalescedRequests()
        {
            var plan = ChunkPlan.Create(Pkg.Remote, null, _ => false, h => Blob.Contains(h));
            return ChunkPlan.Coalesce(plan.Needed);
        }

        public Task FetchAsync(IReadOnlyList<RangeRequest> requests, CancellationToken ct = default, bool forceAppend = false)
            => Fetcher.FetchRangesAsync(requests, Blob, forceAppend, null, ct);

        public Task<byte[]> FetchIndexAsync(CancellationToken ct = default)
            => Fetcher.FetchIndexAsync(Pkg.TrailerOffset, Pkg.Trailer.Length, Pkg.Asset.ChunkIndexSHA256, null, ct);

        /// <summary>Every chunk of the package is in the blob with the right plaintext.</summary>
        public void AssertComplete()
        {
            foreach (var (hash, plain) in Pkg.Chunks()) {
                Assert.True(Blob.Contains(hash), $"chunk {hash} missing from blob");
                Assert.Equal(plain, Blob.Read(hash));
            }
        }

        /// <summary>Ranged requests grouped by their Range header value.</summary>
        public Dictionary<string, int> RangedRequestCounts()
        {
            return Server.Requests.Where(r => r.IsRanged).GroupBy(r => r.Range!).ToDictionary(g => g.Key, g => g.Count());
        }

        public int RangedRequests => Server.Requests.Count(r => r.IsRanged);

        public static string RangeOf(RangeRequest r) => $"bytes={r.Offset}-{r.End - 1}";

        public void Dispose()
        {
            Blob.Dispose();
            Reader.Dispose();
            Server.Dispose();
            (Logger as IDisposable)?.Dispose();
            _tempDir.Dispose();
        }
    }

    private static async Task<T> AssertFatalAsync<T>(Task task) where T : Exception
    {
        var ex = await Assert.ThrowsAsync<T>(() => task);
        return ex;
    }

    [Fact]
    public async Task SurvivesTransientFaults()
    {
        using var h = new Harness(_output, parallelism: 1);
        var requests = h.PerChunkRequests();
        Assert.True(requests.Count >= 40, $"expected a package with many chunks, got {requests.Count}");

        // 8 faults of every transient kind, each below every budget, interleaved with good responses
        h.Server.Faults.Enqueue(
            Fault.ResetAfter(100), Fault.None,
            Fault.Status(500), Fault.None,
            Fault.Status(503, "1"), Fault.None,
            Fault.ShortBody(10), Fault.None,
            Fault.Status(429), Fault.None,
            Fault.Status(502), Fault.None,
            Fault.Status(504), Fault.None,
            Fault.Status(408), Fault.None);

        await h.FetchAsync(requests);
        h.AssertComplete();

        // a completed range is never re-read: exactly one extra request per fault
        Assert.Equal(8, h.Server.Faults.Applied);
        Assert.Equal(requests.Count + 8, h.RangedRequests);
        var counts = h.RangedRequestCounts();
        Assert.All(requests, r => Assert.True(counts[Harness.RangeOf(r)] <= 2));
        Assert.Equal(8, h.Sleeps.Count);
    }

    [Fact]
    public async Task BudgetResetsOnProgress()
    {
        using var h = new Harness(_output, parallelism: 1);
        var requests = h.PerChunkRequests();

        // every other request fails: far more than 16 failures in total, but never two in a row
        h.Server.Faults.AddRule((_, ordinal) => ordinal % 2 == 1 ? Fault.Status(500) : Fault.None);

        await h.FetchAsync(requests);
        h.AssertComplete();
        Assert.True(h.Server.Faults.Applied >= 40, $"only {h.Server.Faults.Applied} faults were applied");
        Assert.Equal(requests.Count, h.Server.Faults.Applied);
    }

    [Fact]
    public async Task ShortOutageIsRiddenOut()
    {
        using var h = new Harness(_output, parallelism: 4);
        var requests = h.PerChunkRequests();
        // the network drops: the first 7 attempts of each worker's first request fail instantly (28 failures across
        // the 4 workers, far past what a shared failure count would allow), then it comes back
        var outage = new HashSet<string>(requests.Take(4).Select(Harness.RangeOf));
        var failures = new Dictionary<string, int>();
        h.Server.Faults.AddRule((req, _) => {
            lock (failures) {
                if (!outage.Contains(req.Range!)) return Fault.None;
                failures.TryGetValue(req.Range!, out int n);
                if (n >= 7) return Fault.None;
                failures[req.Range!] = n + 1;
                return Fault.Status(503);
            }
        });

        await h.FetchAsync(requests);
        h.AssertComplete();
        Assert.Equal(4 * 7, h.Server.Faults.Applied);
        // equal jitter: every backoff waits at least half its cap
        lock (h.Sleeps) {
            Assert.Equal(4 * 7, h.Sleeps.Count);
            Assert.All(h.Sleeps, s => Assert.True(s >= TimeSpan.FromSeconds(0.5), $"backoff {s} below the jitter floor"));
        }
    }

    [Fact]
    public void BackoffIsEqualJitter()
    {
        var policy = new RetryPolicy(randomSeed: 42);
        for (int attempt = 1; attempt <= 12; attempt++) {
            double cap = Math.Min(30, Math.Pow(2, attempt - 1));
            for (int i = 0; i < 50; i++) {
                var d = policy.GetBackoff(attempt, null).TotalSeconds;
                Assert.InRange(d, cap / 2, cap);
            }
        }
        // the 8 retries of one request always wait at least 0.5+1+2+4+8+15+15+15 seconds
        double floor = Enumerable.Range(1, RetryPolicy.MaxRetriesPerRequest).Sum(a => Math.Min(30, Math.Pow(2, a - 1)) / 2);
        Assert.Equal(60.5, floor);
    }

    [Fact]
    public void RequestTimeoutScalesWithLength()
    {
        Assert.Equal(TimeSpan.FromSeconds(300), RetryPolicy.GetRequestTimeout(RetryPolicy.RequestTimeout, 0));
        Assert.Equal(TimeSpan.FromSeconds(304), RetryPolicy.GetRequestTimeout(RetryPolicy.RequestTimeout, 64 * 1024));
        // an 8 MiB coalesced request has room for 16 KiB/s
        Assert.Equal(TimeSpan.FromSeconds(300 + 512), RetryPolicy.GetRequestTimeout(RetryPolicy.RequestTimeout, ChunkPlan.MaxRequestLength));
    }

    [Fact]
    public async Task FailingMultiChunkRequestIsSplit()
    {
        using var h = new Harness(_output, parallelism: 1);
        var requests = h.GroupedRequests(8);
        var first = requests[0];
        Assert.Equal(8, first.Chunks.Count);
        // the whole span fails once (eg. it timed out on a slow link): it is fetched as two halves, never whole again
        h.Server.Faults.Enqueue(Fault.ResetAfter(1000));
        var progress = new List<long>();

        await h.Fetcher.FetchRangesAsync(requests, h.Blob, false, p => { lock (progress) progress.Add(p); }, CancellationToken.None);
        h.AssertComplete();

        var counts = h.RangedRequestCounts();
        Assert.Equal(1, counts[Harness.RangeOf(first)]);
        var left = new RangeRequest(first.Chunks[0]);
        for (int i = 1; i < 4; i++) left.Add(first.Chunks[i]);
        var right = new RangeRequest(first.Chunks[4]);
        for (int i = 5; i < 8; i++) right.Add(first.Chunks[i]);
        Assert.Equal(1, counts[Harness.RangeOf(left)]);
        Assert.Equal(1, counts[Harness.RangeOf(right)]);
        Assert.Equal(requests.Count + 2, h.RangedRequests);
        Assert.Single(h.Sleeps);
        // progress still ends at the total and never goes backwards
        Assert.Equal(requests.Sum(r => r.Length), progress[^1]);
        Assert.Equal(progress.OrderBy(p => p), progress);
    }

    [Fact]
    public async Task SplitHalvesSplitAgainDownToSingleChunks()
    {
        using var h = new Harness(_output, parallelism: 1);
        var requests = h.GroupedRequests(8);
        var doomedOffset = requests[0].Chunks[5].Offset;
        // every request covering chunk 5 of the first group fails its first attempt: 8 -> 4 -> 2 -> 1, then retried
        var failedOnce = new HashSet<string>();
        h.Server.Faults.AddRule((req, _) => {
            var range = req.Range!;
            long start = long.Parse(range.Substring(6, range.IndexOf('-') - 6));
            long end = long.Parse(range.Substring(range.IndexOf('-') + 1));
            if (start <= doomedOffset && doomedOffset <= end) {
                lock (failedOnce) {
                    if (failedOnce.Add(range)) return Fault.Status(500);
                }
            }
            return Fault.None;
        });

        await h.FetchAsync(requests);
        h.AssertComplete();
        // 8, 4, 2 and 1 chunk spans each failed once
        Assert.Equal(4, h.Server.Faults.Applied);
        Assert.Equal(4, h.Sleeps.Count);
    }

    [Fact]
    public async Task BlobWriteFailureIsFatalWithoutRetry()
    {
        Assert.SkipUnless(VelopackRuntimeInfo.IsWindows, "byte-range locks are mandatory only on Windows");
        using var h = new Harness(_output, parallelism: 2);
        // another handle locks the whole blob file: appends fail (once the write buffer spills) like a full disk would
        using var locker = new FileStream(Path.Combine(h.Dir, ChunkBlob.FileName), FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite);
        locker.Lock(0, 1L << 40);

        var ex = await AssertFatalAsync<StagingIoException>(h.FetchAsync(h.PerChunkRequests()));
        Assert.IsAssignableFrom<IOException>(ex.InnerException);
        Assert.Equal(RetryClass.Fatal, RetryPolicy.Classify(ex, CancellationToken.None));
        // a local write error is not retried as a network error: no range is downloaded twice and nothing backs off
        Assert.Empty(h.Sleeps);
        Assert.All(h.RangedRequestCounts().Values, n => Assert.Equal(1, n));
    }

    [Fact]
    public async Task NoProgressForTenMinutesIsFatal()
    {
        using var h = new Harness(_output, parallelism: 4);
        h.Server.Faults.AddRule((_, _) => Fault.Status(503));
        // every retry waits a (virtual) minute, so the ten-minute no-progress rule trips before 16 consecutive
        // failures and before any single request has used up its own budget
        h.Policy.Sleep = (_, _) => {
            h.Now += TimeSpan.FromMinutes(1);
            return Task.CompletedTask;
        };

        var ex = await AssertFatalAsync<ChunkFetchFatalException>(h.FetchAsync(h.PerChunkRequests()));
        Assert.Contains("No progress", ex.Message);
        Assert.InRange(h.Server.Faults.Applied, 11, 11 + 4);
    }

    [Fact]
    public async Task PerRequestBudgetIsFatal()
    {
        using var h = new Harness(_output, parallelism: 1);
        var requests = h.PerChunkRequests();
        var doomed = Harness.RangeOf(requests[2]);
        h.Server.Faults.AddRule((req, _) => req.Range == doomed ? Fault.Status(500) : Fault.None);

        var ex = await AssertFatalAsync<ChunkFetchFatalException>(h.FetchAsync(requests));
        Assert.Contains("failed 9 times", ex.Message);
        Assert.Equal(RetryPolicy.MaxRetriesPerRequest + 1, h.RangedRequestCounts()[doomed]);
        Assert.Equal(2, h.Blob.Count);
    }

    [Fact]
    public async Task RetryAfterHonoured()
    {
        using var h = new Harness(_output, parallelism: 1);
        h.Server.Faults.Enqueue(Fault.Status(503, "7"), Fault.None, Fault.Status(429, "120"));

        await h.FetchAsync(h.PerChunkRequests());
        h.AssertComplete();
        Assert.Equal(new[] { TimeSpan.FromSeconds(7), RetryPolicy.MaxRetryAfter }, h.Sleeps);
    }

    [Fact]
    public async Task Status200IsFatalWithoutReadingBody()
    {
        using var h = new Harness(_output, parallelism: 1);
        h.Server.Faults.Enqueue(Fault.IgnoreRange());

        var ex = await AssertFatalAsync<ChunkFetchFatalException>(h.FetchAsync(h.PerChunkRequests()));
        Assert.IsType<RangeUnavailableException>(ex.InnerException);
        Assert.Contains("200", ex.InnerException!.Message);
        Assert.Equal(0, h.Reader.BodyBytesRead);
        Assert.Equal(1, h.RangedRequests);
        Assert.Empty(h.Sleeps);
    }

    [Fact]
    public async Task WrongContentRangeIsFatal()
    {
        using var h = new Harness(_output, parallelism: 1);
        h.Server.Faults.Enqueue(Fault.WrongContentRange());

        var ex = await AssertFatalAsync<ChunkFetchFatalException>(h.FetchAsync(h.PerChunkRequests()));
        Assert.IsType<RangeUnavailableException>(ex.InnerException);
        Assert.Contains("Content-Range", ex.InnerException!.Message);
        Assert.Equal(1, h.RangedRequests);
    }

    [Fact]
    public async Task ShortBodyRetried()
    {
        using var h = new Harness(_output, parallelism: 1);
        var requests = h.PerChunkRequests();
        h.Server.Faults.Enqueue(Fault.ShortBody(10));

        await h.FetchAsync(requests);
        h.AssertComplete();
        Assert.Equal(2, h.RangedRequestCounts()[Harness.RangeOf(requests[0])]);
        Assert.Equal(requests.Count + 1, h.RangedRequests);
    }

    [Fact]
    public async Task HashMismatchRefetchesOnlyThatRange()
    {
        using var h = new Harness(_output, parallelism: 1);
        var requests = h.GroupedRequests(5);
        Assert.True(requests.Count >= 2);
        // the second request's body is corrupted once: its chunks fail verification and only it is re-read
        h.Server.Faults.Enqueue(Fault.None, Fault.CorruptBytes(64));

        await h.FetchAsync(requests);
        h.AssertComplete();
        var counts = h.RangedRequestCounts();
        Assert.Equal(1, counts[Harness.RangeOf(requests[0])]);
        Assert.Equal(2, counts[Harness.RangeOf(requests[1])]);
        Assert.Equal(requests.Count + 1, h.RangedRequests);
        Assert.Single(h.Sleeps);
    }

    [Fact]
    public async Task HashMismatchThreeTimesIsFatal()
    {
        using var h = new Harness(_output, parallelism: 1);
        h.Server.Faults.AddRule((_, _) => Fault.CorruptBytes(64));

        var ex = await AssertFatalAsync<ChunkFetchFatalException>(h.FetchAsync(h.PerChunkRequests()));
        Assert.Contains("mismatched 3 times", ex.Message);
        Assert.Equal(3, h.RangedRequests);
    }

    [Fact]
    public async Task IndexShaMismatchRetriesOnceThenFatal()
    {
        using var h = new Harness(_output, parallelism: 1);
        var indexRange = $"bytes={h.Pkg.TrailerOffset}-{h.Pkg.TrailerOffset + h.Pkg.Trailer.Length - 1}";
        h.Server.Faults.AddRule((req, _) => req.Range == indexRange ? Fault.CorruptBytes(8) : Fault.None);

        await AssertFatalAsync<ChunkIndexInvalidException>(h.FetchIndexAsync());
        Assert.Equal(2, h.RangedRequestCounts()[indexRange]);
        Assert.Empty(h.Sleeps);

        // and a clean index fetch returns the trailer bytes
        using var ok = new Harness(_output, parallelism: 1);
        Assert.Equal(ok.Pkg.Trailer, await ok.FetchIndexAsync());
    }

    [Fact]
    public async Task RedirectResolvedOnceAndCached()
    {
        using var h = new Harness(_output, parallelism: 4, requestPath: "/download/pkg", configureServer: s => {
            s.Redirect("/download/pkg", n => s.BaseUrl + $"signed/{PackageFile}?t={n}");
            s.Alias($"/signed/{PackageFile}", PackageFile);
        });

        await h.FetchAsync(h.CoalescedRequests());
        h.AssertComplete();
        Assert.Equal(1, h.Server.RedirectHits("/download/pkg"));
        Assert.Equal(1, h.Reader.ResolutionCount);
        Assert.All(h.Server.Requests.Where(r => r.IsRanged && r.Path.StartsWith("/signed/")), r => Assert.EndsWith("?t=1", r.PathAndQuery));
        Assert.Equal($"{h.Server.BaseUrl}signed/{PackageFile}?t=1", h.Reader.CachedUri!.ToString());
    }

    [Fact]
    public async Task ExpiredUrlRefreshIsSingleFlight()
    {
        using var h = new Harness(_output, parallelism: 4, requestPath: "/download/pkg", configureServer: s => {
            s.Redirect("/download/pkg", n => s.BaseUrl + $"signed/{PackageFile}?t={n}");
            s.Alias($"/signed/{PackageFile}", PackageFile);
        });
        // the first signed url "expires" after four successful reads: every worker then sees 403 at about the same
        // time, yet the original url is re-resolved only once and all of them continue on the second signed url
        h.Server.Faults.AddRule((req, ordinal) => req.PathAndQuery.EndsWith("?t=1") && ordinal > 4 ? Fault.Status(403) : Fault.None);

        await h.FetchAsync(h.PerChunkRequests());
        h.AssertComplete();
        Assert.Equal(2, h.Server.RedirectHits("/download/pkg"));
        Assert.Equal(2, h.Reader.ResolutionCount);
        Assert.Contains(h.Server.Requests, r => r.PathAndQuery.EndsWith("?t=2"));
        Assert.DoesNotContain(h.Server.Requests, r => r.PathAndQuery.EndsWith("?t=3"));
        Assert.Empty(h.Sleeps);
    }

    [Fact]
    public async Task RefreshBudgetResetsOnProgress()
    {
        using var h = new Harness(_output, parallelism: 1, requestPath: "/download/pkg", configureServer: s => {
            s.Redirect("/download/pkg", n => s.BaseUrl + $"signed/{PackageFile}?t={n}");
            s.Alias($"/signed/{PackageFile}", PackageFile);
        });
        // every other signed request is rejected: each refresh is followed by a verified chunk, so dozens of refreshes
        // never add up to the budget of three consecutive ones
        h.Server.Faults.AddRule((req, ordinal) => req.Path.StartsWith("/signed/") && ordinal % 2 == 0 ? Fault.Status(403) : Fault.None);

        var requests = h.PerChunkRequests();
        await h.FetchAsync(requests);
        h.AssertComplete();
        Assert.True(h.Server.RedirectHits("/download/pkg") > RetryPolicy.MaxConsecutiveRefreshes * 5);
        // the first request resolves the url; every later chunk is rejected once on the cached url and re-resolved
        Assert.Equal(requests.Count, h.Server.RedirectHits("/download/pkg"));
    }

    [Fact]
    public async Task RefreshWithoutProgressThreeTimesIsFatal()
    {
        using var h = new Harness(_output, parallelism: 1, requestPath: "/download/pkg", configureServer: s => {
            s.Redirect("/download/pkg", n => s.BaseUrl + $"signed/{PackageFile}?t={n}");
            s.Alias($"/signed/{PackageFile}", PackageFile);
        });
        // the resolution succeeds, every read on the cached url is rejected, and every re-resolved url answers 503
        // before it is used from the cache again: three refreshes without a verified chunk in between
        h.Server.Faults.AddRule((req, ordinal) => ordinal == 1 ? Fault.None : ordinal % 2 == 0 ? Fault.Status(403) : Fault.Status(503));

        var ex = await AssertFatalAsync<ChunkFetchFatalException>(h.FetchAsync(h.PerChunkRequests()));
        Assert.Contains("refreshes", ex.Message);
        Assert.Equal(3, h.Server.RedirectHits("/download/pkg"));
        Assert.Equal(1, h.Blob.Count);
    }

    [Fact]
    public async Task CancellationStopsPromptly()
    {
        using var h = new Harness(_output, parallelism: 4);
        h.Server.Faults.AddRule((_, _) => Fault.Stall(TimeSpan.FromSeconds(60)));
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(300));

        var sw = Stopwatch.StartNew();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => h.FetchAsync(h.PerChunkRequests(), cts.Token));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(10), $"cancellation took {sw.Elapsed}");
        Assert.Empty(h.Sleeps);
    }

    [Fact]
    public async Task NoSleepInTests()
    {
        using var h = new Harness(_output, parallelism: 1);
        h.Server.Faults.Enqueue(Fault.Status(500), Fault.None, Fault.Status(500), Fault.None, Fault.Status(503, "30"));

        var sw = Stopwatch.StartNew();
        await h.FetchAsync(h.PerChunkRequests());
        h.AssertComplete();
        Assert.Equal(3, h.Sleeps.Count);
        Assert.Equal(TimeSpan.FromSeconds(30), h.Sleeps[2]);
        Assert.InRange(h.Sleeps[0], TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1));
        Assert.InRange(h.Sleeps[1], TimeSpan.FromSeconds(0.5), TimeSpan.FromSeconds(1));
        Assert.True(sw.Elapsed < TimeSpan.FromSeconds(20), $"the fetch really waited: {sw.Elapsed}");
    }

    [Fact]
    public async Task AutoDecompressionDisabled()
    {
        using var h = new Harness(_output, parallelism: 1);
        h.Server.Faults.Enqueue(Fault.Gzip());

        var ex = await AssertFatalAsync<ChunkFetchFatalException>(h.FetchAsync(h.PerChunkRequests()));
        Assert.IsType<RangeUnavailableException>(ex.InnerException);
        Assert.Contains("gzip", ex.InnerException!.Message);
    }

    [Fact]
    public async Task HttpTimeoutIsTransientNotCancel()
    {
        using var h = new Harness(_output, parallelism: 1);
        h.Reader.HeadersTimeout = TimeSpan.FromMilliseconds(500);
        h.Reader.ReadStallTimeout = TimeSpan.FromMilliseconds(500);
        var requests = h.PerChunkRequests();
        // headers plus a few bytes, then silence: the read stalls, the request is retried and completes
        h.Server.Faults.Enqueue(Fault.Stall(TimeSpan.FromSeconds(5), afterBytes: 100));

        await h.FetchAsync(requests);
        h.AssertComplete();
        Assert.Equal(2, h.RangedRequestCounts()[Harness.RangeOf(requests[0])]);
        Assert.Single(h.Sleeps);
    }

    [Fact]
    public async Task HeadersTimeoutIsTransientNotCancel()
    {
        using var h = new Harness(_output, parallelism: 1);
        h.Reader.HeadersTimeout = TimeSpan.FromMilliseconds(500);
        var requests = h.PerChunkRequests();
        h.Server.Faults.AddRule((req, ordinal) => ordinal == 1 ? Fault.Stall(TimeSpan.FromSeconds(5)) : Fault.None);

        await h.FetchAsync(requests);
        h.AssertComplete();
        Assert.Single(h.Sleeps);
    }

    [Fact]
    public async Task MissingRoundAppendsUnconditionally()
    {
        using var h = new Harness(_output, parallelism: 1);
        var requests = h.PerChunkRequests();
        var first = requests[0];
        await h.FetchAsync(new[] { first });
        long after = h.Blob.Length;
        Assert.Equal(1, h.Blob.Count);

        // a normal round does not append chunks the blob already holds
        await h.FetchAsync(new[] { first });
        Assert.Equal(after, h.Blob.Length);

        // a missing round appends them again unconditionally (the assembler found the earlier copy unusable)
        await h.FetchAsync(new[] { first }, forceAppend: true);
        Assert.Equal(after + ChunkBlob.RecordHeaderSize + first.Chunks[0].PlainLength, h.Blob.Length);
        Assert.Equal(1, h.Blob.Count);
        Assert.Equal(3, h.RangedRequests);
    }

    [Fact]
    public async Task CorruptPayloadIsRefetched()
    {
        using var h = new Harness(_output, parallelism: 1);
        var (hash, plain) = h.Pkg.Chunks()[0];

        // a blob with a flushed header but a zeroed payload (the power-loss tail): not indexed, not a structural failure
        h.Blob.Dispose();
        var blobPath = Path.Combine(h.Dir, ChunkBlob.FileName);
        using (var fs = new FileStream(blobPath, FileMode.Create)) {
            var header = new byte[ChunkBlob.RecordHeaderSize];
            "VCHK"u8.ToArray().CopyTo(header, 0);
            BitConverter.GetBytes((uint) plain.Length).CopyTo(header, 4);
            hash.CopyTo(header, 8);
            fs.Write(header);
            fs.Write(new byte[plain.Length]);
        }

        using var blob = ChunkBlob.Open(blobPath);
        Assert.False(blob.Contains(hash));
        var plan = ChunkPlan.Create(h.Pkg.Remote, null, _ => false, blob.Contains);
        Assert.Contains(plan.Needed, c => c.Hash == hash);

        await h.Fetcher.FetchRangesAsync(ChunkPlan.Coalesce(plan.Needed), blob, false, null, CancellationToken.None);
        Assert.True(blob.Contains(hash));
        Assert.Equal(plain, blob.Read(hash));
        foreach (var (hh, pp) in h.Pkg.Chunks()) Assert.Equal(pp, blob.Read(hh));
    }

    [Fact]
    public async Task FileSourceRangeReaderWorks()
    {
        using var h = new Harness(_output, parallelism: 2);
        var serveDir = Path.Combine(h.Dir, "serve");
        var source = new SimpleFileSource(new DirectoryInfo(serveDir));
        using var reader = await source.OpenRangeReaderAsync(h.VeloLog, new VelopackAsset { FileName = PackageFile }, CancellationToken.None);
        Assert.NotNull(reader);
        var fetcher = new ChunkFetcher(h.VeloLog, reader!, h.Pkg.Bytes.Length, h.Policy);
        var trailer = await fetcher.FetchIndexAsync(h.Pkg.TrailerOffset, h.Pkg.Trailer.Length, h.Pkg.Asset.ChunkIndexSHA256, null, default);
        Assert.Equal(h.Pkg.Trailer, trailer);
        Assert.Equal(h.Pkg.Bytes.Length, reader!.Length);
        await fetcher.FetchRangesAsync(h.CoalescedRequests(), h.Blob, false, null, default);
        h.AssertComplete();
    }
}
