using System.IO.Compression;
using System.Text.Json;

namespace Velopack.Sources.Tests.Infrastructure;

public static class TestFeedData
{
    public const string PackageId = "TestApp";
    public const string CurrentVersion = "1.0.0";
    public const string LatestVersion = "1.0.3";
    public const string Channel = "test";

    public static readonly string[] AllVersions = ["1.0.1", "1.0.2", "1.0.3"];

    public static string FullFileName(string version) => $"TestApp-{version}-full.nupkg";
    public static string DeltaFileName(string version) => $"TestApp-{version}-delta.nupkg";
    public static string NotesMarkdown(string version) => $"## Release {version}\nBug fixes and improvements.";
    public static string NotesHTML(string version) => $"<h2>Release {version}</h2>\n<p>Bug fixes and improvements.</p>";

    public const string SHA1 = "da39a3ee5e6b4b0d3255bfef95601890afd80709";
    public const string SHA256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    public const long FullSize = 10000;
    public const long DeltaSize = 500;

    public static string FeedJsonForVersion(string version)
    {
        var feed = new {
            Assets = new object[] {
                new {
                    PackageId,
                    Version = version,
                    Type = "Full",
                    FileName = FullFileName(version),
                    SHA1,
                    SHA256,
                    Size = FullSize,
                    NotesMarkdown = NotesMarkdown(version),
                    NotesHTML = NotesHTML(version),
                },
                new {
                    PackageId,
                    Version = version,
                    Type = "Delta",
                    FileName = DeltaFileName(version),
                    SHA1,
                    SHA256,
                    Size = DeltaSize,
                },
            },
        };
        return JsonSerializer.Serialize(feed, new JsonSerializerOptions { WriteIndented = true });
    }

    public static string FeedJson { get; } = BuildFullFeedJson();

    private static string BuildFullFeedJson()
    {
        var assets = new List<object>();
        foreach (var version in AllVersions) {
            assets.Add(new {
                PackageId,
                Version = version,
                Type = "Full",
                FileName = FullFileName(version),
                SHA1,
                SHA256,
                Size = FullSize,
                NotesMarkdown = NotesMarkdown(version),
                NotesHTML = NotesHTML(version),
            });
            assets.Add(new {
                PackageId,
                Version = version,
                Type = "Delta",
                FileName = DeltaFileName(version),
                SHA1,
                SHA256,
                Size = DeltaSize,
            });
        }
        return JsonSerializer.Serialize(new { Assets = assets }, new JsonSerializerOptions { WriteIndented = true });
    }

    public static readonly string ManifestXml = """
        <?xml version="1.0" encoding="utf-8"?>
        <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
          <metadata>
            <id>TestApp</id>
            <version>1.0.0</version>
            <channel>test</channel>
            <mainExe>TestApp</mainExe>
          </metadata>
        </package>
        """;

    public static void GenerateTestData(string testDataDir)
    {
        var fileDir = Path.Combine(testDataDir, "file");
        var httpDir = Path.Combine(testDataDir, "http");
        var packagesDir = Path.Combine(testDataDir, "packages");

        Directory.CreateDirectory(fileDir);
        Directory.CreateDirectory(httpDir);
        Directory.CreateDirectory(packagesDir);

        File.WriteAllText(Path.Combine(fileDir, $"releases.{Channel}.json"), FeedJson);
        File.WriteAllText(Path.Combine(httpDir, $"releases.{Channel}.json"), FeedJson);
        File.WriteAllText(Path.Combine(testDataDir, "sq.version"), ManifestXml);

        CreateLocalFullPackage(packagesDir, CurrentVersion);
    }

    private static void CreateLocalFullPackage(string packagesDir, string version)
    {
        var nupkgPath = Path.Combine(packagesDir, FullFileName(version));
        if (File.Exists(nupkgPath)) return;

        var nuspecXml = $"""
            <?xml version="1.0" encoding="utf-8"?>
            <package xmlns="http://schemas.microsoft.com/packaging/2010/07/nuspec.xsd">
              <metadata>
                <id>{PackageId}</id>
                <version>{version}</version>
                <channel>{Channel}</channel>
                <mainExe>TestApp</mainExe>
              </metadata>
            </package>
            """;

        using var stream = new FileStream(nupkgPath, FileMode.Create);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Create);
        var entry = archive.CreateEntry($"{PackageId}.nuspec");
        using var writer = new StreamWriter(entry.Open());
        writer.Write(nuspecXml);
    }
}
