namespace Velopack.Flow.Commands;

public sealed class PublishOptions : VelopackFlowServiceOptions
{
    public RuntimeOs TargetOs { get; set; }

    public string ReleaseDirectory { get; set; } = "";

    public string? Channel { get; set; }

    public bool WaitForLive { get; set; }
}