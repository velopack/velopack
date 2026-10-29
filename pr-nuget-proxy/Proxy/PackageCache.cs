using System.IO.Compression;
using System.Text.Json;
using NuGet.Packaging;

namespace PrNuGetProxy;

public sealed record CachedPackage(string Id, string OriginalVersion, string Version);

public sealed class PackageCache(ProxyOptions options, ArtifactCache artifacts)
{
    private readonly SemaphoreSlim _gate = new(1);

    private string DirectoryFor(BuildEntry build) => Path.Combine(options.CacheDirectory, "packages", "discovered",
        $"{build.PullRequest}.{build.RunId}.{build.Attempt}.{build.Packages.Id}");

    public async Task<string> GetAsync(string id, BuildEntry build, CancellationToken cancellationToken)
    {
        var packages = await GetPackagesAsync(build, cancellationToken);
        var package = packages.SingleOrDefault(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidDataException($"Package {id} is not present in artifact {build.Packages.Id}.");
        return Path.Combine(DirectoryFor(build), $"{package.Id.ToLowerInvariant()}.{package.Version}.nupkg");
    }

    public async Task<CachedPackage[]> GetPackagesAsync(BuildEntry build, CancellationToken cancellationToken)
    {
        var directory = DirectoryFor(build);
        var index = Path.Combine(directory, "packages.json");
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (File.Exists(index))
                return JsonSerializer.Deserialize<CachedPackage[]>(await File.ReadAllTextAsync(index, cancellationToken), JsonSettings.Options)
                    ?? throw new InvalidDataException("Invalid cached package inventory.");
            var archivePath = await artifacts.GetAsync(build.Packages, cancellationToken);
            var staging = Path.Combine(options.CacheDirectory, "staging-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(staging);
            try
            {
                var originals = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                var packages = new List<CachedPackage>();
                using (var archive = ZipFile.OpenRead(archivePath))
                {
                    foreach (var entry in archive.Entries.Where(x => x.FullName.EndsWith(".nupkg", StringComparison.OrdinalIgnoreCase)))
                    {
                        // Artifact paths never become local filenames.
                        var original = Path.Combine(staging, Guid.NewGuid().ToString("N") + ".original");
                        entry.ExtractToFile(original);
                        using var reader = new PackageArchiveReader(original);
                        var identity = reader.GetIdentity();
                        if (!PackageIdValidator.IsValidPackageId(identity.Id))
                            throw new InvalidDataException($"Invalid package ID in artifact {build.Packages.Id}.");
                        if (!originals.TryAdd(identity.Id, original))
                            throw new InvalidDataException($"Duplicate {identity.Id} in artifact {build.Packages.Id}.");
                        var originalVersion = identity.Version.ToNormalizedString();
                        packages.Add(new CachedPackage(identity.Id, originalVersion,
                            PackageRewriter.VersionFor(originalVersion, build.PullRequest, build.RunId, build.Attempt, build.Packages.Id)));
                    }
                }
                if (packages.Count == 0)
                    throw new InvalidDataException($"Artifact {build.Packages.Id} contains no NuGet packages.");
                var versions = packages.ToDictionary(x => x.Id, x => x.Version, StringComparer.OrdinalIgnoreCase);
                foreach (var package in packages)
                {
                    var original = originals[package.Id];
                    PackageRewriter.Rewrite(original, Path.Combine(staging, $"{package.Id.ToLowerInvariant()}.{package.Version}.nupkg"),
                        package.Id, package.OriginalVersion, package.Version, versions);
                    File.Delete(original);
                }
                await JsonSettings.WriteAtomicAsync(Path.Combine(staging, "packages.json"), packages, cancellationToken);
                if (Directory.Exists(directory)) throw new InvalidDataException("Incomplete package cache. Remove the affected artifact directory.");
                Directory.CreateDirectory(Path.GetDirectoryName(directory)!);
                Directory.Move(staging, directory);
                return packages.ToArray();
            }
            finally
            {
                if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
