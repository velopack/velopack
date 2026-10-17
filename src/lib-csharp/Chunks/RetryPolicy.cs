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
    /// The chunk fetch retry policy (§5.6): classification of failures, full-jitter backoff and the progress-based
    /// budgets shared by all fetch workers. The sleep function and the clock are injectable so tests never wait.
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

        /// <summary>A request may fail this many times and still be retried; the next failure is fatal.</summary>
        public const int MaxRetriesPerRequest = 8;
        /// <summary>Chunk hash/length mismatches on the same request before it is fatal.</summary>
        public const int MaxHashMismatchesPerRequest = 3;
        /// <summary>Failed attempts across all workers since the last verified chunk before the fetch is fatal.</summary>
        public const int MaxConsecutiveFailures = 16;
        /// <summary>Consecutive redirect refreshes without a verified chunk before the fetch is fatal.</summary>
        public const int MaxConsecutiveRefreshes = 3;
        /// <summary>Extra <c>assemble</c> rounds allowed to fetch hashes reported in <c>missing.bin</c>.</summary>
        public const int MaxMissingRounds = 2;
        /// <summary>Maximum number of redirect hops followed per request.</summary>
        public const int MaxRedirects = 10;

        private readonly object _lock = new object();
        private readonly Random _random;
        private int _consecutiveFailures;
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

        /// <summary>Failed attempts across all workers since the last verified chunk.</summary>
        public int ConsecutiveFailures {
            get {
                lock (_lock) return _consecutiveFailures;
            }
        }

        /// <summary>Must be called once before the first request so the no-progress clock starts from the policy's clock.</summary>
        public void Start()
        {
            lock (_lock) {
                _lastProgress = Clock();
                _consecutiveFailures = 0;
                _consecutiveRefreshes = 0;
            }
        }

        /// <summary>Resets every budget: a chunk (or index segment) was verified.</summary>
        public void OnProgress()
        {
            lock (_lock) {
                _consecutiveFailures = 0;
                _consecutiveRefreshes = 0;
                _lastProgress = Clock();
            }
        }

        /// <summary>
        /// Records a failed attempt. Throws <see cref="ChunkFetchFatalException"/> when the consecutive-failure or
        /// no-progress budget is exhausted.
        /// </summary>
        public void OnFailure(Exception error)
        {
            lock (_lock) {
                _consecutiveFailures++;
                if (_consecutiveFailures >= MaxConsecutiveFailures) {
                    throw new ChunkFetchFatalException($"{_consecutiveFailures} consecutive failed requests without progress.", error);
                }
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
            if (ex is RangeRefreshException) return RetryClass.Refresh;
            // connect/read/timeout/IO errors, HttpClient timeouts (OperationCanceledException while the caller's
            // token is not cancelled), short bodies, retryable statuses and chunk mismatches
            return RetryClass.Transient;
        }

        /// <summary>
        /// The delay before retry number <paramref name="attempt"/> (1-based): full-jitter exponential backoff capped at 30 s,
        /// or <c>min(Retry-After, 60 s)</c> when the server sent a hint.
        /// </summary>
        public TimeSpan GetBackoff(int attempt, TimeSpan? retryAfter)
        {
            if (retryAfter.HasValue) {
                var ra = retryAfter.Value;
                if (ra < TimeSpan.Zero) ra = TimeSpan.Zero;
                return ra > MaxRetryAfter ? MaxRetryAfter : ra;
            }
            if (attempt < 1) attempt = 1;
            double maxSeconds = Math.Min(MaxBackoff.TotalSeconds, BaseBackoff.TotalSeconds * Math.Pow(2, attempt - 1));
            double seconds;
            lock (_lock) {
                seconds = _random.NextDouble() * maxSeconds;
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
