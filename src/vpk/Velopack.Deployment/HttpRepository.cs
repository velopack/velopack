using Microsoft.Extensions.Logging;
using Velopack.Sources;

namespace Velopack.Deployment;

public class HttpDownloadOptions : RepositoryOptions, IObjectDownloadOptions
{
    public string Url { get; set; }

    public bool UpdateReleasesFile { get; set; }
}

public class HttpRepository : SourceRepository<HttpDownloadOptions, HttpDownloadOptions, SimpleWebSource>
{
    public HttpRepository(ILogger logger)
        : base(logger)
    { }

    public override SimpleWebSource CreateSource(HttpDownloadOptions options)
    {
        return new SimpleWebSource(options.Url);
    }
}
