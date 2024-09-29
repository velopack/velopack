#nullable enable
using Newtonsoft.Json.Converters;
using Newtonsoft.Json;

namespace Velopack.Packaging.Flow;

internal sealed class UpdateReleaseGroupRequest
{
    public string? NotesHtml { get; set; }
    public string? NotesMarkdown { get; set; }
    public ReleaseGroupState? State { get; set; }
}

[JsonConverter(typeof(StringEnumConverter))]
internal enum ReleaseGroupState
{
    Draft,
    Published,
    Unlisted
}
