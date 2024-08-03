namespace Velopack.Packaging.Flow;

public class VelopackServiceOptions
{
    public const string DefaultBaseUrl = "https://api.velopack.io/";

    public string VelopackBaseUrl { get; set; } = DefaultBaseUrl;

    public string ApiKey { get; set; } = string.Empty;
}
