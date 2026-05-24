namespace Velopack.Sources.Tests.Infrastructure;

public static class TestFeedData
{
    public const string PackageId = "TestApp";
    public const string CurrentVersion = "1.0.0";
    public const string UpdateVersion = "2.0.0";
    public const string Channel = "test";
    public const string FileName = "TestApp-2.0.0-full.nupkg";
    public const string SHA1 = "da39a3ee5e6b4b0d3255bfef95601890afd80709";
    public const string SHA256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    public const long Size = 1024;

    public static readonly string FeedJson = """
        {
          "Assets": [
            {
              "PackageId": "TestApp",
              "Version": "2.0.0",
              "Type": "Full",
              "FileName": "TestApp-2.0.0-full.nupkg",
              "SHA1": "da39a3ee5e6b4b0d3255bfef95601890afd80709",
              "SHA256": "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855",
              "Size": 1024
            }
          ]
        }
        """;

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
    }
}
