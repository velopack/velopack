using System.IO.Compression;
using NuGet.Packaging;
using NuGet.Versioning;

namespace PrNuGetProxy;

public sealed class PackageCache(ProxyOptions options, ArtifactCache artifacts)
{
    private readonly SemaphoreSlim _gate = new(1);

    public async Task<string> GetAsync(string id, BuildEntry build, CancellationToken cancellationToken)
    {
        if (!options.PackageIds.Contains(id, StringComparer.OrdinalIgnoreCase))
            throw new InvalidDataException("Package is not enabled.");
        var directory = Path.Combine(options.CacheDirectory, "packages", build.Version);
        var path = Path.Combine(directory, $"{id.ToLowerInvariant()}.{build.Version}.nupkg");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(path)) return path;
            var archivePath = await artifacts.GetAsync(build.Packages, cancellationToken);
            var staging = Path.Combine(options.CacheDirectory, "staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    foreach (var entry in archive.Entries.Where(x => x.FullName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Artifact paths never become local filenames.
                        var original = Path.Combine(staging, Guid.NewGuid().ToString("N") + ".original");
                        entry.ExtractToFile(original);
                        using (var reader = new PackageArchiveReader(original))
                        {
                            var identity = reader.GetIdentity();
                            if (options.PackageIds.Contains(identity.Id, StringComparer.OrdinalIgnoreCase)
                                && identity.Version == NuGetVersion.Parse(build.OriginalVersion))
                            {
                                if (reader.NuspecReader.GetDependencyGroups().SelectMany(x => x.Packages)
                                    .Any(x => options.PackageIds.Contains(x.Id, StringComparer.OrdinalIgnoreCase)))
                                    throw new InvalidDataException("Packages depending on another configured package require coordinated dependency rewriting.");
                                if (!originals.TryAdd(identity.Id, original))
                                    throw new InvalidDataException($"Duplicate {identity.Id} in artifact.");
                            }
                        }
                        if (!originals.ContainsValue(original)) File.Delete(original);
                    }
                    foreach (var packageId in options.PackageIds)
                    {
                        if (!originals.TryGetValue(packageId, out var original))
                            throw new InvalidDataException($"Expected one {packageId} {build.OriginalVersion} in artifact.");
                        PackageRewriter.Rewrite(original, Path.Combine(staging, $"{packageId.ToLowerInvariant()}.{build.Version}.nupkg"),
                            packageId, build.OriginalVersion, build.Version);
                        File.Delete(original);
                    }
                }
                if (Directory.Exists(directory)) throw new InvalidDataException("Incomplete package cache. Remove the affected version directory.");
                Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
                Directory.Move(staging, directory);
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
            return path;
        }
        finally
        {
            _gate.Release();
        }
    }
}
