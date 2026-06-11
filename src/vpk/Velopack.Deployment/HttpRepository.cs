using FluentValidation;
using Microsoft.Extensions.Logging;
using Velopack.Core.Validation;
using Velopack.Sources;

namespace Velopack.Deployment;

public class HttpDownloadOptions : RepositoryOptions
{
    public string Url { get; set; }
}

public sealed class HttpDownloadOptionsValidator : RepositoryOptionsValidator<HttpDownloadOptions>
{
    public HttpDownloadOptionsValidator()
    {
        RuleFor(x => x.Url).NotEmpty().MustBeValidHttpUri();
    }
}

public class HttpRepository : SourceRepository<HttpDownloadOptions, SimpleWebSource>
{
    public HttpRepository(ILogger logger)
        : base(logger, new HttpDownloadOptionsValidator())
    { }

    public override SimpleWebSource CreateSource(HttpDownloadOptions options)
    {
        return new SimpleWebSource(options.Url, timeout: options.Timeout);
    }
}
