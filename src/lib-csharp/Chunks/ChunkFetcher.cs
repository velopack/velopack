using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Logging;
using Velopack.Sources;

namespace Velopack.Chunks
{
    /// <summary>
    /// Fetches the chunk index trailer and the needed chunk ranges of a package through an <see cref="IRangeReader"/>
    /// (§5.2, §5.5–§5.7): a fixed number of workers over an offset-ordered queue, each request retried under the
    /// shared <see cref="RetryPolicy"/>. Every received chunk is decoded, SHA-256 verified and appended to the blob;
    /// progress is reported in transport bytes received.
    /// </summary>
    internal sealed class ChunkFetcher
    {
        private readonly IVelopackLogger _log;
        private readonly IRangeReader _reader;
        private readonly long _assetSize;
        private readonly RetryPolicy _policy;

        /// <summary>Number of concurrent requests.</summary>
        public int Parallelism { get; set; } = ChunkPlan.Parallelism;

        /// <summary>Largest single read issued to the reader; longer requests are split into consecutive reads.</summary>
        public long MaxReadLength { get; set; } = ChunkPlan.MaxRequestLength;

        public ChunkFetcher(IVelopackLogger log, IRangeReader reader, long assetSize, RetryPolicy policy)
        {
            _log = log ?? throw new ArgumentNullException(nameof(log));
            _reader = reader ?? throw new ArgumentNullException(nameof(reader));
            _assetSize = assetSize;
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        /// <summary>
        /// Reads the trailer bytes and verifies their SHA-256 against the feed pointer. A digest mismatch retries the
        /// whole index once; a second mismatch is fatal (<see cref="ChunkIndexInvalidException"/>).
        /// </summary>
        public async Task<byte[]> FetchIndexAsync(long offset, long length, string expectedSha256, Action<long>? progress, CancellationToken ct)
        {
            if (length <= 0 || length > ChunkIndex.MaxTrailerLength) throw new ArgumentOutOfRangeException(nameof(length));
            if (!Hash256.TryParse(expectedSha256, out var expected)) throw new ArgumentException("Not a SHA-256 hex string.", nameof(expectedSha256));
            _policy.Start();

            for (int round = 0; round < 2; round++) {
                var data = new byte[length];
                long done = 0;
                while (done < length) {
                    int n = (int) Math.Min(MaxReadLength, length - done);
                    long segmentOffset = offset + done;
                    int segmentStart = (int) done;
                    await RunRequestAsync(
                        $"index {segmentOffset}+{n}",
                        async () => {
                            await _reader.ReadExactAsync(segmentOffset, data, segmentStart, n, ct).ConfigureAwait(false);
                            CheckAssetLength();
                        },
                        ct).ConfigureAwait(false);
                    _policy.OnProgress();
                    done += n;
                    progress?.Invoke(done);
                }
                if (ChunkInflate.Sha256(data) == expected) return data;
                _log.Warn($"Chunk index digest mismatch (round {round + 1}).");
            }
            // nothing fetched so far is usable: the staged directory is discarded with the index
            throw new ChunkIndexInvalidException("The chunk index digest did not match the feed twice.");
        }

        /// <summary>
        /// Fetches every request, decoding and verifying its chunks into <paramref name="blob"/>. A chunk already in
        /// the blob is skipped unless <paramref name="forceAppend"/> is set (the <c>missing.bin</c> round).
        /// <paramref name="progress"/> receives the cumulative transport bytes received.
        /// </summary>
        public async Task FetchRangesAsync(IReadOnlyList<RangeRequest> requests, ChunkBlob blob, bool forceAppend, Action<long>? progress,
            CancellationToken ct)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            if (blob == null) throw new ArgumentNullException(nameof(blob));
            if (requests.Count == 0) return;
            _policy.Start();

            var queue = new ConcurrentQueue<RangeRequest>(requests);
            long received = 0;
            using var abort = CancellationTokenSource.CreateLinkedTokenSource(ct);
            Exception? firstError = null;
            var errorLock = new object();

            async Task Worker()
            {
                while (!abort.IsCancellationRequested && queue.TryDequeue(out var request)) {
                    try {
                        await FetchRequestAsync(request, blob, forceAppend, abort.Token).ConfigureAwait(false);
                        long total = Interlocked.Add(ref received, request.Length);
                        progress?.Invoke(total);
                    } catch (Exception ex) {
                        lock (errorLock) {
                            firstError ??= ex;
                        }
                        abort.Cancel();
                        return;
                    }
                }
            }

            int workers = Math.Max(1, Math.Min(Parallelism, requests.Count));
            var tasks = new Task[workers];
            for (int i = 0; i < workers; i++) tasks[i] = Task.Run(Worker);
            await Task.WhenAll(tasks).ConfigureAwait(false);

            ct.ThrowIfCancellationRequested();
            if (firstError != null) {
                if (firstError is OperationCanceledException) throw new ChunkFetchFatalException("The fetch was aborted.", firstError);
                throw firstError;
            }
        }

        private async Task FetchRequestAsync(RangeRequest request, ChunkBlob blob, bool forceAppend, CancellationToken ct)
        {
            int hashMismatches = 0;
            var buffer = new byte[request.Length];
            await RunRequestAsync(
                $"range {request.Offset}+{request.Length}",
                async () => {
                    long done = 0;
                    while (done < request.Length) {
                        int n = (int) Math.Min(MaxReadLength, request.Length - done);
                        await _reader.ReadExactAsync(request.Offset + done, buffer, (int) done, n, ct).ConfigureAwait(false);
                        done += n;
                    }
                    CheckAssetLength();
                    foreach (var chunk in request.Chunks) {
                        if (!forceAppend && blob.Contains(chunk.Hash)) continue;
                        int start = (int) (chunk.Offset - request.Offset);
                        var plain = ChunkInflate.TryDecodeChunk(buffer, start, chunk.Length, chunk.Method, chunk.PlainLength, chunk.Hash);
                        if (plain == null) {
                            if (++hashMismatches >= RetryPolicy.MaxHashMismatchesPerRequest) {
                                throw new ChunkFetchFatalException($"Chunk {chunk.ChunkIndex} mismatched {hashMismatches} times.");
                            }
                            throw new ChunkMismatchException($"Chunk {chunk.ChunkIndex} did not verify after decoding.");
                        }
                        blob.Append(chunk.Hash, plain);
                        _policy.OnProgress();
                    }
                },
                ct).ConfigureAwait(false);
        }

        private void CheckAssetLength()
        {
            long reported = _reader.Length;
            if (reported >= 0 && _assetSize > 0 && reported != _assetSize) {
                throw new RangeUnavailableException($"The origin reports {reported} bytes but the feed says {_assetSize}.");
            }
        }

        /// <summary>Runs one request attempt loop under the retry policy.</summary>
        private async Task RunRequestAsync(string what, Func<Task> attempt, CancellationToken ct)
        {
            int failures = 0;
            while (true) {
                ct.ThrowIfCancellationRequested();
                try {
                    await attempt().ConfigureAwait(false);
                    return;
                } catch (Exception ex) {
                    var cls = RetryPolicy.Classify(ex, ct);
                    if (cls == RetryClass.Cancel) throw;
                    if (cls == RetryClass.Fatal) {
                        if (ex is ChunkFetchFatalException || ex is ChunkIndexInvalidException) throw;
                        throw new ChunkFetchFatalException($"Fatal error fetching {what}: {ex.Message}", ex);
                    }
                    failures++;
                    if (failures > RetryPolicy.MaxRetriesPerRequest) {
                        throw new ChunkFetchFatalException($"Request {what} failed {failures} times: {ex.Message}", ex);
                    }
                    if (cls == RetryClass.Refresh) {
                        _log.Debug($"Download URL refreshed while fetching {what}; retrying.");
                        if (((RangeRefreshException) ex).Invalidated) _policy.OnRefresh(ex);
                        continue;
                    }
                    _policy.OnFailure(ex);
                    var delay = _policy.GetBackoff(failures, RetryPolicy.GetRetryAfter(ex));
                    _log.Debug($"Fetching {what} failed ({ex.Message}); retry {failures} in {delay.TotalSeconds:0.##}s.");
                    await _policy.Sleep(delay, ct).ConfigureAwait(false);
                }
            }
        }
    }
}
