#nullable enable
namespace Velopack.Packaging.Flow;

internal sealed class CreateReleaseGroupRequest
{
    public string? PackageId { get; set; }
    public string? Version { get; set; }
    public string? ChannelIdentifier { get; set; }
}