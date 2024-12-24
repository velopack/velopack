namespace Velopack.Flow;

public class VelopackFlowLoginOptions
{
    public bool AllowCacheCredentials { get; set; } = true;
    public bool AllowInteractiveLogin { get; set; } = true;
    public bool AllowDeviceCodeFlow { get; set; } = true;
}