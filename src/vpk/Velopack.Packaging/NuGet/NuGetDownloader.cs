#nullable enable
using NuGet.Configuration;
using NuGet.Packaging.Core;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using NugetLogger = NuGet.Common.ILogger;

namespace Velopack.Packaging.NuGet;

public class NuGetDownloader
{
    private readonly NugetLogger _logger;
    private readonly PackageSource _packageSource;
    private readonly SourceRepository _sourceRepository;
    private readonly SourceCacheContext _sourceCacheContext;

    public NuGetDownloader()
        : this(global::NuGet.Common.NullLogger.Instance)
    { }

    public NuGetDownloader(NugetLogger logger)
    {
        _logger = logger;
        _packageSource = new PackageSource("https://api.nuget.org/v3/index.json", "NuGet.org");
        _sourceRepository = new SourceRepository(_packageSource, Repository.Provider.GetCoreV3());
        _sourceCacheContext = new SourceCacheContext();
    }

    public async Task<IPackageSearchMetadata> GetPackageMetadata(string packageName, string? version, CancellationToken cancellationToken)
    {
        PackageMetadataResource packageMetadataResource = _sourceRepository.GetResource<PackageMetadataResource>();
        FindPackageByIdResource packageByIdResource = _sourceRepository.GetResource<FindPackageByIdResource>();
        IPackageSearchMetadata? package = null;

        var prerelease = version?.Equals("pre", StringComparison.InvariantCultureIgnoreCase) == true;
        if (version is null || version.Equals("latest", StringComparison.InvariantCultureIgnoreCase) || prerelease) {
            // get latest (or prerelease) version
            IEnumerable<IPackageSearchMetadata> metadata = await packageMetadataResource
                .GetMetadataAsync(packageName, true, true, _sourceCacheContext, _logger, cancellationToken)
                .ConfigureAwait(false);
            package = metadata
                .Where(x => x.IsListed)
                .Where(x => prerelease || !x.Identity.Version.IsPrerelease)
                .OrderByDescending(x => x.Identity.Version)
                .FirstOrDefault();
        } else {
            // resolve version ranges and wildcards
            var versions = await packageByIdResource.GetAllVersionsAsync(packageName, _sourceCacheContext, _logger, cancellationToken)
                .ConfigureAwait(false);
            var resolved = versions.FindBestMatch(VersionRange.Parse(version), version => version);

            // get exact version
            var packageIdentity = new PackageIdentity(packageName, resolved);
            package = await packageMetadataResource
                .GetMetadataAsync(packageIdentity, _sourceCacheContext, _logger, cancellationToken)
                .ConfigureAwait(false);
        }

        if (package is null) {
            throw new Exception($"Unable to locate {packageName} {version} on NuGet.org");
        }

        return package;
    }

    public async Task DownloadPackageToStream(IPackageSearchMetadata package, Stream targetStream, CancellationToken cancellationToken)
    {
        FindPackageByIdResource packageByIdResource = _sourceRepository.GetResource<FindPackageByIdResource>();
        await packageByIdResource
            .CopyNupkgToStreamAsync(package.Identity.Id, package.Identity.Version, targetStream, _sourceCacheContext, _logger, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task DownloadPackageToStream(string packageName, string? version, Stream targetStream, CancellationToken cancellationToken)
    {
        IPackageSearchMetadata packageMetadata = await GetPackageMetadata(packageName, version, cancellationToken);

        await DownloadPackageToStream(packageMetadata, targetStream, cancellationToken);
    }
}