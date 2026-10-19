using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using Microsoft.Build.Framework;

namespace Velopack.Build;

public class PublishTask : VpkTask
{
    [Required]
    [NotNull]
    public string? ReleaseDirectory { get; set; }

    public string? ServiceUrl { get; set; }

    public string? Channel { get; set; }

    public string? ApiKey { get; set; }

    public string? Timeout { get; set; }

    public bool WaitForLive { get; set; }

    protected override string CommandName => "publish";

    protected override Dictionary<string, object?> BuildJsonOptions()
    {
        var options = new Dictionary<string, object?>();
        AddIfNotEmpty(options, "ReleaseDirectory", ReleaseDirectory);
        AddIfNotEmpty(options, "Channel", Channel);
        AddIfNotEmpty(options, "VelopackBaseUrl", ServiceUrl);
        AddIfNotEmpty(options, "ApiKey", ApiKey);
        if (!string.IsNullOrWhiteSpace(Timeout)) {
            if (!double.TryParse(Timeout, NumberStyles.Float, CultureInfo.InvariantCulture, out double timeout)) {
                throw new FormatException($"Timeout must be a number of minutes, but was '{Timeout}'.");
            }
            options["Timeout"] = timeout;
        }
        if (WaitForLive) {
            options["WaitForLive"] = true;
        }

        return options;
    }

    private static void AddIfNotEmpty(Dictionary<string, object?> options, string name, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value)) {
            options[name] = value;
        }
    }

    protected override string GetSuccesMessage()
        => "Successfully published release to Velopack Flow";
}