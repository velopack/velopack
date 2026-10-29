using System.IO.Compression;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Caching.Memory;
using NuGet.Versioning;

namespace PrNuGetProxy;

public sealed record BuildEntry(
    long RunId, int Attempt, int PullRequest, string HeadSha, string OriginalVersion, string Version,
    Artifact Packages, Artifact VersionArtifact);
public sealed record CatalogFile(string Repository, List<BuildEntry> Builds);
public sealed record PublishedPackage(string Id, BuildEntry Build);
public sealed record RunArtifacts(
    long RunId, int Attempt, int PullRequest, string HeadSha, Artifact Packages, Artifact VersionArtifact);
public sealed record RunArtifactsFile(string Repository, string Workflow, List<RunArtifacts> Runs);

public sealed class PackageCatalog
{
    private readonly ProxyOptions _options;
    private readonly IGitHubClient _github;
    private readonly ILogger<PackageCatalog> _logger;
    private readonly IMemoryCache _memoryCache;
    private readonly ArtifactCache _artifacts;
    private readonly PackageCache _packages;
    private readonly object _cacheKey = new();
    private readonly SemaphoreSlim _gate = new(1);
    private readonly string _file;
    private readonly string _runsFile;
    private List<RunArtifacts> _runs;
    private List<BuildEntry> _builds;

    public PackageCatalog(ProxyOptions options, IGitHubClient github, IMemoryCache memoryCache, ArtifactCache artifacts, PackageCache packages,
        ILogger<PackageCatalog>? logger = null)
    {
        _options = options;
        _github = github;
        _logger = logger ?? NullLogger<PackageCatalog>.Instance;
        _memoryCache = memoryCache;
        _artifacts = artifacts;
        _packages = packages;
        _file = Path.Combine(options.CacheDirectory, "catalog.json");
        Directory.CreateDirectory(options.CacheDirectory);
        var saved = File.Exists(_file)
            ? JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(_file), JsonSettings.Options)
                ?? throw new InvalidDataException("Invalid saved catalog.")
            : null;
        if (saved is not null && saved.Repository != options.Repository)
            throw new InvalidDataException("This cache belongs to another repository. Use a different CacheDirectory.");
        _builds = saved?.Builds ?? [];
        _runsFile = Path.Combine(options.CacheDirectory, "runs.json");
        var savedRuns = File.Exists(_runsFile)
            ? JsonSerializer.Deserialize<RunArtifactsFile>(File.ReadAllText(_runsFile), JsonSettings.Options)
                ?? throw new InvalidDataException("Invalid saved run metadata.")
            : null;
        if (savedRuns is not null && (savedRuns.Repository != options.Repository || savedRuns.Workflow != options.Workflow))
            throw new InvalidDataException("This run cache belongs to another repository/workflow. Use a different CacheDirectory.");
        _runs = savedRuns?.Runs ?? _builds
            .Where(x => x.VersionArtifact is not null)
            .Select(x => new RunArtifacts(x.RunId, x.Attempt, x.PullRequest, x.HeadSha, x.Packages, x.VersionArtifact))
            .ToList();
    }

    public async Task<PublishedPackage[]> GetPackagesAsync(CancellationToken cancellationToken)
    {
        var result = new List<PublishedPackage>();
        foreach (var build in await GetBuildsAsync(cancellationToken))
        {
            foreach (var package in await _packages.GetPackagesAsync(build, cancellationToken))
                result.Add(new PublishedPackage(package.Id, build with
                {
                    OriginalVersion = package.OriginalVersion,
                    Version = package.Version
                }));
        }
        return result.OrderBy(x => NuGetVersion.Parse(x.Build.Version), VersionComparer.VersionRelease).ToArray();
    }

    public async Task<BuildEntry[]> GetBuildsAsync(CancellationToken cancellationToken)
    {
        var runs = await GetRunArtifactsAsync(cancellationToken);
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var next = new List<BuildEntry>(_builds);
            foreach (var run in runs)
            {
                if (next.Any(x => x.RunId == run.RunId && x.Attempt == run.Attempt && x.PullRequest == run.PullRequest)) continue;
                var original = await ReadVersionAsync(run.VersionArtifact, cancellationToken);
                next.Add(new BuildEntry(run.RunId, run.Attempt, run.PullRequest, run.HeadSha, original,
                    PackageRewriter.VersionFor(original, run.PullRequest, run.RunId, run.Attempt, run.Packages.Id),
                    run.Packages, run.VersionArtifact));
            }
            if (next.Count != _builds.Count)
            {
                await JsonSettings.WriteAtomicAsync(_file, new CatalogFile(_options.Repository, next), cancellationToken);
                _builds = next;
            }
            var visible = runs.Select(x => (x.RunId, x.Attempt, x.PullRequest)).ToHashSet();
            return _builds.Where(x => visible.Contains((x.RunId, x.Attempt, x.PullRequest)))
                .OrderBy(x => NuGetVersion.Parse(x.Version), VersionComparer.VersionRelease).ToArray();
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<RunArtifacts[]> GetRunArtifactsAsync(CancellationToken cancellationToken)
    {
        if (_memoryCache.TryGetValue(_cacheKey, out RunArtifacts[]? cached) && cached is not null) return cached;
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (_memoryCache.TryGetValue(_cacheKey, out cached) && cached is not null) return cached;
            var openPullRequests = (await _github.GetOpenPullRequestsAsync(cancellationToken)).Select(x => x.Number).ToHashSet();
            var discovered = openPullRequests.Count == 0 ? [] : await _github.GetRunsAsync(cancellationToken);
            var eligibleRuns = discovered
                .Where(x => x.Status == "completed" && x.Conclusion == "success" && x.Event == "pull_request")
                .SelectMany(run => run.PullRequests.Where(pr => openPullRequests.Contains(pr.Number))
                    .Where(pr => _options.PullRequests.Length == 0 || _options.PullRequests.Contains(pr.Number) || _options.RunIds.Contains(run.Id))
                    .Select(pr => (Run: run, PullRequest: pr.Number)))
                .ToArray();
            var runs = eligibleRuns.GroupBy(x => x.PullRequest)
                .SelectMany(g => g.OrderByDescending(x => x.Run.Id).Take(1).Concat(g.Where(x => _options.RunIds.Contains(x.Run.Id))))
                .DistinctBy(x => (x.Run.Id, x.Run.Attempt, x.PullRequest));
            // Keep immutable completed-run and artifact metadata on disk even while a PR is closed.
            // Open-PR membership is volatile and is applied only to the response below.
            var next = new List<RunArtifacts>(_runs);
            foreach (var (run, pr) in runs)
            {
                // Completed immutable builds already known locally need no artifact API/download request.
                if (next.Any(x => x.RunId == run.Id && x.Attempt == run.Attempt && x.PullRequest == pr)) continue;
                var artifacts = await _github.GetArtifactsAsync(run.Id, cancellationToken);
                var packages = artifacts.SingleOrDefault(x => x.Name == _options.PackagesArtifact);
                var version = artifacts.SingleOrDefault(x => x.Name == _options.VersionArtifact);
                if (packages is null || version is null)
                {
                    if (_options.RunIds.Contains(run.Id))
                        throw new InvalidDataException($"Run {run.Id} has missing package/version artifacts.");
                    _logger.LogWarning("Skipping run {RunId}: missing package/version artifacts.", run.Id);
                    continue;
                }
                if ((packages.Expired && !_artifacts.Contains(packages)) || (version.Expired && !_artifacts.Contains(version)))
                {
                    if (_options.RunIds.Contains(run.Id))
                        throw new InvalidDataException($"Run {run.Id} has expired artifacts that are not cached locally.");
                    _logger.LogWarning("Skipping run {RunId}: expired artifacts are not cached locally.", run.Id);
                    continue;
                }
                next.Add(new RunArtifacts(run.Id, run.Attempt, pr, run.HeadSha, packages, version));
            }
            foreach (var id in _options.RunIds)
            {
                if (discovered.Any(x => x.Id == id && x.PullRequests.Any(pr => openPullRequests.Contains(pr.Number)))
                    && !next.Any(x => x.RunId == id))
                    throw new InvalidDataException($"Run {id} is not an available successful build of an open pull request.");
            }
            await JsonSettings.WriteAtomicAsync(_runsFile, new RunArtifactsFile(_options.Repository, _options.Workflow, next), cancellationToken);
            _runs = next;
            var result = next.Where(x => openPullRequests.Contains(x.PullRequest))
                .Where(x => _options.PullRequests.Length == 0 || _options.PullRequests.Contains(x.PullRequest)
                    || _options.RunIds.Contains(x.RunId))
                .OrderBy(x => x.RunId).ThenBy(x => x.Attempt).ToArray();
            return _memoryCache.Set(_cacheKey, result, TimeSpan.FromSeconds(_options.RefreshSeconds));
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<string> ReadVersionAsync(Artifact artifact, CancellationToken cancellationToken)
    {
        var path = await _artifacts.GetAsync(artifact, cancellationToken);
        using var archive = ZipFile.OpenRead(path);
        var entry = archive.Entries.Single(x => x.FullName == "version.txt");
        if (entry.Length > 4096) throw new InvalidDataException("Version file is unexpectedly large.");
        using var reader = new StreamReader(entry.Open());
        var value = (await reader.ReadToEndAsync(cancellationToken)).Trim();
        return NuGetVersion.Parse(value).ToNormalizedString();
    }
}
