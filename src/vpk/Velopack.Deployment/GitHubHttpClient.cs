using System.Collections.ObjectModel;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using Octokit;
using Octokit.Internal;

namespace Velopack.Deployment;

public class GitHubHttpClient : IHttpClient
{
    private HttpClient _client;

    public const string RedirectCountKey = "RedirectCount";

    public const string ReceivedTimeHeaderName = "X-Octokit-ReceivedDate";

    public GitHubHttpClient(TimeSpan timeout)
    {
        var handler = new HttpClientHandler {
            AllowAutoRedirect = false
        };

        if (handler.SupportsAutomaticDecompression) {
            handler.AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate;
        }

        _client = new HttpClient(handler);
        _client.Timeout = timeout;
    }

    public void Dispose()
    {
        _client.Dispose();
        _client = null;
    }

    public async Task<IResponse> Send(IRequest request, CancellationToken cancellationToken, Func<object, object> preprocessResponseBody = null)
    {
        if (_client == null) {
            throw new ObjectDisposedException(nameof(GitHubHttpClient));
        }

        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }


        using (var requestMessage = BuildRequestMessage(request)) {
            var responseMessage = await SendAsync(requestMessage).ConfigureAwait(false);

            return await BuildResponse(responseMessage, preprocessResponseBody).ConfigureAwait(false);
        }
    }

    public async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request)
    {
        // Clone the request/content in case we get a redirect
        var clonedRequest = await CloneHttpRequestMessageAsync(request).ConfigureAwait(false);

        // Send initial response
        var response = await _client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, CancellationToken.None).ConfigureAwait(false);
        // Need to determine time on client computer as soon as possible.
        var receivedTime = DateTimeOffset.Now;
        // Since Properties are stored as objects, serialize to HTTP round-tripping string (Format: r)
        // Resolution is limited to one-second, matching the resolution of the HTTP Date header
        request.Properties[ReceivedTimeHeaderName] =
            receivedTime.ToString("r", CultureInfo.InvariantCulture);

        // Can't redirect without somewhere to redirect to.
        if (response.Headers.Location == null) {
            return response;
        }

        // Don't redirect if we exceed max number of redirects
        var redirectCount = 0;
        if (request.Properties.Keys.Contains(RedirectCountKey)) {
            redirectCount = (int) request.Properties[RedirectCountKey];
        }

        if (redirectCount > 3) {
            throw new InvalidOperationException("The redirect count for this request has been exceeded. Aborting.");
        }

        if (response.StatusCode == HttpStatusCode.MovedPermanently
            || response.StatusCode == HttpStatusCode.Redirect
            || response.StatusCode == HttpStatusCode.Found
            || response.StatusCode == HttpStatusCode.SeeOther
            || response.StatusCode == HttpStatusCode.TemporaryRedirect
            || (int) response.StatusCode == 308) {
            if (response.StatusCode == HttpStatusCode.SeeOther) {
                clonedRequest.Content = null;
                clonedRequest.Method = HttpMethod.Get;
            }

            // Increment the redirect count
            clonedRequest.Properties[RedirectCountKey] = ++redirectCount;

            // Set the new Uri based on location header
            clonedRequest.RequestUri = response.Headers.Location;

            // Clear authentication if redirected to a different host
            if (string.Compare(clonedRequest.RequestUri.Host, request.RequestUri.Host, StringComparison.OrdinalIgnoreCase) != 0) {
                clonedRequest.Headers.Authorization = null;
            }

            // Send redirected request
            response = await SendAsync(clonedRequest).ConfigureAwait(false);
        }

        return response;
    }

    protected virtual HttpRequestMessage BuildRequestMessage(IRequest request)
    {
        if (request == null) {
            throw new ArgumentNullException(nameof(request));
        }

        HttpRequestMessage requestMessage = null;
        try {
            var fullUri = new Uri(request.BaseAddress, request.Endpoint);
            requestMessage = new HttpRequestMessage(request.Method, fullUri);

            foreach (var header in request.Headers) {
                requestMessage.Headers.Add(header.Key, header.Value);
            }

            var httpContent = request.Body as HttpContent;
            if (httpContent != null) {
                requestMessage.Content = httpContent;
            }

            var body = request.Body as string;
            if (body != null) {
                requestMessage.Content = new StringContent(body, Encoding.UTF8, request.ContentType);
            }

            var bodyStream = request.Body as Stream;
            if (bodyStream != null) {
                requestMessage.Content = new StreamContent(bodyStream);
                requestMessage.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue(request.ContentType);
            }
        } catch (Exception) {
            if (requestMessage != null) {
                requestMessage.Dispose();
            }

            throw;
        }

        return requestMessage;
    }

    static string GetContentMediaType(HttpContent httpContent)
    {
        if (httpContent.Headers?.ContentType != null) {
            return httpContent.Headers.ContentType.MediaType;
        }

        // Issue #2898 - Bad "zip" Content-Type coming from Blob Storage for artifacts
        if (httpContent.Headers?.TryGetValues("Content-Type", out var contentTypeValues) == true
            && contentTypeValues.FirstOrDefault() == "zip") {
            return "application/zip";
        }

        return null;
    }

    protected virtual async Task<IResponse> BuildResponse(HttpResponseMessage responseMessage, Func<object, object> preprocessResponseBody)
    {
        if (responseMessage == null) {
            throw new ArgumentNullException(nameof(responseMessage));
        }

        object responseBody = null;
        string contentType = null;

        // We added support for downloading images,zip-files and application/octet-stream.
        // Let's constrain this appropriately.
        var binaryContentTypes = new[] {
            AcceptHeaders.RawContentMediaType,
            "application/zip",
            "application/x-gzip",
            "application/octet-stream"
        };

        var content = responseMessage.Content;
        if (content != null) {
            contentType = GetContentMediaType(content);

            if (contentType != null && (contentType.StartsWith("image/") || binaryContentTypes
                    .Any(item => item.Equals(contentType, StringComparison.OrdinalIgnoreCase)))) {
                responseBody = await content.ReadAsStreamAsync().ConfigureAwait(false);
            } else {
                responseBody = await content.ReadAsStringAsync().ConfigureAwait(false);
                content.Dispose();
            }

            if (!(preprocessResponseBody is null))
                responseBody = preprocessResponseBody(responseBody);
        }

        var responseHeaders = responseMessage.Headers.ToDictionary(h => h.Key, h => h.Value.First());

        // Add Client response received time as a synthetic header
        const string receivedTimeHeaderName = ReceivedTimeHeaderName;
        if (responseMessage.RequestMessage?.Properties is IDictionary<string, object> reqProperties
            && reqProperties.TryGetValue(receivedTimeHeaderName, out object receivedTimeObj)
            && receivedTimeObj is string receivedTimeString
            && !responseHeaders.ContainsKey(receivedTimeHeaderName)) {
            responseHeaders[receivedTimeHeaderName] = receivedTimeString;
        }

        return new GitHubResponse(
            responseMessage.StatusCode,
            responseBody,
            responseHeaders,
            contentType);
    }

    public static async Task<HttpRequestMessage> CloneHttpRequestMessageAsync(HttpRequestMessage oldRequest)
    {
        var newRequest = new HttpRequestMessage(oldRequest.Method, oldRequest.RequestUri);

        // Copy the request's content (via a MemoryStream) into the cloned object
        var ms = new MemoryStream();
        if (oldRequest.Content != null) {
            await oldRequest.Content.CopyToAsync(ms).ConfigureAwait(false);
            ms.Position = 0;
            newRequest.Content = new StreamContent(ms);

            // Copy the content headers
            if (oldRequest.Content.Headers != null) {
                foreach (var h in oldRequest.Content.Headers) {
                    newRequest.Content.Headers.Add(h.Key, h.Value);
                }
            }
        }

        newRequest.Version = oldRequest.Version;

        foreach (var header in oldRequest.Headers) {
            newRequest.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        foreach (var property in oldRequest.Properties) {
            newRequest.Properties.Add(property);
        }

        return newRequest;
    }

    public void SetRequestTimeout(TimeSpan timeout)
    {
        // noop
    }

    private class GitHubResponse : IResponse
    {
        public GitHubResponse(HttpStatusCode statusCode, object body, IDictionary<string, string> headers, string contentType)
        {
            if (headers == null) {
                throw new ArgumentNullException(nameof(headers));
            }

            StatusCode = statusCode;
            Body = body;
            Headers = new ReadOnlyDictionary<string, string>(headers);
            ApiInfo = ApiInfoParser.ParseResponseHeaders(headers);
            ContentType = contentType;
        }

        /// <inheritdoc />
        public object Body { get; private set; }

        /// <summary>
        /// Information about the API.
        /// </summary>
        public IReadOnlyDictionary<string, string> Headers { get; private set; }

        /// <summary>
        /// Information about the API response parsed from the response headers.
        /// </summary>
        public ApiInfo ApiInfo { get; internal set; } // This setter is internal for use in tests.

        /// <summary>
        /// The response status code.
        /// </summary>
        public HttpStatusCode StatusCode { get; private set; }

        /// <summary>
        /// The content type of the response.
        /// </summary>
        public string ContentType { get; private set; }
    }

    private static class ApiInfoParser
    {
        const RegexOptions regexOptions =
#if HAS_REGEX_COMPILED_OPTIONS
            RegexOptions.Compiled |
#endif
            RegexOptions.IgnoreCase;

        static readonly Regex _linkRelRegex = new Regex("rel=\"(next|prev|first|last)\"", regexOptions);
        static readonly Regex _linkUriRegex = new Regex("<(.+)>", regexOptions);

        static KeyValuePair<string, string> LookupHeader(IDictionary<string, string> headers, string key)
        {
            return headers.FirstOrDefault(h => string.Equals(h.Key, key, StringComparison.OrdinalIgnoreCase));
        }

        static bool Exists(KeyValuePair<string, string> kvp)
        {
            return !kvp.Equals(default(KeyValuePair<string, string>));
        }

        public static ApiInfo ParseResponseHeaders(IDictionary<string, string> responseHeaders)
        {
            if (responseHeaders == null) {
                throw new ArgumentNullException(nameof(responseHeaders));
            }

            var httpLinks = new Dictionary<string, Uri>();
            var oauthScopes = new List<string>();
            var acceptedOauthScopes = new List<string>();
            string etag = null;

            var acceptedOauthScopesKey = LookupHeader(responseHeaders, "X-Accepted-OAuth-Scopes");
            if (Exists(acceptedOauthScopesKey)) {
                acceptedOauthScopes.AddRange(
                    acceptedOauthScopesKey.Value
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim()));
            }

            var oauthScopesKey = LookupHeader(responseHeaders, "X-OAuth-Scopes");
            if (Exists(oauthScopesKey)) {
                oauthScopes.AddRange(
                    oauthScopesKey.Value
                        .Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
                        .Select(x => x.Trim()));
            }

            var etagKey = LookupHeader(responseHeaders, "ETag");
            if (Exists(etagKey)) {
                etag = etagKey.Value;
            }

            var linkKey = LookupHeader(responseHeaders, "Link");
            if (Exists(linkKey)) {
                var links = linkKey.Value.Split(',');
                foreach (var link in links) {
                    var relMatch = _linkRelRegex.Match(link);
                    if (!relMatch.Success || relMatch.Groups.Count != 2) break;

                    var uriMatch = _linkUriRegex.Match(link);
                    if (!uriMatch.Success || uriMatch.Groups.Count != 2) break;

                    httpLinks.Add(relMatch.Groups[1].Value, new Uri(uriMatch.Groups[1].Value));
                }
            }

            var receivedTimeKey = LookupHeader(responseHeaders, ReceivedTimeHeaderName);
            var serverTimeKey = LookupHeader(responseHeaders, "Date");
            TimeSpan serverTimeSkew = TimeSpan.Zero;
            if (DateTimeOffset.TryParse(receivedTimeKey.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var receivedTime)
                && DateTimeOffset.TryParse(serverTimeKey.Value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var serverTime)) {
                serverTimeSkew = serverTime - receivedTime;
            }

            return new ApiInfo(httpLinks, oauthScopes, acceptedOauthScopes, etag, new RateLimit(responseHeaders), serverTimeSkew);
        }
    }
}