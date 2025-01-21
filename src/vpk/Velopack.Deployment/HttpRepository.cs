using Microsoft.Extensions.Logging;
using Velopack.Sources;

namespace Velopack.Deployment;

public class HttpDownloadOptions : RepositoryOptions
{
    public string? Url { get; set; }
}

public class HttpRepository : SourceRepository<HttpDownloadOptions, SimpleWebSource>
{
    public HttpRepository(ILogger logger)
        : base(logger)
    { }

    public override SimpleWebSource CreateSource(HttpDownloadOptions options)
    {
        return new SimpleWebSource(options.Url, timeout: options.Timeout);
    }
}
