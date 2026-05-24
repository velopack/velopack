using System.Text.Json.Serialization;

namespace Velopack.Sources.Tests.Infrastructure;

public class HarnessResult
{
    [JsonPropertyName("target")]
    public HarnessAsset? Target { get; set; }

    [JsonPropertyName("deltas")]
    public HarnessAsset[]? Deltas { get; set; }

    [JsonPropertyName("isDowngrade")]
    public bool IsDowngrade { get; set; }

    [JsonPropertyName("feed")]
    public HarnessAsset[]? Feed { get; set; }
}

public class HarnessAsset
{
    [JsonPropertyName("PackageId")]
    public string? PackageId { get; set; }

    [JsonPropertyName("Version")]
    public string? Version { get; set; }

    [JsonPropertyName("FileName")]
    public string? FileName { get; set; }

    [JsonPropertyName("Type")]
    public string? Type { get; set; }

    [JsonPropertyName("SHA1")]
    public string? SHA1 { get; set; }

    [JsonPropertyName("SHA256")]
    public string? SHA256 { get; set; }

    [JsonPropertyName("Size")]
    public long Size { get; set; }

    [JsonPropertyName("NotesMarkdown")]
    public string? NotesMarkdown { get; set; }

    [JsonPropertyName("NotesHtml")]
    public string? NotesHtml { get; set; }
}
