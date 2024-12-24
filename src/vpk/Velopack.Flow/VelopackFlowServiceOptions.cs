namespace Velopack.Flow;

public class VelopackFlowServiceOptions
{
    public string? VelopackBaseUrl { get; set; } = string.Empty;

    public string? ApiKey { get; set; } = string.Empty;
    
    public double Timeout { get; set; } = 30d;
}