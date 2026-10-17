#nullable enable
using System.Collections.Concurrent;
using System.Net;
using System.Threading;

namespace Velopack.TestCommon;

/// <summary>
/// A minimal in-process static file server backed by <see cref="HttpListener"/>, bound to a random
/// free port on <c>127.0.0.1</c>. Serves GET/HEAD requests (with single <c>Range</c> support) from a root
/// directory and records every received request (path + headers) so tests can assert on things like custom
/// download headers or ranged access. Faults (resets, 5xx, stalls, bad ranges, redirects...) can be scripted
/// through <see cref="Faults"/>. Reachable by external harness processes because it is a real socket.
/// </summary>
public sealed class StaticFileServer : IDisposable
{
    /// <summary> A request received by the server, captured for later assertion. </summary>
    public sealed record RecordedRequest(string PathAndQuery, IReadOnlyDictionary<string, string> Headers)
    {
        /// <summary> The request path without the query string. </summary>
        public string Path => PathAndQuery.Split('?')[0];

        /// <summary> The raw <c>Range</c> header, or null. </summary>
        public string? Range => Headers.TryGetValue("Range", out var r) ? r : null;

        /// <summary> True when the request carried a <c>Range</c> header. </summary>
        public bool IsRanged => Range != null;
    }

    private readonly HttpListener _listener;
    private readonly string _rootFull;
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();
    private readonly ConcurrentDictionary<string, Func<int, string>> _redirects = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, int> _redirectHits = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _aliases = new(StringComparer.Ordinal);
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _log;
    private long _bodyBytesSent;

    /// <summary> The base URL the server is listening on, e.g. <c>http://127.0.0.1:43521/</c> (trailing slash). </summary>
    public string BaseUrl { get; }

    /// <summary> A thread-safe snapshot view of all requests received so far. </summary>
    public IReadOnlyCollection<RecordedRequest> Requests => _requests;

    /// <summary> Scripted faults applied to matching requests. </summary>
    public FaultPlan Faults { get; } = new();

    /// <summary> Total body bytes written to clients (headers excluded). </summary>
    public long BodyBytesSent => Interlocked.Read(ref _bodyBytesSent);

    /// <summary> Serves <paramref name="rootDir"/> over HTTP on a random free loopback port. </summary>
    public StaticFileServer(string rootDir, ILogger? log = null)
    {
        _rootFull = System.IO.Path.GetFullPath(rootDir);
        _log = log ?? NullLogger.Instance;

        var rnd = new Random();
        HttpListener? bound = null;
        string? baseUrl = null;
        for (var attempt = 0; attempt < 50; attempt++) {
            var port = rnd.Next(20000, 60000);
            var url = $"http://127.0.0.1:{port}/";
            var listener = new HttpListener();
            listener.Prefixes.Add(url);
            try {
                listener.Start();
                bound = listener;
                baseUrl = url;
                break;
            } catch (HttpListenerException) {
                try { listener.Close(); } catch { /* ignore */ }
            }
        }

        if (bound == null || baseUrl == null)
            throw new IOException("StaticFileServer could not bind to a free loopback port after 50 attempts.");

        _listener = bound;
        BaseUrl = baseUrl;
        _log.LogInformation("StaticFileServer listening on {BaseUrl} serving {Root}", BaseUrl, _rootFull);
        _ = Task.Run(() => AcceptLoopAsync(_cts.Token));
    }

    /// <summary>
    /// Answers every GET of <paramref name="requestPath"/> (eg. <c>/pkg.nupkg</c>) with a 302 to the location produced by
    /// <paramref name="location"/>, which receives the 1-based hit count (eg. to mint a fresh signed URL per resolution).
    /// </summary>
    public void Redirect(string requestPath, Func<int, string> location)
    {
        _redirects[requestPath] = location;
    }

    /// <summary> Number of redirects served for <paramref name="requestPath"/>. </summary>
    public int RedirectHits(string requestPath) => _redirectHits.TryGetValue(requestPath, out var n) ? n : 0;

    /// <summary> Serves <paramref name="requestPath"/> (query ignored) from <paramref name="relativeFile"/> under the root. </summary>
    public void Alias(string requestPath, string relativeFile)
    {
        _aliases[requestPath] = relativeFile;
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested) {
            HttpListenerContext ctx;
            try {
                ctx = await _listener.GetContextAsync();
            } catch (Exception) {
                break; // listener stopped/disposed
            }

            // handle each request on its own task so a stalled response never blocks the accept loop
            _ = Task.Run(() => {
                try {
                    Handle(ctx, ct);
                } catch (Exception ex) {
                    _log.LogDebug(ex, "StaticFileServer failed to handle a request");
                    try { ctx.Response.Abort(); } catch { /* ignore */ }
                }
            }, CancellationToken.None);
        }
    }

    private void Handle(HttpListenerContext ctx, CancellationToken ct)
    {
        var req = ctx.Request;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in req.Headers) {
            headers[key] = req.Headers[key] ?? "";
        }

        var pathAndQuery = req.Url?.PathAndQuery ?? req.RawUrl ?? "";
        var recorded = new RecordedRequest(pathAndQuery, headers);
        _requests.Enqueue(recorded);

        var method = req.HttpMethod;
        if (method != "GET" && method != "HEAD") {
            ctx.Response.StatusCode = 405;
            ctx.Response.Close();
            return;
        }

        var absolutePath = req.Url?.AbsolutePath ?? "/";
        if (_redirects.TryGetValue(absolutePath, out var redirect)) {
            var hits = _redirectHits.AddOrUpdate(absolutePath, 1, (_, n) => n + 1);
            ctx.Response.StatusCode = 302;
            ctx.Response.RedirectLocation = redirect(hits);
            ctx.Response.Close();
            return;
        }

        var relative = _aliases.TryGetValue(absolutePath, out var alias) ? alias : Uri.UnescapeDataString(absolutePath.TrimStart('/'));
        var fullPath = System.IO.Path.GetFullPath(System.IO.Path.Combine(_rootFull, relative));
        var rootWithSep = _rootFull.EndsWith(System.IO.Path.DirectorySeparatorChar) ? _rootFull : _rootFull + System.IO.Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath)) {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        var bytes = File.ReadAllBytes(fullPath);
        var fault = Faults.Next(recorded) ?? Fault.None;
        var resp = ctx.Response;
        resp.ContentType = GetContentType(fullPath);

        if (fault is Fault.StatusFault sf) {
            resp.StatusCode = sf.Code;
            if (sf.RetryAfter != null) resp.AddHeader("Retry-After", sf.RetryAfter);
            if (sf.Code == 416) resp.AddHeader("Content-Range", $"bytes */{bytes.Length}");
            resp.Close();
            return;
        }

        if (fault is Fault.RedirectFault rf) {
            resp.StatusCode = 302;
            resp.RedirectLocation = rf.Location;
            resp.Close();
            return;
        }

        long start = 0, end = bytes.Length - 1;
        bool ranged = false;
        if (recorded.Range != null && fault is not Fault.IgnoreRangeFault) {
            if (!TryParseRange(recorded.Range, bytes.Length, out start, out end)) {
                resp.StatusCode = 416;
                resp.AddHeader("Content-Range", $"bytes */{bytes.Length}");
                resp.Close();
                return;
            }
            ranged = true;
        }

        var body = new byte[end - start + 1];
        Buffer.BlockCopy(bytes, (int) start, body, 0, body.Length);

        if (fault is Fault.CorruptBytesFault cf) {
            for (int i = 0; i < Math.Min(cf.Count, body.Length); i++) body[i] ^= 0x5A;
        }

        resp.StatusCode = ranged ? 206 : 200;
        if (ranged) {
            var crStart = start;
            var crEnd = end;
            if (fault is Fault.WrongContentRangeFault) {
                crStart += 1;
                crEnd += 1;
            }
            resp.AddHeader("Content-Range", $"bytes {crStart}-{crEnd}/{bytes.Length}");
            resp.AddHeader("Accept-Ranges", "bytes");
        }
        if (fault is Fault.GzipFault) {
            resp.AddHeader("Content-Encoding", "gzip");
        }
        resp.ContentLength64 = body.Length;

        if (method == "HEAD") {
            resp.Close();
            return;
        }

        switch (fault) {
        case Fault.ShortBodyFault sb: {
            int n = Math.Min(sb.Bytes, body.Length);
            resp.OutputStream.Write(body, 0, n);
            resp.OutputStream.Flush();
            Interlocked.Add(ref _bodyBytesSent, n);
            try { resp.OutputStream.Close(); } catch { /* the client sees a premature end of body */ }
            try { resp.Close(); } catch { /* ignore */ }
            return;
        }
        case Fault.ResetAfterFault ra: {
            int n = Math.Min(ra.Bytes, body.Length);
            if (n > 0) {
                resp.OutputStream.Write(body, 0, n);
                resp.OutputStream.Flush();
                Interlocked.Add(ref _bodyBytesSent, n);
            }
            resp.Abort();
            return;
        }
        case Fault.StallFault st: {
            int n = Math.Min(st.AfterBytes, body.Length);
            if (n > 0) {
                resp.OutputStream.Write(body, 0, n);
                Interlocked.Add(ref _bodyBytesSent, n);
            }
            resp.OutputStream.Flush();
            try {
                Task.Delay(st.Duration, ct).Wait(ct);
            } catch (OperationCanceledException) {
                // server disposed
            } catch (AggregateException) {
                // server disposed
            }
            resp.Abort();
            return;
        }
        }

        resp.OutputStream.Write(body, 0, body.Length);
        Interlocked.Add(ref _bodyBytesSent, body.Length);
        resp.OutputStream.Close();
        resp.Close();
    }

    private static bool TryParseRange(string header, long length, out long start, out long end)
    {
        start = end = 0;
        if (!header.StartsWith("bytes=", StringComparison.OrdinalIgnoreCase)) return false;
        var spec = header.Substring(6);
        if (spec.Contains(',')) return false;
        var dash = spec.IndexOf('-');
        if (dash < 0) return false;
        var a = spec.Substring(0, dash);
        var b = spec.Substring(dash + 1);
        if (a.Length == 0) {
            // suffix range: last N bytes
            if (!long.TryParse(b, out var suffix) || suffix <= 0) return false;
            start = Math.Max(0, length - suffix);
            end = length - 1;
            return start <= end;
        }
        if (!long.TryParse(a, out start)) return false;
        if (b.Length == 0) end = length - 1;
        else if (!long.TryParse(b, out end)) return false;
        if (start < 0 || start >= length) return false;
        if (end >= length) end = length - 1;
        return start <= end;
    }

    private static string GetContentType(string path)
    {
        var ext = System.IO.Path.GetExtension(path).ToLowerInvariant();
        return ext switch {
            ".json" => "application/json",
            ".nupkg" => "application/octet-stream",
            _ => "application/octet-stream",
        };
    }

    /// <inheritdoc />
    public void Dispose()
    {
        _cts.Cancel();
        try { _listener.Stop(); } catch { /* ignore */ }
        try { _listener.Close(); } catch { /* ignore */ }
        _cts.Dispose();
    }
}

/// <summary>
/// A scripted response fault for <see cref="StaticFileServer"/>. Faults are consumed from a queue by every matching
/// request (see <see cref="FaultPlan"/>); <see cref="None"/> lets a request through unchanged.
/// </summary>
public abstract class Fault
{
    /// <summary> No fault: the request is served normally. </summary>
    public static readonly Fault None = new NoneFault();

    /// <summary> Answer with the given status code (eg. 500, 503, 429, 403, 416) and an optional <c>Retry-After</c> header. </summary>
    public static Fault Status(int code, string? retryAfter = null) => new StatusFault(code, retryAfter);

    /// <summary> Ignore the Range header and answer 200 with the whole file. </summary>
    public static Fault IgnoreRange() => new IgnoreRangeFault();

    /// <summary> Answer 206 whose Content-Range is off by one. </summary>
    public static Fault WrongContentRange() => new WrongContentRangeFault();

    /// <summary> Declare the full length but end the body after <paramref name="bytes"/> bytes. </summary>
    public static Fault ShortBody(int bytes) => new ShortBodyFault(bytes);

    /// <summary> Write <paramref name="bytes"/> bytes then reset the connection. </summary>
    public static Fault ResetAfter(int bytes) => new ResetAfterFault(bytes);

    /// <summary>
    /// Send the headers (and <paramref name="afterBytes"/> body bytes), then send nothing for <paramref name="duration"/> and reset.
    /// </summary>
    public static Fault Stall(TimeSpan duration, int afterBytes = 0) => new StallFault(duration, afterBytes);

    /// <summary> Answer with a <c>Content-Encoding: gzip</c> header (body unchanged). </summary>
    public static Fault Gzip() => new GzipFault();

    /// <summary> Answer with correct headers but the first <paramref name="count"/> body bytes corrupted. </summary>
    public static Fault CorruptBytes(int count) => new CorruptBytesFault(count);

    /// <summary> Answer with a 302 to <paramref name="location"/>. </summary>
    public static Fault RedirectTo(string location) => new RedirectFault(location);

    internal sealed class NoneFault : Fault { }

    internal sealed class StatusFault : Fault
    {
        public int Code { get; }
        public string? RetryAfter { get; }
        public StatusFault(int code, string? retryAfter) { Code = code; RetryAfter = retryAfter; }
    }

    internal sealed class IgnoreRangeFault : Fault { }

    internal sealed class WrongContentRangeFault : Fault { }

    internal sealed class ShortBodyFault : Fault
    {
        public int Bytes { get; }
        public ShortBodyFault(int bytes) { Bytes = bytes; }
    }

    internal sealed class ResetAfterFault : Fault
    {
        public int Bytes { get; }
        public ResetAfterFault(int bytes) { Bytes = bytes; }
    }

    internal sealed class StallFault : Fault
    {
        public TimeSpan Duration { get; }
        public int AfterBytes { get; }
        public StallFault(TimeSpan duration, int afterBytes) { Duration = duration; AfterBytes = afterBytes; }
    }

    internal sealed class GzipFault : Fault { }

    internal sealed class CorruptBytesFault : Fault
    {
        public int Count { get; }
        public CorruptBytesFault(int count) { Count = count; }
    }

    internal sealed class RedirectFault : Fault
    {
        public string Location { get; }
        public RedirectFault(string location) { Location = location; }
    }
}

/// <summary>
/// The fault script of a <see cref="StaticFileServer"/>: rules are evaluated first (the first non-null answer wins),
/// then the next queued fault is consumed. Only requests accepted by <see cref="Filter"/> (by default: ranged requests)
/// consume faults; everything else is served normally.
/// </summary>
public sealed class FaultPlan
{
    private readonly ConcurrentQueue<Fault> _queue = new();
    private readonly List<Func<StaticFileServer.RecordedRequest, int, Fault?>> _rules = new();
    private readonly object _lock = new();
    private int _matched;
    private int _applied;

    /// <summary> Which requests take part in the fault script. Default: requests carrying a Range header. </summary>
    public Func<StaticFileServer.RecordedRequest, bool> Filter { get; set; } = r => r.IsRanged;

    /// <summary> Number of matching requests seen so far. </summary>
    public int Matched => Volatile.Read(ref _matched);

    /// <summary> Number of faults (other than <see cref="Fault.None"/>) applied so far. </summary>
    public int Applied => Volatile.Read(ref _applied);

    /// <summary> Queues faults to be consumed, in order, by the next matching requests. </summary>
    public void Enqueue(params Fault[] faults)
    {
        foreach (var f in faults) _queue.Enqueue(f);
    }

    /// <summary> Queues <paramref name="count"/> copies of <paramref name="fault"/>. </summary>
    public void EnqueueRepeated(Fault fault, int count)
    {
        for (int i = 0; i < count; i++) _queue.Enqueue(fault);
    }

    /// <summary>
    /// Adds a rule evaluated for every matching request with its 1-based match ordinal; a non-null result is applied
    /// (and nothing is dequeued), null falls through to the next rule and finally the queue.
    /// </summary>
    public void AddRule(Func<StaticFileServer.RecordedRequest, int, Fault?> rule)
    {
        lock (_lock) _rules.Add(rule);
    }

    internal Fault? Next(StaticFileServer.RecordedRequest request)
    {
        if (!Filter(request)) return null;
        int ordinal = Interlocked.Increment(ref _matched);
        Fault? result = null;
        lock (_lock) {
            foreach (var rule in _rules) {
                result = rule(request, ordinal);
                if (result != null) break;
            }
        }
        if (result == null && _queue.TryDequeue(out var queued)) result = queued;
        if (result != null && result is not Fault.NoneFault) Interlocked.Increment(ref _applied);
        return result;
    }
}
