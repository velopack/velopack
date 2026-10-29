using System.Globalization;
using NuGet.Packaging;
using NuGet.Versioning;

namespace PrNuGetProxy;

public static class FeedEndpoints
{
    public static Dictionary<string, object> Resource(string url, string type) => new() { ["@id"] = url, ["@type"] = type };

    public static void Map(WebApplication app)
    {
        app.MapMethods("/flat/{id}/index.json", ["GET", "HEAD"], async (string id, PackageCatalog catalog, CancellationToken ct) =>
        {
            var builds = BuildsFor(await catalog.GetPackagesAsync(ct), id);
            return builds.Length == 0 ? Results.NotFound() : Results.Json(new { versions = builds.Select(x => x.Version) });
        });
        app.MapMethods("/flat/{id}/{version}/{file}", ["GET", "HEAD"],
            async (string id, string version, string file, PackageCatalog catalog, PackageCache cache, CancellationToken ct) =>
            {
                var build = await FindAsync(catalog, id, version, ct);
                if (build is null) return Results.NotFound();
                var packageFile = $"{id}.{build.Version}.nupkg";
                var manifestFile = $"{id}.nuspec";
                if (!file.Equals(packageFile, StringComparison.OrdinalIgnoreCase)
                    && !file.Equals(manifestFile, StringComparison.OrdinalIgnoreCase)) return Results.NotFound();
                var path = await cache.GetAsync(id, build, ct);
                return file.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase)
                    ? Results.Bytes(PackageRewriter.ReadManifest(path), "application/xml")
                    : Results.File(path, "application/octet-stream", enableRangeProcessing: true);
            });
        app.MapMethods("/registration/{id}/index.json", ["GET", "HEAD"],
            async (string id, ProxyOptions options, PackageCatalog catalog, PackageCache cache, CancellationToken ct) =>
            {
                var builds = BuildsFor(await catalog.GetPackagesAsync(ct), id);
                if (builds.Length == 0) return Results.NotFound();
                var leaves = new List<object>();
                foreach (var build in builds) leaves.Add(await LeafAsync(options, cache, id, build, ct));
                var url = options.BaseUrl + "/registration/" + id.ToLowerInvariant() + "/index.json";
                return Results.Json(new Dictionary<string, object>
                {
                    ["@id"] = url,
                    ["count"] = 1,
                    ["items"] = new[] { new Dictionary<string, object> {
                        ["@id"] = url, ["count"] = leaves.Count, ["lower"] = builds[0].Version,
                        ["upper"] = builds[^1].Version, ["items"] = leaves, ["parent"] = url } }
                });
            });
        app.MapMethods("/registration/{id}/{version}.json", ["GET", "HEAD"],
            async (string id, string version, ProxyOptions options, PackageCatalog catalog, CancellationToken ct) =>
            {
                var build = await FindAsync(catalog, id, version, ct);
                if (build is null) return Results.NotFound();
                id = id.ToLowerInvariant();
                return Results.Json(new Dictionary<string, object>
                {
                    ["@id"] = $"{options.BaseUrl}/registration/{id}/{build.Version}.json",
                    ["listed"] = true,
                    ["packageContent"] = $"{options.BaseUrl}/flat/{id}/{build.Version}/{id}.{build.Version}.nupkg",
                    ["registration"] = $"{options.BaseUrl}/registration/{id}/index.json"
                });
            });
        app.MapMethods("/query", ["GET", "HEAD"], async (HttpRequest request, ProxyOptions options, PackageCatalog catalog,
            PackageCache cache, CancellationToken ct) =>
        {
            var packages = IncludesBuilds(request) ? await catalog.GetPackagesAsync(ct) : [];
            var query = request.Query["q"].ToString();
            var ids = PackageIds(packages).Where(x => x.Contains(query, StringComparison.OrdinalIgnoreCase)).ToArray();
            var types = new Dictionary<string, string>();
            foreach (var id in ids) types[id] = await PackageTypeAsync(cache, id, BuildsFor(packages, id)[^1], ct);
            var type = request.Query["packageType"].ToString();
            if (type.Length > 0) ids = ids.Where(x => types[x].Equals(type, StringComparison.OrdinalIgnoreCase)).ToArray();
            var data = new List<object>();
            foreach (var id in ids.Skip(Number(request, "skip", 0)).Take(Number(request, "take", 20)))
            {
                var builds = BuildsFor(packages, id);
                var latest = builds[^1];
                data.Add(new
                {
                    id,
                    version = latest.Version,
                    description = $"PR builds of {id} from {options.Repository}. Select an immutable PR/run version.",
                    title = id + " PR builds",
                    authors = new[] { options.Repository.Split('/')[0] },
                    totalDownloads = 0,
                    registration = options.BaseUrl + "/registration/" + id.ToLowerInvariant() + "/index.json",
                    versions = builds.Select(x => new Dictionary<string, object>
                    {
                        ["version"] = x.Version,
                        ["downloads"] = 0,
                        ["@id"] = options.BaseUrl + "/registration/" + id.ToLowerInvariant() + "/" + x.Version + ".json"
                    }),
                    packageTypes = new[] { new { name = types[id] } }
                });
            }
            return Results.Json(new { totalHits = ids.Length, data });
        });
        app.MapMethods("/autocomplete", ["GET", "HEAD"], async (HttpRequest request, PackageCatalog catalog,
            PackageCache cache, CancellationToken ct) =>
        {
            var packages = IncludesBuilds(request) ? await catalog.GetPackagesAsync(ct) : [];
            var id = request.Query["id"].ToString();
            var query = request.Query["q"].ToString();
            IEnumerable<string> values = id.Length > 0 ? BuildsFor(packages, id).Select(x => x.Version)
                : PackageIds(packages).Where(x => x.Contains(query, StringComparison.OrdinalIgnoreCase));
            var type = request.Query["packageType"].ToString();
            if (id.Length == 0 && type.Length > 0)
            {
                var filtered = new List<string>();
                foreach (var candidate in values)
                {
                    if ((await PackageTypeAsync(cache, candidate, BuildsFor(packages, candidate)[^1], ct))
                        .Equals(type, StringComparison.OrdinalIgnoreCase))
                        filtered.Add(candidate);
                }
                values = filtered;
            }
            var all = values.ToArray();
            return Results.Json(new
            {
                totalHits = all.Length,
                data = id.Length > 0 ? all : all.Skip(Number(request, "skip", 0)).Take(Number(request, "take", 20)).ToArray()
            });
        });
    }

    private static string[] PackageIds(PublishedPackage[] packages) =>
        packages.Select(x => x.Id).Distinct(StringComparer.OrdinalIgnoreCase).Order(StringComparer.OrdinalIgnoreCase).ToArray();

    private static BuildEntry[] BuildsFor(PublishedPackage[] packages, string id) =>
        packages.Where(x => x.Id.Equals(id, StringComparison.OrdinalIgnoreCase)).Select(x => x.Build).ToArray();

    private static int Number(HttpRequest request, string name, int fallback) =>
        int.TryParse(request.Query[name], NumberStyles.None, CultureInfo.InvariantCulture, out var value)
            ? (name == "take" ? Math.Min(value, 1000) : value) : fallback;

    private static bool IncludesBuilds(HttpRequest request) =>
        bool.TryParse(request.Query["prerelease"], out var prerelease) && prerelease
        && NuGetVersion.TryParse(request.Query["semVerLevel"], out var semver) && semver >= new NuGetVersion(2, 0, 0);

    private static async Task<BuildEntry?> FindAsync(PackageCatalog catalog, string id, string version, CancellationToken ct)
    {
        if (!PackageIdValidator.IsValidPackageId(id) || !NuGetVersion.TryParse(version, out var parsed)) return null;
        return BuildsFor(await catalog.GetPackagesAsync(ct), id).SingleOrDefault(x => NuGetVersion.Parse(x.Version) == parsed);
    }

    private static async Task<string> PackageTypeAsync(PackageCache cache, string id, BuildEntry build, CancellationToken ct)
    {
        using var reader = new PackageArchiveReader(await cache.GetAsync(id, build, ct));
        return reader.NuspecReader.GetPackageTypes().FirstOrDefault()?.Name ?? "Dependency";
    }

    private static async Task<object> LeafAsync(ProxyOptions options, PackageCache cache, string id, BuildEntry build, CancellationToken ct)
    {
        id = id.ToLowerInvariant();
        using var reader = new PackageArchiveReader(await cache.GetAsync(id, build, ct));
        var nuspec = reader.NuspecReader;
        var content = $"{options.BaseUrl}/flat/{id}/{build.Version}/{id}.{build.Version}.nupkg";
        return new Dictionary<string, object>
        {
            ["@id"] = $"{options.BaseUrl}/registration/{id}/{build.Version}.json",
            ["listed"] = true,
            ["packageContent"] = content,
            ["registration"] = $"{options.BaseUrl}/registration/{id}/index.json",
            ["catalogEntry"] = new Dictionary<string, object>
            {
                ["@id"] = $"{options.BaseUrl}/registration/{id}/{build.Version}.json#catalog",
                ["id"] = nuspec.GetId(),
                ["version"] = build.Version,
                ["listed"] = true,
                ["authors"] = nuspec.GetAuthors(),
                ["description"] = nuspec.GetDescription(),
                ["title"] = $"{nuspec.GetId()} - PR #{build.PullRequest}, run {build.RunId}",
                ["projectUrl"] = $"https://github.com/{options.Repository}/pull/{build.PullRequest}",
                ["packageContent"] = content,
                ["dependencyGroups"] = nuspec.GetDependencyGroups().Select(g => new
                {
                    targetFramework = g.TargetFramework.GetShortFolderName(),
                    dependencies = g.Packages.Select(p => new
                    {
                        id = p.Id,
                        range = p.VersionRange.ToNormalizedString()
                    })
                })
            }
        };
    }
}
