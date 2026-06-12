using Velopack.Core;
using Velopack.Core.Json;
using Velopack.Util;

namespace Velopack.Tests;

public class JsonConfigLoaderTests
{
    private enum TestMode
    {
        None,
        Fast,
        Slow,
    }

    private class TestOptions
    {
        public string PackId { get; set; }
        public string PackVersion { get; set; }
        public int Timeout { get; set; } = 30;
        public DirectoryInfo ReleaseDir { get; set; }
        public RID TargetRuntime { get; set; }
        public TestMode Mode { get; set; }
        public FileInfo SomeFile { get; set; }
    }

    [Fact]
    public void Populate_OverlaysOnlySpecifiedKeys()
    {
        // simulates json > env var > default precedence: values already set on the object
        // (eg. from env vars) survive when omitted from the json.
        var options = new TestOptions {
            PackId = "From.Env",
            PackVersion = "1.0.0",
        };

        JsonConfigLoader.PopulateFromJson("""{ "packVersion": "2.0.0" }""", options);

        Assert.Equal("From.Env", options.PackId); // not in json - preserved
        Assert.Equal("2.0.0", options.PackVersion); // overridden by json
        Assert.Equal(30, options.Timeout); // default preserved
    }

    [Fact]
    public void Populate_MatchesKeysCaseInsensitively()
    {
        var options = new TestOptions();
        JsonConfigLoader.PopulateFromJson("""{ "PACKID": "My.App" }""", options);
        Assert.Equal("My.App", options.PackId);
    }

    [Fact]
    public void Populate_IgnoresSchemaKey()
    {
        var options = new TestOptions();
        JsonConfigLoader.PopulateFromJson("""{ "$schema": "https://example.com/schema.json", "packId": "My.App" }""", options);
        Assert.Equal("My.App", options.PackId);
    }

    [Fact]
    public void Populate_UnknownKey_Throws()
    {
        var options = new TestOptions();
        var ex = Assert.Throws<UserInfoException>(() => JsonConfigLoader.PopulateFromJson("""{ "packIdd": "My.App" }""", options));
        Assert.Contains("packIdd", ex.Message);
    }

    [Fact]
    public void Populate_InvalidJson_Throws()
    {
        var options = new TestOptions();
        Assert.Throws<UserInfoException>(() => JsonConfigLoader.PopulateFromJson("not json {", options));
    }

    [Fact]
    public void Populate_MissingFile_Throws()
    {
        var options = new TestOptions();
        var ex = Assert.Throws<UserInfoException>(() => JsonConfigLoader.Populate(Path.Combine(Path.GetTempPath(), Path.GetRandomFileName()), options));
        Assert.Contains("does not exist", ex.Message);
    }

    [Fact]
    public void Populate_ConvertsRid()
    {
        var options = new TestOptions();
        JsonConfigLoader.PopulateFromJson("""{ "targetRuntime": "win-x64" }""", options);
        Assert.Equal(RID.Parse("win-x64"), options.TargetRuntime);
    }

    [Fact]
    public void Populate_InvalidRid_IsParsedAsUnknown()
    {
        // RID.Parse is lenient and does not throw for garbage input - it produces a RID with an
        // Unknown BaseRID, which is rejected later by the MustBeSupportedRid validator rule.
        var options = new TestOptions();
        JsonConfigLoader.PopulateFromJson("""{ "targetRuntime": "!!!" }""", options);
        Assert.NotNull(options.TargetRuntime);
        Assert.Equal(RuntimeOs.Unknown, options.TargetRuntime.BaseRID);
    }

    [Fact]
    public void Populate_ConvertsEnumFromString()
    {
        var options = new TestOptions();
        JsonConfigLoader.PopulateFromJson("""{ "mode": "fast" }""", options);
        Assert.Equal(TestMode.Fast, options.Mode);
    }

    [Fact]
    public void Populate_ConvertsFileInfo()
    {
        var options = new TestOptions();
        JsonConfigLoader.PopulateFromJson("""{ "someFile": "myfile.txt" }""", options);
        Assert.Equal("myfile.txt", options.SomeFile.Name);
    }

    [Fact]
    public void Populate_DoesNotCreateMissingDirectory()
    {
        using var _1 = TempUtil.GetTempDirectory(out var tempDir);
        var newDir = Path.Combine(tempDir, "sub", "releases");

        var options = new TestOptions();
        JsonConfigLoader.PopulateFromJson($$"""{ "releaseDir": {{System.Text.Json.JsonSerializer.Serialize(newDir)}} }""", options);

        Assert.Equal(newDir, options.ReleaseDir.FullName);
        // directories must be created at point of use after validation, not as a parsing side effect
        Assert.False(Directory.Exists(newDir));
    }

    [Fact]
    public void Populate_PreservesDateLikeStrings()
    {
        // Json.NET would normally re-interpret date-like strings (shifting timezones and dropping
        // fractional seconds) - the loader must pass them through to string properties verbatim.
        var options = new TestOptions();
        JsonConfigLoader.PopulateFromJson("""{ "packId": "2023-11-23T08:00:00.0000000+03:00" }""", options);
        Assert.Equal("2023-11-23T08:00:00.0000000+03:00", options.PackId);
    }

    [Fact]
    public void Populate_FromFile_Works()
    {
        using var _1 = TempUtil.GetTempFileName(out var path);
        File.WriteAllText(path, """{ "packId": "File.App" }""");

        var options = new TestOptions();
        JsonConfigLoader.Populate(path, options);

        Assert.Equal("File.App", options.PackId);
    }
}
