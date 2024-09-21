using System.Text.Json.Serialization;
using System.Text.Json;
using NuGet.Versioning;
using Velopack.Packaging;
using Velopack.Sources;
using JsonPropertyNameAttribute = System.Text.Json.Serialization.JsonPropertyNameAttribute;

#if NET5_0_OR_GREATER
using SimpleJsonNameAttribute = System.Text.Json.Serialization.JsonPropertyNameAttribute;
#else
using SimpleJsonNameAttribute = Velopack.Json.JsonPropertyNameAttribute;
#endif

namespace Velopack.Tests;

public class SimpleJsonTests
{
    public static readonly JsonSerializerOptions Options = new JsonSerializerOptions {
        AllowTrailingCommas = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter(), new SemanticVersionConverter() },
    };

    internal class SemanticVersionConverter : JsonConverter<SemanticVersion>
    {
        public override SemanticVersion Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return SemanticVersion.Parse(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, SemanticVersion value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToFullString());
        }
    }

    [Fact]
    public void JsonPropertyNameAttribueWrks()
    {
        var obj = new TestGithubReleaseAsset {
            UrlSomething = "https://ho",
            BrowserDownloadUrl = "https://browser",
            ContentType = "via",
        };
        var json = JsonSerializer.Serialize(obj, Options);
        var dez = SimpleJson.DeserializeObject<GithubReleaseAsset>(json);
        Assert.Equal(obj.UrlSomething, dez.Url);
        Assert.Equal(obj.BrowserDownloadUrl, dez.BrowserDownloadUrl);
        Assert.Equal(obj.ContentType, dez.ContentType);
    }

    [Fact]
    public void JsonCanRoundTripComplexTypes()
    {
        var obj = new TestClass1 {
            NameAsd = "hello",
            UpcomingRelease = true,
            ReleasedAt = DateTime.UtcNow,
            Version = SemanticVersion.Parse("1.2.3-hello.23+metadata"),
            AssetType = VelopackAssetType.Delta,
            Greetings = new List<string> { "hi", "there" },
        };
        var json = JsonSerializer.Serialize(obj, Options);

        Assert.Contains("\"Delta\"", json);

        var dez = SimpleJson.DeserializeObject<TestClass2>(json);
        Assert.Equal(obj.NameAsd, dez.nameAsd);
        Assert.Equal(obj.UpcomingRelease, dez.upcomingRelease);
        Assert.Equal(obj.ReleasedAt, dez.releasedAt);
        Assert.Equal(obj.Version, dez.version);
        Assert.Equal(obj.AssetType, dez.assetType);
        Assert.Equal(obj.Greetings, dez.greetings);
    }

    [Fact]
    public void JsonCanParseReleasesJson()
    {
        var json = File.ReadAllText(PathHelper.GetFixture("testfeed.json"));
        var feed = SimpleJson.DeserializeObject<VelopackAssetFeed>(json);
        Assert.Equal(21, feed.Assets.Length);
        Assert.True(feed.Assets.First().Version == new SemanticVersion(1, 0, 11));
    }

    public class TestGithubReleaseAsset
    {
        /// <summary> 
        /// The asset URL for this release asset. Requests to this URL will use API
        /// quota and return JSON unless the 'Accept' header is "application/octet-stream". 
        /// </summary>
        [JsonPropertyName("url")]
        public string UrlSomething { get; set; }

        /// <summary>  
        /// The browser URL for this release asset. This does not use API quota,
        /// however this URL only works for public repositories. If downloading
        /// assets from a private repository, the <see cref="Url"/> property must
        /// be used with an appropriate access token.
        /// </summary>
        [JsonPropertyName("browser_download_url")]
        public string BrowserDownloadUrl { get; set; }

        /// <summary> The mime type of this release asset (as detected by GitHub). </summary>
        [JsonPropertyName("content_type")]
        public string ContentType { get; set; }
    }


    internal class TestClass1
    {
        public string NameAsd { get; set; }

        [JsonPropertyName("upcoming_release888")]
        public bool UpcomingRelease { get; set; }

        [JsonPropertyName("released_at")]
        public DateTime ReleasedAt { get; set; }

        public SemanticVersion Version { get; set; }

        [JsonPropertyName("t")]
        public VelopackAssetType AssetType { get; set; }

        public List<string> Greetings { get; set; }
    }

    internal class TestClass2
    {
        public string nameAsd { get; set; }

        [SimpleJsonName("upcoming_release888")]
        public bool upcomingRelease { get; set; }

        [SimpleJsonName("released_at")]
        public DateTime releasedAt { get; set; }

        public SemanticVersion version { get; set; }

        [SimpleJsonName("t")]
        public VelopackAssetType assetType { get; set; }

        public List<string> greetings { get; set; }
    }
}
