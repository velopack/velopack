using System.Collections.Concurrent;
using System.Net;

namespace Velopack.Deployment.Tests;

/// <summary>
/// A minimal in-process static file server backed by <see cref="HttpListener"/>, bound to a random
/// free port on <c>127.0.0.1</c>. Serves GET/HEAD requests from a root directory and records every
/// received request (path + headers) so tests can assert on things like custom download headers.
/// Reachable by external harness processes because it is a real socket.
/// </summary>
public sealed class StaticFileServer : IDisposable
{
    /// <summary> A request received by the server, captured for later assertion. </summary>
    public sealed record RecordedRequest(string PathAndQuery, IReadOnlyDictionary<string, string> Headers);

    private readonly HttpListener _listener;
    private readonly string _rootFull;
    private readonly ConcurrentQueue<RecordedRequest> _requests = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly ILogger _log;

    /// <summary> The base URL the server is listening on, e.g. <c>http://127.0.0.1:43521/</c> (trailing slash). </summary>
    public string BaseUrl { get; }

    /// <summary> A thread-safe snapshot view of all requests received so far. </summary>
    public IReadOnlyCollection<RecordedRequest> Requests => _requests;

    /// <summary> Serves <paramref name="rootDir"/> over HTTP on a random free loopback port. </summary>
    public StaticFileServer(string rootDir, ILogger? log = null)
    {
        _rootFull = Path.GetFullPath(rootDir);
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

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested) {
            HttpListenerContext ctx;
            try {
                ctx = await _listener.GetContextAsync();
            } catch (Exception) {
                break; // listener stopped/disposed
            }

            try {
                Handle(ctx);
            } catch (Exception ex) {
                _log.LogWarning(ex, "StaticFileServer failed to handle a request");
                try { ctx.Response.Abort(); } catch { /* ignore */ }
            }
        }
    }

    private void Handle(HttpListenerContext ctx)
    {
        var req = ctx.Request;
        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string key in req.Headers) {
            headers[key] = req.Headers[key] ?? "";
        }

        var pathAndQuery = req.Url?.PathAndQuery ?? req.RawUrl ?? "";
        _requests.Enqueue(new RecordedRequest(pathAndQuery, headers));

        var method = req.HttpMethod;
        if (method != "GET" && method != "HEAD") {
            ctx.Response.StatusCode = 405;
            ctx.Response.Close();
            return;
        }

        var relative = Uri.UnescapeDataString((req.Url?.AbsolutePath ?? "/").TrimStart('/'));
        var fullPath = Path.GetFullPath(Path.Combine(_rootFull, relative));
        var rootWithSep = _rootFull.EndsWith(Path.DirectorySeparatorChar) ? _rootFull : _rootFull + Path.DirectorySeparatorChar;

        if (!fullPath.StartsWith(rootWithSep, StringComparison.OrdinalIgnoreCase) || !File.Exists(fullPath)) {
            ctx.Response.StatusCode = 404;
            ctx.Response.Close();
            return;
        }

        var bytes = File.ReadAllBytes(fullPath);
        ctx.Response.StatusCode = 200;
        ctx.Response.ContentType = GetContentType(fullPath);
        ctx.Response.ContentLength64 = bytes.Length;

        if (method == "HEAD") {
            ctx.Response.Close();
            return;
        }

        ctx.Response.OutputStream.Write(bytes, 0, bytes.Length);
        ctx.Response.OutputStream.Close();
        ctx.Response.Close();
    }

    private static string GetContentType(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
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
