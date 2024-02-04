using Microsoft.Extensions.Logging;
using Velopack.Sources;

namespace Velopack.Deployment;

public class PathDownloadOptions : RepositoryOptions
{
    public DirectoryInfo Path { get; set; }
}

public class PathRepository(ILogger logger) : SourceRepository<PathDownloadOptions, SimpleFileSource>(logger)
{
    public override SimpleFileSource CreateSource(PathDownloadOptions options)
    {
        return new SimpleFileSource(options.Path);
    }
}
