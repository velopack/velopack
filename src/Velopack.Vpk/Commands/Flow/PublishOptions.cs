using Velopack.Packaging.Flow;

namespace Velopack.Vpk.Commands.Flow;

#nullable enable
public sealed class PublishOptions : VelopackServiceOptions
{
    public string ReleaseDirectory { get; set; } = "";

    public string? Channel { get; set; }
}