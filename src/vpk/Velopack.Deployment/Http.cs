using System.Net;
using FluentValidation;
using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Validation;
using Velopack.Sources;

namespace Velopack.Deployment;

public class HttpDownloadOptions : RepositoryOptions
{
    public string Url { get; set; }

    public string[] Headers { get; set; }

    public bool AllowEmptyChannel { get; set; }
}

public sealed class HttpDownloadOptionsValidator : RepositoryOptionsValidator<HttpDownloadOptions>
{
    public HttpDownloadOptionsValidator()
    {
        RuleFor(x => x.Url).NotEmpty().MustBeValidHttpUri();
        RuleForEach(x => x.Headers)
            .Must(h => !string.IsNullOrWhiteSpace(h) && h.IndexOf(':') > 0 && !string.IsNullOrWhiteSpace(h.Substring(0, h.IndexOf(':'))))
            .WithMessage("{PropertyName} must be in the format 'Name: Value' ('{PropertyValue}').");
    }
}

/// <summary>
/// An <see cref="HttpClientFileDownloader"/> which adds a fixed set of custom headers to every request.
/// </summary>
public class StaticHeadersFileDownloader(IDictionary<string, string> customHeaders) : HttpClientFileDownloader
{
    protected override HttpClient CreateHttpClient(IDictionary<string, string> headers, double timeout)
    {
        var client = base.CreateHttpClient(headers, timeout);
        foreach (var header in customHeaders) {
            client.DefaultRequestHeaders.TryAddWithoutValidation(header.Key, header.Value);
        }

        return client;
    }
}

public class HttpDownloadCommandRunner(ILogger logger)
    : SourceDownloadCommandRunner<HttpDownloadOptions, HttpDownloadOptionsValidator>(logger)
{
    protected override IUpdateSource CreateSource(HttpDownloadOptions options)
    {
        var headers = ParseHeaders(options.Headers);
        var downloader = headers.Count > 0 ? new StaticHeadersFileDownloader(headers) : null;
        return new SimpleWebSource(options.Url, downloader, timeout: options.Timeout);
    }

    protected override async Task<VelopackAssetFeed> GetReleasesAsync(HttpDownloadOptions options)
    {
        try {
            return await base.GetReleasesAsync(options).ConfigureAwait(false);
        } catch (HttpRequestException ex) when (options.AllowEmptyChannel && ex.StatusCode == HttpStatusCode.NotFound) {
            Log.Warn($"No releases file found for channel '{options.Channel}' (HTTP 404), returning empty feed because 'allowEmptyChannel' is enabled.");
            return new VelopackAssetFeed();
        }
    }

    internal static Dictionary<string, string> ParseHeaders(string[] headers)
    {
        var result = new Dictionary<string, string>();
        foreach (var header in headers ?? []) {
            var idx = header?.IndexOf(':') ?? -1;
            if (idx <= 0) {
                throw new UserInfoException($"Invalid header '{header}', must be in the format 'Name: Value'.");
            }

            result[header.Substring(0, idx).Trim()] = header.Substring(idx + 1).Trim();
        }

        return result;
    }
}
