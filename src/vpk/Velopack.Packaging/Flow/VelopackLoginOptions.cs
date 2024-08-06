#nullable enable
namespace Velopack.Packaging.Flow;

public class VelopackLoginOptions : VelopackServiceOptions
{
    public bool AllowCacheCredentials { get; set; } = true;
    public bool AllowInteractiveLogin { get; set; } = true;
    public bool AllowDeviceCodeFlow { get; set; } = true;
}
