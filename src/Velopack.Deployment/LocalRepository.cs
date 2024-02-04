using Microsoft.Extensions.Logging;
using Velopack.Sources;

namespace Velopack.Deployment;

public class LocalDownloadOptions : RepositoryOptions
{
    public DirectoryInfo Path { get; set; }
}

public class LocalRepository(ILogger logger) : SourceRepository<LocalDownloadOptions, SimpleFileSource>(logger)
{
    public override SimpleFileSource CreateSource(LocalDownloadOptions options)
    {
        return new SimpleFileSource(options.Path);
    }
}
