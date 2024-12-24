namespace Velopack.Flow.Commands;

public sealed class ApiOptions : VelopackFlowServiceOptions
{
    public string Endpoint { get; set; } = "";
    public string Method { get; set; } = "";
    public string? Body { get; set; }
}