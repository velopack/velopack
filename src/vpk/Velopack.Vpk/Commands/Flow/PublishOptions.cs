using Velopack.Packaging.Flow;

namespace Velopack.Vpk.Commands.Flow;

#nullable enable
public sealed class PublishOptions : VelopackServiceOptions
{
    public RuntimeOs TargetOs { get; set; }

    public string ReleaseDirectory { get; set; } = "";

    public string? Channel { get; set; }
    
    public bool NoWaitForLive { get; set; }
}