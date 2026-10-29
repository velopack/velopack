using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using NuGet.Packaging;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Versioning;
using PrNuGetProxy;
using Xunit;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace PrNuGetProxy.Tests;

public sealed class ProxyTests : IDisposable
{
    private static CancellationToken Cancellation => TestContext.Current.CancellationToken;
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "pr-nuget-tests-" + Guid.NewGuid().ToString("N"));
    private readonly FakeGitHub _github;
    private readonly ProxyOptions _options;
    private readonly MemoryCache _memoryCache = new(new MemoryCacheOptions());
    private readonly ArtifactCache _artifacts;
    private PackageCatalog CreateCatalog() => new(_options, _github, _memoryCache, _artifacts, CreatePackageCache());
    private PackageCache CreatePackageCache() => new(_options, _artifacts);

    public ProxyTests()
    {
        Directory.CreateDirectory(_directory);
        _github = new FakeGitHub(_directory);
        _options = new ProxyOptions
        {
            CacheDirectory = Path.Combine(_directory, "cache"),
            PullRequests = [755]
        };
        _artifacts = new ArtifactCache(_options, _github);
    }

    [Fact]
    public void VersionsAreDistinctNormalizedPrereleases()
    {
        var first = PackageRewriter.VersionFor("1.3.67-gabcdef0+metadata", 755, 100, 1, 200);
        Assert.Equal("1.3.67-pr.755.run.100.attempt.1.artifact.200", first);
        Assert.Equal(first, NuGetVersion.Parse(first).ToNormalizedString());
        Assert.NotEqual(first, PackageRewriter.VersionFor("1.3.67-gabcdef0", 755, 100, 2, 201));
        Assert.NotEqual(first, PackageRewriter.VersionFor("1.3.67-gabcdef0", 756, 101, 1, 202));
    }

    [Fact]
    public void RewriterPreservesPayloadAndDependencies()
    {
        var source = Path.Combine(_directory, "original.nupkg");
        var destination = Path.Combine(_directory, "rewritten.nupkg");
        FakeGitHub.WritePackage(source, "Velopack");
        PackageRewriter.Rewrite(source, destination, "Velopack", FakeGitHub.OriginalVersion, "1.3.67-pr.755");
        using var reader = new PackageArchiveReader(destination);
        Assert.Equal("Velopack", reader.GetIdentity().Id);
        Assert.Equal("1.3.67-pr.755", reader.GetIdentity().Version.ToNormalizedString());
        Assert.Equal("Newtonsoft.Json", Assert.Single(Assert.Single(reader.NuspecReader.GetDependencyGroups()).Packages).Id);
        using var original = ZipFile.OpenRead(source);
        using var rewritten = ZipFile.OpenRead(destination);
        foreach (var entry in original.Entries.Where(x => !x.FullName.EndsWith(".nuspec")))
        {
            var result = rewritten.GetEntry(entry.FullName)!;
            Assert.Equal(entry.ExternalAttributes, result.ExternalAttributes);
            Assert.Equal(entry.LastWriteTime, result.LastWriteTime);
            using var input = entry.Open();
            using var output = result.Open();
            Assert.Equal(SHA256.HashData(input), SHA256.HashData(output));
        }
    }

    [Fact]
    public void SignedAndMismatchedPackagesAreRejected()
    {
        var source = Path.Combine(_directory, "signed.nupkg");
        FakeGitHub.WritePackage(source, "Velopack");
        Assert.Throws<InvalidDataException>(() => PackageRewriter.Rewrite(source, source + ".wrong", "Other",
            FakeGitHub.OriginalVersion, "1.0.0-test"));
        using (var zip = ZipFile.Open(source, ZipArchiveMode.Update)) zip.CreateEntry(".signature.p7s");
        Assert.Throws<InvalidDataException>(() => PackageRewriter.Rewrite(source, source + ".out", "Velopack",
            FakeGitHub.OriginalVersion, "1.0.0-test"));
    }

    [Fact]
    public async Task MetadataWarmupPersistsArtifactListingsWithoutDownloadingContents()
    {
        var catalog = CreateCatalog();
        _github.FailDownloads = true;
        var run = Assert.Single(await catalog.GetRunArtifactsAsync(Cancellation));
        Assert.Equal(100, run.RunId);
        Assert.Equal(1, _github.OpenPullRequestQueries);
        Assert.Equal(1, _github.RunQueries);
        Assert.Equal(1, _github.ArtifactQueries);
        Assert.Equal(0, _github.VersionDownloads);
        Assert.Equal(0, _github.PackageDownloads);
        Assert.False(Directory.Exists(Path.Combine(_options.CacheDirectory, "artifacts")));
        Assert.False(File.Exists(Path.Combine(_options.CacheDirectory, "catalog.json")));
        var saved = JsonSerializer.Deserialize<RunArtifactsFile>(
            File.ReadAllText(Path.Combine(_options.CacheDirectory, "runs.json")), JsonSettings.Options)!;
        Assert.Equal(run, Assert.Single(saved.Runs));

        Assert.Equal(run, Assert.Single(await CreateCatalog().GetRunArtifactsAsync(Cancellation)));
        Assert.Equal(2, _github.OpenPullRequestQueries);
        Assert.Equal(1, _github.ArtifactQueries);
        _github.FailDownloads = false;
        Assert.Single(await catalog.GetBuildsAsync(Cancellation));
        Assert.Equal(2, _github.OpenPullRequestQueries);
        Assert.Equal(1, _github.VersionDownloads);
        Assert.Equal(0, _github.PackageDownloads);
    }

    [Fact]
    public async Task CatalogFiltersRunsAndPersistsImmutableIdentities()
    {
        var catalog = CreateCatalog();
        var builds = await catalog.GetBuildsAsync(Cancellation);
        var build = Assert.Single(builds);
        Assert.Equal(100, build.RunId);
        Assert.Equal(755, build.PullRequest);
        Assert.Equal(FakeGitHub.OriginalVersion, build.OriginalVersion);
        Assert.Equal(1, _github.VersionDownloads);
        await catalog.GetBuildsAsync(Cancellation);
        Assert.Equal(1, _github.VersionDownloads);
        var restored = await CreateCatalog().GetBuildsAsync(Cancellation);
        Assert.Equal(builds, restored);
        Assert.Equal(1, _github.VersionDownloads);
    }

    [Fact]
    public async Task PackageCacheDownloadsOnceAndReusesLocalFiles()
    {
        var build = Assert.Single(await CreateCatalog().GetBuildsAsync(Cancellation));
        var cache = CreatePackageCache();
        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => cache.GetAsync("Velopack", build, Cancellation)));
        Assert.All(results, x => Assert.Equal(results[0], x));
        Assert.Equal(1, _github.PackageDownloads);
        var tool = await cache.GetAsync("vpk", build, Cancellation);
        using var reader = new PackageArchiveReader(tool);
        Assert.Equal(build.Version, reader.GetIdentity().Version.ToNormalizedString());
        _github.FailDownloads = true;
        Assert.Equal(results[0], await CreatePackageCache().GetAsync("Velopack", build, Cancellation));
    }

    [Fact]
    public async Task CorruptArtifactNeverPublishesPartialPackages()
    {
        var build = Assert.Single(await CreateCatalog().GetBuildsAsync(Cancellation));
        var cache = CreatePackageCache();
        _github.CorruptDownload = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetAsync("vpk", build, Cancellation));
        Assert.False(Directory.Exists(Path.Combine(_options.CacheDirectory, "packages", "discovered")));
        Assert.Empty(Directory.GetDirectories(_options.CacheDirectory, "staging-*"));
        _github.CorruptDownload = false;
        Assert.True(File.Exists(await cache.GetAsync("vpk", build, Cancellation)));
    }

    [Fact]
    public async Task PackageInventoryIsDiscoveredAndPersistedWithoutConfiguration()
    {
        var catalog = CreateCatalog();
        var packages = await catalog.GetPackagesAsync(Cancellation);
        Assert.Equal(["Example.Plugin", "Velopack", "Velopack.Build", "vpk"], packages.Select(x => x.Id).Order().ToArray());
        var plugin = Assert.Single(packages, x => x.Id == "Example.Plugin");
        Assert.StartsWith("2.0.0-pr.", plugin.Build.Version);
        var cache = CreatePackageCache();
        using var reader = new PackageArchiveReader(await cache.GetAsync(plugin.Id, plugin.Build, Cancellation));
        Assert.Equal(plugin.Build.Version, reader.GetIdentity().Version.ToNormalizedString());
        var dependency = Assert.Single(Assert.Single(reader.NuspecReader.GetDependencyGroups()).Packages);
        Assert.Equal("Velopack", dependency.Id);
        var library = Assert.Single(packages, x => x.Id == "Velopack");
        Assert.Equal(VersionRange.Parse($"[{library.Build.Version}]").ToNormalizedString(), dependency.VersionRange.ToNormalizedString());
        _github.FailDownloads = true;
        var restored = await CreateCatalog().GetPackagesAsync(Cancellation);
        Assert.Equal(packages, restored);
        await Assert.ThrowsAsync<InvalidDataException>(() => cache.GetAsync("missing", plugin.Build, Cancellation));
        Assert.Equal(1, _github.PackageDownloads);
    }

    [Fact]
    public async Task ExistingAllowlistCatalogAndPartialPackageCacheAreReusable()
    {
        var build = Assert.Single(await CreateCatalog().GetBuildsAsync(Cancellation));
        await JsonSettings.WriteAtomicAsync(Path.Combine(_options.CacheDirectory, "catalog.json"),
            new { _options.Repository, PackageIds = new[] { "Velopack", "vpk" }, Builds = new[] { build } }, Cancellation);
        var oldDirectory = Path.Combine(_options.CacheDirectory, "packages", build.Version);
        Directory.CreateDirectory(oldDirectory);
        File.WriteAllText(Path.Combine(oldDirectory, $"velopack.{build.Version}.nupkg"), "old cached package");
        var packages = await CreateCatalog().GetPackagesAsync(Cancellation);
        Assert.Contains(packages, x => x.Id == "Velopack.Build");
        Assert.Contains(packages, x => x.Id == "Example.Plugin");
    }

    [Fact]
    public async Task PinnedExpiredRunFailsExplicitly()
    {
        _options.RunIds = [100];
        _github.Expired = true;
        await Assert.ThrowsAsync<InvalidDataException>(() => CreateCatalog().GetBuildsAsync(Cancellation));
    }

    [Fact]
    public async Task DiscoveryErrorsDoNotBecomeEmptySuccessfulFeeds()
    {
        _github.FailDiscovery = true;
        await Assert.ThrowsAsync<IOException>(() => CreateCatalog().GetBuildsAsync(Cancellation));
    }

    [Fact]
    public async Task MemoryCacheGatesGitHubQueriesAndRefreshesAfterEviction()
    {
        var catalog = CreateCatalog();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(_ => catalog.GetBuildsAsync(Cancellation)));
        Assert.Equal(1, _github.OpenPullRequestQueries);
        Assert.Equal(1, _github.RunQueries);
        Assert.Equal(1, _github.ArtifactQueries);
        _memoryCache.Compact(1);
        await catalog.GetBuildsAsync(Cancellation);
        Assert.Equal(2, _github.OpenPullRequestQueries);
        Assert.Equal(2, _github.RunQueries);
        Assert.Equal(1, _github.ArtifactQueries);
        Assert.Equal(1, _github.VersionDownloads);
    }

    [Fact]
    public async Task ClosedPullRequestsDisappearEvenWhenPinnedAndCached()
    {
        _options.RunIds = [100];
        var catalog = CreateCatalog();
        var build = Assert.Single(await catalog.GetBuildsAsync(Cancellation));
        Assert.Equal(1, _github.ArtifactQueries);
        var cachedPackage = await CreatePackageCache().GetAsync("Velopack", build, Cancellation);
        _github.OpenPullRequests = [new(756)];
        _memoryCache.Compact(1);
        Assert.Empty(await catalog.GetBuildsAsync(Cancellation));
        Assert.Empty(await CreateCatalog().GetBuildsAsync(Cancellation));
        var saved = JsonSerializer.Deserialize<CatalogFile>(File.ReadAllText(Path.Combine(_options.CacheDirectory, "catalog.json")),
            JsonSettings.Options)!;
        var cachedBuild = Assert.Single(saved.Builds);
        Assert.Equal(build.VersionArtifact.Id, cachedBuild.VersionArtifact.Id);
        Assert.Equal(build.Packages.Id, cachedBuild.Packages.Id);
        Assert.True(File.Exists(cachedPackage));

        _github.OpenPullRequests = [new(755), new(756)];
        _memoryCache.Compact(1);
        Assert.Equal(build.Version, Assert.Single(await catalog.GetBuildsAsync(Cancellation)).Version);
        Assert.Equal(1, _github.ArtifactQueries);
        Assert.Equal(1, _github.VersionDownloads);
    }

    [Fact]
    public async Task OnlyCompletedSuccessfulRunsOfOpenPullRequestsAreDiscovered()
    {
        _options.PullRequests = [];
        _github.OpenPullRequests = [new(755)];
        _github.Runs = [
            new(100, 1, "good", "completed", "success", "pull_request", [new(755)]),
            new(101, 1, "running", "in_progress", null, "pull_request", [new(755)]),
            new(102, 1, "queued", "queued", null, "pull_request", [new(755)]),
            new(103, 1, "failed", "completed", "failure", "pull_request", [new(755)]),
            new(104, 1, "closed", "completed", "success", "pull_request", [new(756)]),
            new(105, 1, "push", "completed", "success", "push", [new(755)])
        ];
        var build = Assert.Single(await CreateCatalog().GetBuildsAsync(Cancellation));
        Assert.Equal(100, build.RunId);
        Assert.Equal(1, _github.ArtifactQueries);
    }

    [Fact]
    public async Task NoOpenPullRequestsMeansNoRunOrArtifactQueries()
    {
        _github.OpenPullRequests = [];
        Assert.Empty(await CreateCatalog().GetBuildsAsync(Cancellation));
        Assert.Equal(0, _github.RunQueries);
        Assert.Equal(0, _github.ArtifactQueries);
        Assert.Equal(0, _github.VersionDownloads);
    }

    [Fact]
    public async Task RawArtifactsAreReusedAfterCatalogOrRewrittenPackagesAreRemoved()
    {
        var build = Assert.Single(await CreateCatalog().GetBuildsAsync(Cancellation));
        var package = await CreatePackageCache().GetAsync("Velopack", build, Cancellation);
        Directory.Delete(Path.GetDirectoryName(package)!, recursive: true);
        File.Delete(Path.Combine(_options.CacheDirectory, "catalog.json"));
        _github.FailDownloads = true;
        var restored = Assert.Single(await CreateCatalog().GetBuildsAsync(Cancellation));
        Assert.Equal(build, restored);
        Assert.True(File.Exists(await CreatePackageCache().GetAsync("Velopack", restored, Cancellation)));
        Assert.Equal(1, _github.PackageDownloads);
        Assert.Equal(1, _github.VersionDownloads);
    }

    [Fact]
    public async Task ExpiredArtifactsCanStillBeUsedFromDisk()
    {
        var build = Assert.Single(await CreateCatalog().GetBuildsAsync(Cancellation));
        await CreatePackageCache().GetAsync("Velopack", build, Cancellation);
        File.Delete(Path.Combine(_options.CacheDirectory, "catalog.json"));
        _github.Expired = true;
        _github.FailDownloads = true;
        var restored = Assert.Single(await CreateCatalog().GetBuildsAsync(Cancellation));
        Assert.Equal(build.Version, restored.Version);
        Assert.Equal(build.Packages.Id, restored.Packages.Id);
    }

    [Fact]
    public async Task FailedDiscoveryIsNotCached()
    {
        var catalog = CreateCatalog();
        _github.FailDiscovery = true;
        await Assert.ThrowsAsync<IOException>(() => catalog.GetBuildsAsync(Cancellation));
        _github.FailDiscovery = false;
        Assert.Single(await catalog.GetBuildsAsync(Cancellation));
        Assert.Equal(2, _github.OpenPullRequestQueries);
    }

    [Fact]
    public async Task CacheCannotBeReusedForAnotherRepository()
    {
        await CreateCatalog().GetBuildsAsync(Cancellation);
        _options.Repository = "other/project";
        Assert.Throws<InvalidDataException>(() => CreateCatalog());
    }

    [Fact]
    public async Task HttpFeedHasConsistentIdentitiesAndDependencies()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        _options.BaseUrl = $"http://localhost:{((IPEndPoint)listener.LocalEndpoint).Port}";
        listener.Stop();
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Urls"] = _options.BaseUrl,
            ["Proxy:BaseUrl"] = _options.BaseUrl,
            ["Proxy:CacheDirectory"] = _options.CacheDirectory,
            ["Proxy:PullRequests:0"] = "755"
        });
        builder.Services.AddProxy(builder.Configuration);
        builder.Services.AddSingleton<IGitHubClient>(_github);
        await using var app = builder.Build();
        app.UseExceptionHandler();
        app.MapProxy();
        await app.StartAsync(Cancellation);
        Assert.Equal(1, _github.OpenPullRequestQueries);
        Assert.Equal(1, _github.RunQueries);
        Assert.Equal(1, _github.ArtifactQueries);
        Assert.Equal(0, _github.VersionDownloads);
        Assert.Equal(0, _github.PackageDownloads);
        using var client = new HttpClient { BaseAddress = new Uri(_options.BaseUrl) };
        using var index = JsonDocument.Parse(await client.GetStringAsync("/v3/index.json", Cancellation));
        Assert.Equal("PackageBaseAddress/3.0.0", index.RootElement.GetProperty("resources")[0].GetProperty("@type").GetString());
        using var versions = JsonDocument.Parse(await client.GetStringAsync("/flat/velopack/index.json", Cancellation));
        var version = Assert.Single(versions.RootElement.GetProperty("versions").EnumerateArray()).GetString();
        var package = await client.GetByteArrayAsync($"/flat/velopack/{version}/velopack.{version}.nupkg", Cancellation);
        using var reader = new PackageArchiveReader(new MemoryStream(package));
        Assert.Equal(version, reader.GetIdentity().Version.ToNormalizedString());
        using var buildVersions = JsonDocument.Parse(await client.GetStringAsync("/flat/velopack.build/index.json", Cancellation));
        Assert.Equal(version, Assert.Single(buildVersions.RootElement.GetProperty("versions").EnumerateArray()).GetString());
        var buildPackage = await client.GetByteArrayAsync(
            $"/flat/velopack.build/{version}/velopack.build.{version}.nupkg", Cancellation);
        using var buildReader = new PackageArchiveReader(new MemoryStream(buildPackage));
        Assert.Equal("Velopack.Build", buildReader.GetIdentity().Id);
        Assert.Equal(version, buildReader.GetIdentity().Version.ToNormalizedString());
        using var pluginVersions = JsonDocument.Parse(await client.GetStringAsync("/flat/example.plugin/index.json", Cancellation));
        var pluginVersion = Assert.Single(pluginVersions.RootElement.GetProperty("versions").EnumerateArray()).GetString();
        Assert.StartsWith("2.0.0-pr.", pluginVersion);
        using var pluginRegistration = JsonDocument.Parse(await client.GetStringAsync("/registration/example.plugin/index.json", Cancellation));
        var pluginEntry = pluginRegistration.RootElement.GetProperty("items")[0].GetProperty("items")[0].GetProperty("catalogEntry");
        Assert.Equal(pluginVersion, pluginEntry.GetProperty("version").GetString());
        Assert.Equal(VersionRange.Parse($"[{version}]").ToNormalizedString(),
            pluginEntry.GetProperty("dependencyGroups")[0].GetProperty("dependencies")[0].GetProperty("range").GetString());
        using var registration = JsonDocument.Parse(await client.GetStringAsync("/registration/velopack/index.json", Cancellation));
        var entry = registration.RootElement.GetProperty("items")[0].GetProperty("items")[0].GetProperty("catalogEntry");
        Assert.Equal(version, entry.GetProperty("version").GetString());
        Assert.Equal("Newtonsoft.Json", entry.GetProperty("dependencyGroups")[0].GetProperty("dependencies")[0].GetProperty("id").GetString());
        var nuspec = await client.GetByteArrayAsync($"/flat/velopack/{version}/velopack.nuspec", Cancellation);
        Assert.Equal(PackageRewriter.ReadManifest(await CreatePackageCache().GetAsync("velopack",
            Assert.Single(await CreateCatalog().GetBuildsAsync(Cancellation)), Cancellation)), nuspec);
        using var search = JsonDocument.Parse(await client.GetStringAsync("/query?q=velopack&prerelease=true&semVerLevel=2.0.0", Cancellation));
        Assert.Equal(2, search.RootElement.GetProperty("totalHits").GetInt32());
        Assert.Equal(version, search.RootElement.GetProperty("data")[0].GetProperty("version").GetString());
        using var stable = JsonDocument.Parse(await client.GetStringAsync("/query?prerelease=false", Cancellation));
        Assert.Equal(0, stable.RootElement.GetProperty("totalHits").GetInt32());
        using var tools = JsonDocument.Parse(await client.GetStringAsync(
            "/query?prerelease=true&semVerLevel=2.0.0&packageType=DotnetTool", Cancellation));
        Assert.Equal("vpk", Assert.Single(tools.RootElement.GetProperty("data").EnumerateArray()).GetProperty("id").GetString());
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head,
            $"/flat/vpk/{version}/vpk.{version}.nupkg"), Cancellation);
        Assert.Equal(HttpStatusCode.OK, head.StatusCode);
        Assert.Empty(await head.Content.ReadAsByteArrayAsync(Cancellation));
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/flat/other/index.json", Cancellation)).StatusCode);
        var source = Repository.Factory.GetCoreV3(new PackageSource(_options.BaseUrl + "/v3/index.json")
        {
            AllowInsecureConnections = true
        });
        var metadataResource = await source.GetResourceAsync<PackageMetadataResource>(Cancellation);
        Assert.NotNull(metadataResource);
        using var context = new SourceCacheContext { NoCache = true };
        var metadata = Assert.Single(await metadataResource.GetMetadataAsync("Velopack", true, false, context,
            NullLogger.Instance, Cancellation));
        Assert.Equal(version, metadata.Identity.Version.ToNormalizedString());
        Assert.Equal("Newtonsoft.Json", Assert.Single(Assert.Single(metadata.DependencySets).Packages).Id);
        var searchResource = await source.GetResourceAsync<PackageSearchResource>(Cancellation);
        Assert.NotNull(searchResource);
        var foundPackages = (await searchResource.SearchAsync("Velopack", new SearchFilter(includePrerelease: true),
            0, 20, NullLogger.Instance, Cancellation)).ToArray();
        Assert.Equal(2, foundPackages.Length);
        var found = Assert.Single(foundPackages, x => x.Identity.Id == "Velopack.Build");
        Assert.Equal(version, found.Identity.Version.ToNormalizedString());
        var autocomplete = await source.GetResourceAsync<AutoCompleteResource>(Cancellation);
        Assert.NotNull(autocomplete);
        Assert.Equal(["Velopack", "Velopack.Build"], await autocomplete.IdStartsWith("Velo", true, NullLogger.Instance, Cancellation));
        app.Services.GetRequiredService<ProxyOptions>().RunIds = [99];
        Assert.IsType<MemoryCache>(app.Services.GetRequiredService<IMemoryCache>()).Compact(1);
        using var allLibraryVersions = JsonDocument.Parse(await client.GetStringAsync("/flat/velopack/index.json", Cancellation));
        Assert.Equal(2, allLibraryVersions.RootElement.GetProperty("versions").GetArrayLength());
        using var allPluginVersions = JsonDocument.Parse(await client.GetStringAsync("/flat/example.plugin/index.json", Cancellation));
        Assert.Single(allPluginVersions.RootElement.GetProperty("versions").EnumerateArray());
        using var pluginSearch = JsonDocument.Parse(await client.GetStringAsync(
            "/query?q=Example.Plugin&prerelease=true&semVerLevel=2.0.0", Cancellation));
        Assert.Single(pluginSearch.RootElement.GetProperty("data")[0].GetProperty("versions").EnumerateArray());
        using var pluginAutocomplete = JsonDocument.Parse(await client.GetStringAsync(
            "/autocomplete?id=Example.Plugin&prerelease=true&semVerLevel=2.0.0", Cancellation));
        Assert.Single(pluginAutocomplete.RootElement.GetProperty("data").EnumerateArray());
        _github.OpenPullRequests = [];
        Assert.IsType<MemoryCache>(app.Services.GetRequiredService<IMemoryCache>()).Compact(1);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/flat/velopack/index.json", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/flat/velopack/{version}/velopack.{version}.nupkg", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync("/registration/velopack/index.json", Cancellation)).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound,
            (await client.GetAsync($"/registration/velopack/{version}.json", Cancellation)).StatusCode);
        using var closedSearch = JsonDocument.Parse(await client.GetStringAsync("/query?prerelease=true&semVerLevel=2.0.0", Cancellation));
        Assert.Empty(closedSearch.RootElement.GetProperty("data").EnumerateArray());
        using var closedAutocomplete = JsonDocument.Parse(await client.GetStringAsync(
            "/autocomplete?id=Velopack&prerelease=true&semVerLevel=2.0.0", Cancellation));
        Assert.Empty(closedAutocomplete.RootElement.GetProperty("data").EnumerateArray());
        await app.StopAsync(Cancellation);
    }

    public void Dispose()
    {
        _memoryCache.Dispose();
        if (Directory.Exists(_directory)) Directory.Delete(_directory, recursive: true);
    }

    private sealed class FakeGitHub : IGitHubClient
    {
        public const string OriginalVersion = "1.3.67-gabcdef0";
        private readonly string _versionFile;
        private readonly string _packagesFile;
        private readonly string _olderPackagesFile;
        private readonly Artifact _version;
        private readonly Artifact _packages;
        private readonly Artifact _olderPackages;
        public int VersionDownloads { get; private set; }
        public int PackageDownloads { get; private set; }
        public bool CorruptDownload { get; set; }
        public bool Expired { get; set; }
        public bool FailDiscovery { get; set; }
        public bool FailDownloads { get; set; }
        public int OpenPullRequestQueries { get; private set; }
        public int RunQueries { get; private set; }
        public int ArtifactQueries { get; private set; }
        public PullRequestInfo[] OpenPullRequests { get; set; } = [new(755), new(756)];
        public WorkflowRun[]? Runs { get; set; }

        public FakeGitHub(string directory)
        {
            _versionFile = Path.Combine(directory, "version.zip");
            using (var zip = ZipFile.Open(_versionFile, ZipArchiveMode.Create))
            using (var writer = new StreamWriter(zip.CreateEntry("version.txt").Open()))
                writer.Write(OriginalVersion);
            _packagesFile = Path.Combine(directory, "packages.zip");
            using (var zip = ZipFile.Open(_packagesFile, ZipArchiveMode.Create))
            {
                foreach (var id in new[] { "Velopack", "Velopack.Build", "vpk" })
                {
                    var package = Path.Combine(directory, id + ".nupkg");
                    WritePackage(package, id);
                    zip.CreateEntryFromFile(package, "../../untrusted/" + id + ".nupkg");
                }
                var plugin = Path.Combine(directory, "Example.Plugin.nupkg");
                WritePackage(plugin, "Example.Plugin", "2.0.0", "Velopack");
                zip.CreateEntryFromFile(plugin, "Example.Plugin.nupkg");
            }
            _version = ArtifactFor(1, "build-version", _versionFile);
            _packages = ArtifactFor(2, "packages", _packagesFile);
            _olderPackagesFile = Path.Combine(directory, "older-packages.zip");
            File.Copy(_packagesFile, _olderPackagesFile);
            using (var zip = ZipFile.Open(_olderPackagesFile, ZipArchiveMode.Update))
                zip.GetEntry("Example.Plugin.nupkg")!.Delete();
            _olderPackages = ArtifactFor(3, "packages", _olderPackagesFile);
        }

        private static Artifact ArtifactFor(long id, string name, string file) =>
            new(id, name, false, "sha256:" + Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(file))).ToLowerInvariant());

        public static void WritePackage(string path, string id, string version = OriginalVersion, string dependency = "Newtonsoft.Json")
        {
            using var zip = ZipFile.Open(path, ZipArchiveMode.Create);
            using (var writer = new StreamWriter(zip.CreateEntry(id + ".nuspec").Open()))
            {
                writer.Write($"""
                    <?xml version="1.0" encoding="utf-8"?>
                    <package xmlns="http://schemas.microsoft.com/packaging/2013/05/nuspec.xsd">
                      <metadata><id>{id}</id><version>{version}</version><authors>test</authors>
                        <description>Test package</description>
                        {(id == "vpk" ? "<packageTypes><packageType name=\"DotnetTool\" /></packageTypes>" : "")}
                        <dependencies><group targetFramework="net8.0"><dependency id="{dependency}" version="[13.0.4,)" /></group></dependencies>
                      </metadata>
                    </package>
                    """);
            }
            var payload = zip.CreateEntry("runtimes/linux-x64/native/test");
            payload.ExternalAttributes = 0x1ED << 16;
            using var stream = payload.Open();
            stream.Write(Encoding.UTF8.GetBytes("unchanged-payload"));
        }

        public Task<IReadOnlyList<PullRequestInfo>> GetOpenPullRequestsAsync(CancellationToken cancellationToken)
        {
            OpenPullRequestQueries++;
            if (FailDiscovery) throw new IOException("GitHub unavailable");
            return Task.FromResult<IReadOnlyList<PullRequestInfo>>(OpenPullRequests);
        }

        public Task<IReadOnlyList<WorkflowRun>> GetRunsAsync(CancellationToken cancellationToken)
        {
            RunQueries++;
            if (FailDiscovery) throw new IOException("GitHub unavailable");
            return Task.FromResult<IReadOnlyList<WorkflowRun>>(Runs ?? [
                new(100, 1, "head", "completed", "success", "pull_request", [new(755)]),
                new(99, 1, "old", "completed", "success", "pull_request", [new(755)]),
                new(101, 1, "other", "completed", "success", "pull_request", [new(756)]),
                new(102, 1, "failed", "completed", "failure", "pull_request", [new(755)])
            ]);
        }

        public Task<IReadOnlyList<Artifact>> GetArtifactsAsync(long runId, CancellationToken cancellationToken)
        {
            ArtifactQueries++;
            var packages = runId == 99 ? _olderPackages : _packages;
            return Task.FromResult<IReadOnlyList<Artifact>>([_version with { Expired = Expired }, packages with { Expired = Expired }]);
        }

        public Task DownloadAsync(Artifact artifact, string destination, CancellationToken cancellationToken)
        {
            if (FailDownloads) throw new IOException("Downloads unavailable");
            if (artifact.Id == _version.Id) VersionDownloads++;
            else PackageDownloads++;
            File.Copy(artifact.Id == _version.Id ? _versionFile
                : artifact.Id == _olderPackages.Id ? _olderPackagesFile : _packagesFile, destination);
            if (CorruptDownload && artifact.Id == _packages.Id) File.AppendAllText(destination, "corrupt");
            return Task.CompletedTask;
        }
    }
}
