namespace Velopack.Flow;

public class VelopackFlowServiceOptions
{
    public string? VelopackBaseUrl { get; set; } = string.Empty;

    public string? ApiKey { get; set; } = string.Empty;
    
    public TimeSpan Timeout { get; set; } = TimeSpan.FromMinutes(30);
}