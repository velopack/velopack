using System.IO.Compression;
using System.Security.Cryptography;
using System.Xml.Linq;
using NuGet.Packaging;
using NuGet.Versioning;

namespace PrNuGetProxy;

public static class PackageRewriter
{
    public static string VersionFor(string original, int pr, long run, int attempt, long artifact)
    {
        var version = NuGetVersion.Parse(original);
        var numeric = version.Version.Revision > 0 ? version.Version.ToString(4) : version.Version.ToString(3);
        return $"{numeric}-pr.{pr}.run.{run}.attempt.{attempt}.artifact.{artifact}";
    }

    public static void Rewrite(string source, string destination, string id, string originalVersion, string version,
        IReadOnlyDictionary<string, string>? packageVersions = null)
    {
        using var archive = ZipFile.OpenRead(source);
        if (archive.Entries.Any(x => x.FullName.Equals(".signature.p7s", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Signed NuGet packages cannot be rewritten without invalidating their signature.");
        var manifests = archive.Entries.Where(x => !x.FullName.Contains('/') && x.FullName.EndsWith(".nuspec", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (manifests.Length != 1) throw new InvalidDataException("Expected exactly one root nuspec.");
        XDocument manifest;
        using (var stream = manifests[0].Open()) manifest = XDocument.Load(stream);
        var metadata = manifest.Root?.Elements().SingleOrDefault(x => x.Name.LocalName == "metadata")
            ?? throw new InvalidDataException("Missing nuspec metadata.");
        var idElement = metadata.Elements().Single(x => x.Name.LocalName == "id");
        var versionElement = metadata.Elements().Single(x => x.Name.LocalName == "version");
        if (!idElement.Value.Equals(id, StringComparison.OrdinalIgnoreCase)
            || NuGetVersion.Parse(versionElement.Value) != NuGetVersion.Parse(originalVersion))
            throw new InvalidDataException($"Package identity does not match expected {id} {originalVersion}.");
        versionElement.Value = version;
        if (packageVersions is not null)
        {
            foreach (var dependency in metadata.Elements().Where(x => x.Name.LocalName == "dependencies")
                .Descendants().Where(x => x.Name.LocalName == "dependency"))
            {
                if (dependency.Attribute("id") is { } dependencyId && packageVersions.TryGetValue(dependencyId.Value, out var dependencyVersion))
                    dependency.SetAttributeValue("version", $"[{dependencyVersion}]");
            }
        }

        using var target = ZipFile.Open(destination, ZipArchiveMode.Create);
        foreach (var entry in archive.Entries)
        {
            var rewritten = target.CreateEntry(entry.FullName, CompressionLevel.Fastest);
            rewritten.LastWriteTime = entry.LastWriteTime;
            rewritten.ExternalAttributes = entry.ExternalAttributes;
            using var output = rewritten.Open();
            if (entry == manifests[0]) manifest.Save(output);
            else
            {
                using var input = entry.Open();
                input.CopyTo(output);
            }
        }
    }

    public static async Task VerifyDigestAsync(string path, Artifact artifact, CancellationToken cancellationToken)
    {
        if (artifact.Digest is null || !artifact.Digest.StartsWith("sha256:", StringComparison.Ordinal))
            throw new InvalidDataException($"Artifact {artifact.Id} has no supported SHA256 digest.");
        await using var file = File.OpenRead(path);
        var actual = Convert.ToHexString(await SHA256.HashDataAsync(file, cancellationToken));
        if (!actual.Equals(artifact.Digest[7..], StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException($"Artifact {artifact.Id} failed SHA256 verification.");
    }

    public static byte[] ReadManifest(string path)
    {
        using var reader = new PackageArchiveReader(path);
        using var stream = reader.GetNuspec();
        using var output = new MemoryStream();
        stream.CopyTo(output);
        return output.ToArray();
    }
}
