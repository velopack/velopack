using System.Xml.Linq;
using Microsoft.Build.Construction;
using NuGet.Versioning;

namespace Squirrel.Csq.Compat;

public class SquirrelVersionLocator
{
    private readonly ILogger _logger;

    public SquirrelVersionLocator(ILogger logger)
    {
        _logger = logger;
    }

    public NuGetVersion Search(string solutionDir, string packageName)
    {
        var dependencies = GetPackageVersionsFromDir(solutionDir, packageName).Distinct().ToArray();

        if (dependencies.Length == 0) {
            throw new Exception($"{packageName} nuget package was not found installed in solution.");
        }

        if (dependencies.Length > 1) {
            throw new Exception($"Found multiple versions of {packageName} installed in solution ({string.Join(", ", dependencies)}). " +
                                $"Please consolidate to a single version.'");
        }

        var targetVersion = dependencies.Single();
        return NuGetVersion.Parse(targetVersion);
    }

    IEnumerable<string> GetPackageVersionsFromDir(string rootDir, string packageName)
    {
        // old-style framework packages.config
        foreach (var packagesFile in EnumerateFilesUntilSpecificDepth(rootDir, "packages.config", 3)) {
            using var xmlStream = File.OpenRead(packagesFile);
            var xdoc = XDocument.Load(xmlStream);

            var sqel = xdoc.Root?.Elements().FirstOrDefault(e => e.Attribute("id")?.Value == packageName);
            var ver = sqel?.Attribute("version");
            if (ver == null) continue;

            _logger.Debug($"{packageName} {ver.Value} referenced in {packagesFile}");

            if (ver.Value.Contains('*'))
                throw new Exception(
                    $"Wildcard versions are not supported in packages.config. Remove wildcard or upgrade csproj format to use PackageReference.");

            yield return ver.Value;
        }

        // new-style csproj PackageReference
        foreach (var projFile in EnumerateFilesUntilSpecificDepth(rootDir, "*.csproj", 3)) {
            var proj = ProjectRootElement.Open(projFile);
            if (proj == null) continue;

            ProjectItemElement item = proj.Items.FirstOrDefault(i => i.ItemType == "PackageReference" && i.Include == packageName);
            if (item == null) continue;

            var version = item.Children.FirstOrDefault(x => x.ElementName == "Version") as ProjectMetadataElement;
            if (version?.Value == null) continue;

            _logger.Debug($"{packageName} {version.Value} referenced in {projFile}");

            yield return version.Value;
        }
    }

    static IEnumerable<string> EnumerateFilesUntilSpecificDepth(string rootPath, string searchPattern, int maxDepth, int currentDepth = 0)
    {
        var files = Directory.EnumerateFiles(rootPath, searchPattern, SearchOption.TopDirectoryOnly);
        foreach (var f in files) {
            yield return f;
        }

        if (currentDepth < maxDepth) {
            foreach (var dir in Directory.EnumerateDirectories(rootPath)) {
                foreach (var file in EnumerateFilesUntilSpecificDepth(dir, searchPattern, maxDepth, currentDepth + 1)) {
                    yield return file;
                }
            }
        }
    }
}
