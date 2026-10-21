using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Sources;

namespace Velopack.Chunks
{
    /// <summary>How a failed range request is handled (§5.6).</summary>
    internal enum RetryClass
    {
        /// <summary>Retry the request after a backoff.</summary>
        Transient,
        /// <summary>The cached redirect target was refreshed; retry the request (counts as an attempt and a refresh).</summary>
        Refresh,
        /// <summary>Abandon the chunk path and download the full package.</summary>
        Fatal,
        /// <summary>The caller's cancellation token was cancelled; propagate.</summary>
        Cancel,
    }

    /// <summary>
    /// The chunk fetch retry policy (§5.6): classification of failures, equal-jitter backoff and the progress-based
    /// budgets shared by all fetch workers. There is deliberately no shared count of failed attempts: 4 workers failing
    /// instantly at once (Wi-Fi drop, DNS failure) would exhaust it within seconds, so a short outage is ridden out by
    /// the per-request budgets and the no-progress clock instead. The sleep function and the clock are injectable so
    /// tests never wait.
    /// </summary>
    internal sealed class RetryPolicy
    {
        public static readonly TimeSpan HeadersTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan ReadStallTimeout = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(300);
        public static readonly TimeSpan BaseBackoff = TimeSpan.FromSeconds(1);
        public static readonly TimeSpan MaxBackoff = TimeSpan.FromSeconds(30);
        public static readonly TimeSpan MaxRetryAfter = TimeSpan.FromSeconds(60);
        public static readonly TimeSpan MaxTimeWithoutProgress = TimeSpan.FromMinutes(10);

        /// <summary>
        /// The slowest body throughput the whole-request timeout of a ranged request allows on top of
        /// <see cref="RequestTimeout"/>, so a large coalesced request on a slow link can still complete.
        /// </summary>
        public const long MinRangeThroughputBytesPerSecond = 16 * 1024;

        /// <summary>A request may fail this many times and still be retried; the next failure is fatal.</summary>
        public const int MaxRetriesPerRequest = 8;
        /// <summary>Chunk hash/length mismatches on the same request before it is fatal.</summary>
        public const int MaxHashMismatchesPerRequest = 3;
        /// <summary>Consecutive redirect refreshes without a verified chunk before the fetch is fatal.</summary>
        public const int MaxConsecutiveRefreshes = 3;
        /// <summary>Extra <c>assemble</c> rounds allowed to fetch hashes reported in <c>missing.bin</c>.</summary>
        public const int MaxMissingRounds = 2;
        /// <summary>Maximum number of redirect hops followed per request.</summary>
        public const int MaxRedirects = 10;

        private readonly object _lock = new object();
        private readonly Random _random;
        private int _consecutiveRefreshes;
        private DateTime _lastProgress;

        /// <summary>Waits for the given delay. Tests replace this so no test really sleeps.</summary>
        public Func<TimeSpan, CancellationToken, Task> Sleep { get; set; } = (delay, ct) => Task.Delay(delay, ct);

        /// <summary>The wall clock used for the no-progress budget. Tests replace this.</summary>
        public Func<DateTime> Clock { get; set; } = () => DateTime.UtcNow;

        public RetryPolicy(int? randomSeed = null)
        {
            _random = randomSeed.HasValue ? new Random(randomSeed.Value) : new Random();
            _lastProgress = DateTime.UtcNow;
        }

        /// <summary>
        /// The whole-request timeout of a ranged request of <paramref name="length"/> bytes: <paramref name="baseTimeout"/>
        /// plus the time the body takes at <see cref="MinRangeThroughputBytesPerSecond"/>.
        /// </summary>
        public static TimeSpan GetRequestTimeout(TimeSpan baseTimeout, long length)
        {
            return baseTimeout + TimeSpan.FromSeconds(Math.Max(0, length) / MinRangeThroughputBytesPerSecond);
        }

        /// <summary>Must be called once before the first request so the no-progress clock starts from the policy's clock.</summary>
        public void Start()
        {
            lock (_lock) {
                _lastProgress = Clock();
                _consecutiveRefreshes = 0;
            }
        }

        /// <summary>Resets every budget: a chunk (or index segment) was verified.</summary>
        public void OnProgress()
        {
            lock (_lock) {
                _consecutiveRefreshes = 0;
                _lastProgress = Clock();
            }
        }

        /// <summary>
        /// Records a failed attempt. Throws <see cref="ChunkFetchFatalException"/> once nothing was verified for
        /// <see cref="MaxTimeWithoutProgress"/>.
        /// </summary>
        public void OnFailure(Exception error)
        {
            lock (_lock) {
                if (Clock() - _lastProgress >= MaxTimeWithoutProgress) {
                    throw new ChunkFetchFatalException($"No progress for {MaxTimeWithoutProgress.TotalMinutes} minutes.", error);
                }
            }
        }

        /// <summary>Records a redirect refresh. Throws <see cref="ChunkFetchFatalException"/> after three without progress.</summary>
        public void OnRefresh(Exception error)
        {
            lock (_lock) {
                _consecutiveRefreshes++;
                if (_consecutiveRefreshes >= MaxConsecutiveRefreshes) {
                    throw new ChunkFetchFatalException($"{_consecutiveRefreshes} consecutive URL refreshes without progress.", error);
                }
            }
        }

        /// <summary>Classifies an exception thrown by a request attempt.</summary>
        public static RetryClass Classify(Exception ex, CancellationToken callerToken)
        {
            if (ex is OperationCanceledException && callerToken.IsCancellationRequested) return RetryClass.Cancel;
            if (ex is ChunkFetchFatalException || ex is ChunkIndexInvalidException || ex is RangeUnavailableException) return RetryClass.Fatal;
            // a local write to the staging directory failed (disk full, access denied): fetching again cannot help
            if (ex is StagingIoException) return RetryClass.Fatal;
            if (ex is RangeRefreshException) return RetryClass.Refresh;
            // connect/read/timeout/IO errors, HttpClient timeouts (OperationCanceledException while the caller's
            // token is not cancelled), short bodies, retryable statuses and chunk mismatches
            return RetryClass.Transient;
        }

        /// <summary>
        /// The delay before retry number <paramref name="attempt"/> (1-based): equal-jitter exponential backoff,
        /// <c>random(cap / 2, cap)</c> with <c>cap = min(30 s, 1 s * 2^(attempt-1))</c>, or <c>min(Retry-After, 60 s)</c> when
        /// the server sent a hint. The half-cap floor means the 8 retries of a request always span at least a minute, so a short
        /// outage with instant connect failures cannot use them up within seconds.
        /// </summary>
        public TimeSpan GetBackoff(int attempt, TimeSpan? retryAfter)
        {
            if (retryAfter.HasValue) {
                var ra = retryAfter.Value;
                if (ra < TimeSpan.Zero) ra = TimeSpan.Zero;
                return ra > MaxRetryAfter ? MaxRetryAfter : ra;
            }
            if (attempt < 1) attempt = 1;
            double maxSeconds = Math.Min(MaxBackoff.TotalSeconds, BaseBackoff.TotalSeconds * Math.Pow(2, Math.Min(attempt - 1, 16)));
            double seconds;
            lock (_lock) {
                seconds = (maxSeconds / 2) + (_random.NextDouble() * maxSeconds / 2);
            }
            return TimeSpan.FromSeconds(seconds);
        }

        /// <summary>The <c>Retry-After</c> hint of a transient failure, if it carried one.</summary>
        public static TimeSpan? GetRetryAfter(Exception ex)
        {
            return (ex as RangeTransientException)?.RetryAfter;
        }
    }
}
