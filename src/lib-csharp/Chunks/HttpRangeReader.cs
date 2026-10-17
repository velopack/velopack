using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Sources;

namespace Velopack.Chunks
{
    /// <summary>
    /// An <see cref="IRangeReader"/> over HTTP (§5.5): one pooled <see cref="HttpClient"/> per reader, strict 206
    /// validation, manual redirects with the final URL cached for every later request, single-flight re-resolution
    /// when a cached signed URL expires (401/403/404), and explicit header / read-stall / whole-request timeouts.
    /// Timeouts never surface as a cancellation unless the caller's own token was cancelled.
    /// </summary>
    internal sealed class HttpRangeReader : IRangeReader
    {
        private static readonly string[] RedirectStatuses = { "301", "302", "303", "307", "308" };

        private readonly HttpClient _client;
        private readonly Uri _originalUri;
        private readonly IDictionary<string, string>? _headers;
        private readonly object _lock = new object();
        private Uri? _finalUri;
        private int _generation;
        private TaskCompletionSource<bool>? _resolving;
        private long _length = -1;
        private long _bodyBytesRead;
        private int _resolutionCount;
        private bool _disposed;

        /// <summary>Time allowed for the response headers of one request.</summary>
        public TimeSpan HeadersTimeout { get; set; } = RetryPolicy.HeadersTimeout;

        /// <summary>Time allowed for one body read to make progress.</summary>
        public TimeSpan ReadStallTimeout { get; set; } = RetryPolicy.ReadStallTimeout;

        /// <summary>
        /// Base time allowed for one whole request; each request adds the time its body takes at
        /// <see cref="RetryPolicy.MinRangeThroughputBytesPerSecond"/>.
        /// </summary>
        public TimeSpan RequestTimeout { get; set; } = RetryPolicy.RequestTimeout;

        /// <summary>The reader takes ownership of <paramref name="client"/>.</summary>
        public HttpRangeReader(HttpClient client, string url, IDictionary<string, string>? headers, double timeoutMinutes)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            if (string.IsNullOrEmpty(url)) throw new ArgumentNullException(nameof(url));
            _originalUri = new Uri(url, UriKind.Absolute);
            _headers = headers;
            if (timeoutMinutes > 0 && TimeSpan.FromMinutes(timeoutMinutes) < RetryPolicy.RequestTimeout) {
                RequestTimeout = TimeSpan.FromMinutes(timeoutMinutes);
            }
        }

        /// <inheritdoc />
        public long Length => Interlocked.Read(ref _length);

        /// <summary>Total body bytes read from the origin (diagnostics / tests).</summary>
        public long BodyBytesRead => Interlocked.Read(ref _bodyBytesRead);

        /// <summary>How many times the redirect chain was resolved from the original URL (diagnostics / tests).</summary>
        public int ResolutionCount => Volatile.Read(ref _resolutionCount);

        /// <summary>The cached final URL, or null when none is cached (diagnostics / tests).</summary>
        public Uri? CachedUri {
            get {
                lock (_lock) return _finalUri;
            }
        }

        /// <inheritdoc />
        public async Task ReadExactAsync(long offset, byte[] buffer, int bufferOffset, int count, CancellationToken cancelToken)
        {
            if (buffer == null) throw new ArgumentNullException(nameof(buffer));
            if (offset < 0) throw new ArgumentOutOfRangeException(nameof(offset));
            if (bufferOffset < 0 || count <= 0 || buffer.Length - bufferOffset < count) throw new ArgumentOutOfRangeException(nameof(count));
            if (_disposed) throw new ObjectDisposedException(nameof(HttpRangeReader));

            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancelToken);
            // scaled with the length so a large coalesced request on a slow link can complete (§5.5)
            requestCts.CancelAfter(RetryPolicy.GetRequestTimeout(RequestTimeout, count));
            try {
                await ReadCoreAsync(offset, buffer, bufferOffset, count, requestCts.Token).ConfigureAwait(false);
            } catch (OperationCanceledException ex) when (!cancelToken.IsCancellationRequested) {
                // HttpClient reports every timeout as a cancellation; only the caller's token is a real cancel.
                throw new RangeTransientException($"Range request {offset}-{offset + count - 1} timed out.", ex);
            }
        }

        private async Task ReadCoreAsync(long offset, byte[] buffer, int bufferOffset, int count, CancellationToken ct)
        {
            long end = offset + count - 1;
            var start = await GetStartAsync(ct).ConfigureAwait(false);
            HttpResponseMessage? response = null;
            bool resolved = false;
            try {
                Uri current = start.Uri;
                int hops = 0;
                while (true) {
                    ct.ThrowIfCancellationRequested();
                    response = await SendAsync(current, offset, end, ct).ConfigureAwait(false);
                    if (!IsRedirect(response)) break;
                    var location = response.Headers.Location;
                    if (location == null) throw new RangeUnavailableException($"Redirect ({(int) response.StatusCode}) without a Location header.");
                    var next = location.IsAbsoluteUri ? location : new Uri(current, location);
                    response.Dispose();
                    response = null;
                    if (++hops > RetryPolicy.MaxRedirects) throw new RangeUnavailableException("Too many redirects.");
                    current = next;
                }

                if (start.IsResolver) {
                    CompleteResolution(current);
                    resolved = true;
                } else if (hops > 0) {
                    // a cached target redirected again: remember where it ended up
                    CacheFinalUri(current);
                }

                int status = (int) response.StatusCode;
                if (status == 401 || status == 403 || status == 404) {
                    if (start.FromCache) {
                        bool invalidated = Invalidate(start.Generation);
                        throw new RangeRefreshException($"Cached download URL answered {status}; it has been invalidated.", invalidated);
                    }
                    throw new RangeUnavailableException($"The server answered {status} for a ranged request.");
                }
                if (status == 200) throw new RangeUnavailableException("The server ignored the Range header and answered 200.");
                if (status == 416) throw new RangeUnavailableException("The server answered 416 (range not satisfiable).");
                if (status == 408 || status == 425 || status == 429 || status == 500 || status == 502 || status == 503 || status == 504) {
                    throw new RangeTransientException($"The server answered {status}.", GetRetryAfter(response));
                }
                if (status >= 400 && status < 500) throw new RangeUnavailableException($"The server answered {status} for a ranged request.");
                if (status != 206) throw new RangeTransientException($"Unexpected status {status} for a ranged request.", GetRetryAfter(response));

                ValidateContentRange(response, offset, end);
                ValidateContentEncoding(response);
                long? contentLength = response.Content.Headers.ContentLength;
                if (contentLength.HasValue && contentLength.Value != count) {
                    throw new RangeUnavailableException($"Content-Length {contentLength.Value} does not match the requested range length {count}.");
                }

                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                int read = 0;
                while (read < count) {
                    int n = await ReadWithStallTimeoutAsync(response, stream, buffer, bufferOffset + read, count - read, ct).ConfigureAwait(false);
                    if (n <= 0) throw new RangeTransientException($"Short body: got {read} of {count} bytes.");
                    read += n;
                    Interlocked.Add(ref _bodyBytesRead, n);
                }
                if (!contentLength.HasValue) {
                    var probe = new byte[1];
                    if (await ReadWithStallTimeoutAsync(response, stream, probe, 0, 1, ct).ConfigureAwait(false) != 0) {
                        throw new RangeUnavailableException("The response body is longer than the requested range.");
                    }
                }
            } finally {
                if (start.IsResolver && !resolved) FailResolution();
                response?.Dispose();
            }
        }

        /// <summary>
        /// One body read bounded by <see cref="ReadStallTimeout"/> and <paramref name="ct"/>. The token alone is not enough:
        /// on .NET Framework the response stream does not observe it once a read is blocked (the base
        /// <c>Stream.ReadAsync</c> only checks it before starting), so a silent connection would hang until
        /// <c>HttpWebRequest.ReadWriteTimeout</c> (5 minutes) and a cancel would wait as long. When the token fires first,
        /// the response is disposed to tear the connection down and the read is abandoned.
        /// </summary>
        private async Task<int> ReadWithStallTimeoutAsync(HttpResponseMessage response, System.IO.Stream stream, byte[] buffer, int offset, int count,
            CancellationToken ct)
        {
            using var readCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            readCts.CancelAfter(ReadStallTimeout);
            var readTask = stream.ReadAsync(buffer, offset, count, readCts.Token);
            if (!readTask.IsCompleted) {
                var cancelled = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (readCts.Token.Register(() => cancelled.TrySetResult(true))) {
                    if (await Task.WhenAny(readTask, cancelled.Task).ConfigureAwait(false) != readTask) {
                        try {
                            response.Dispose();
                        } catch {
                            // best effort: the read is abandoned either way
                        }
                        // the abandoned read fails (or completes) later; observe it so it is never an unobserved exception
                        _ = readTask.ContinueWith(t => _ = t.Exception, CancellationToken.None,
                            TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                        readCts.Token.ThrowIfCancellationRequested();
                    }
                }
            }
            return await readTask.ConfigureAwait(false);
        }

        private async Task<HttpResponseMessage> SendAsync(Uri uri, long offset, long end, CancellationToken ct)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, uri);
            request.Headers.Range = new RangeHeaderValue(offset, end);
            request.Headers.AcceptEncoding.Clear();
            request.Headers.AcceptEncoding.Add(new StringWithQualityHeaderValue("identity"));
            if (_headers != null) {
                bool sameOrigin = SameOrigin(uri, _originalUri);
                foreach (var header in _headers) {
                    if (!sameOrigin && string.Equals(header.Key, "Authorization", StringComparison.OrdinalIgnoreCase)) continue;
                    // content headers (eg. Content-Type) do not apply to a GET without a body and are ignored
                    request.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            using var headersCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            headersCts.CancelAfter(HeadersTimeout);
            return await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, headersCts.Token).ConfigureAwait(false);
        }

        private static bool SameOrigin(Uri a, Uri b)
        {
            return string.Equals(a.Scheme, b.Scheme, StringComparison.OrdinalIgnoreCase)
                && string.Equals(a.Host, b.Host, StringComparison.OrdinalIgnoreCase)
                && a.Port == b.Port;
        }

        private static bool IsRedirect(HttpResponseMessage response)
        {
            var code = ((int) response.StatusCode).ToString(CultureInfo.InvariantCulture);
            return Array.IndexOf(RedirectStatuses, code) >= 0;
        }

        private static TimeSpan? GetRetryAfter(HttpResponseMessage response)
        {
            var ra = response.Headers.RetryAfter;
            if (ra == null) return null;
            if (ra.Delta.HasValue) return ra.Delta.Value;
            if (ra.Date.HasValue) return ra.Date.Value - DateTimeOffset.UtcNow;
            return null;
        }

        private void ValidateContentRange(HttpResponseMessage response, long offset, long end)
        {
            var cr = response.Content.Headers.ContentRange;
            if (cr == null || !string.Equals(cr.Unit, "bytes", StringComparison.OrdinalIgnoreCase) || !cr.HasRange || !cr.HasLength) {
                throw new RangeUnavailableException("The 206 response has no usable Content-Range header.");
            }
            if (cr.From != offset || cr.To != end) {
                throw new RangeUnavailableException($"Content-Range {cr.From}-{cr.To} does not match the requested range {offset}-{end}.");
            }
            long total = cr.Length!.Value;
            if (total <= end) throw new RangeUnavailableException($"Content-Range total {total} is not beyond the requested range.");
            long previous = Interlocked.CompareExchange(ref _length, total, -1);
            if (previous != -1 && previous != total) {
                throw new RangeUnavailableException($"The asset length changed between requests ({previous} -> {total}).");
            }
        }

        private static void ValidateContentEncoding(HttpResponseMessage response)
        {
            foreach (var enc in response.Content.Headers.ContentEncoding) {
                if (!string.Equals(enc, "identity", StringComparison.OrdinalIgnoreCase)) {
                    throw new RangeUnavailableException($"The server applied Content-Encoding '{enc}' to a ranged response.");
                }
            }
        }

        // ------------------------------------------------------------------------------------------------------------
        // Final URL cache with single-flight resolution
        // ------------------------------------------------------------------------------------------------------------

        private readonly struct StartInfo
        {
            public readonly Uri Uri;
            public readonly bool FromCache;
            public readonly bool IsResolver;
            public readonly int Generation;

            public StartInfo(Uri uri, bool fromCache, bool isResolver, int generation)
            {
                Uri = uri;
                FromCache = fromCache;
                IsResolver = isResolver;
                Generation = generation;
            }
        }

        private async Task<StartInfo> GetStartAsync(CancellationToken ct)
        {
            while (true) {
                Task waitFor;
                lock (_lock) {
                    if (_finalUri != null) return new StartInfo(_finalUri, true, false, _generation);
                    if (_resolving == null) {
                        _resolving = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                        return new StartInfo(_originalUri, false, true, _generation);
                    }
                    waitFor = _resolving.Task;
                }
                // another request is resolving the redirect chain: wait for it, then reuse its result
                var cancelTask = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                using (ct.Register(() => cancelTask.TrySetCanceled())) {
                    await Task.WhenAny(waitFor, cancelTask.Task).ConfigureAwait(false);
                }
                ct.ThrowIfCancellationRequested();
            }
        }

        private void CompleteResolution(Uri finalUri)
        {
            TaskCompletionSource<bool>? tcs;
            lock (_lock) {
                _finalUri = finalUri;
                _generation++;
                _resolutionCount++;
                tcs = _resolving;
                _resolving = null;
            }
            tcs?.TrySetResult(true);
        }

        private void FailResolution()
        {
            TaskCompletionSource<bool>? tcs;
            lock (_lock) {
                tcs = _resolving;
                _resolving = null;
            }
            tcs?.TrySetResult(false);
        }

        private void CacheFinalUri(Uri finalUri)
        {
            lock (_lock) {
                _finalUri = finalUri;
                _generation++;
            }
        }

        /// <summary>Drops the cached URL if it is still the generation the failed request used. Returns true when it did.</summary>
        private bool Invalidate(int generation)
        {
            lock (_lock) {
                if (_generation == generation && _finalUri != null) {
                    _finalUri = null;
                    return true;
                }
                return false;
            }
        }

        /// <inheritdoc />
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _client.Dispose();
        }
    }
}
