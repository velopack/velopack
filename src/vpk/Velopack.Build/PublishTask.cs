using System.Diagnostics.CodeAnalysis;
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

    protected override Dictionary<string, string> BuildEnvironmentVariables()
    {
        Dictionary<string, string> envVars = base.BuildEnvironmentVariables();
        if (!string.IsNullOrWhiteSpace(ServiceUrl)) {
            envVars["VPK_FLOW_SERVICE_URL"] = ServiceUrl!;
        }
        if (!string.IsNullOrWhiteSpace(ApiKey)) {
            envVars["VPK_FLOW_API_KEY"] = ApiKey!;
        }
        return envVars;
    }

    protected override string GetSuccesMessage()
        => "Successfully published release to Velopack Flow";

    protected override string[] BuildArguments()
    {
        IEnumerable<string> GetArguments()
        {
            yield return "publish";
            yield return "--legacyConsole";
            yield return "--yes";

            if (!string.IsNullOrWhiteSpace(ReleaseDirectory)) {
                yield return "--outputDir";
                yield return ReleaseDirectory;
            }

            if (!string.IsNullOrWhiteSpace(Channel)) {
                yield return "--channel";
                yield return Channel!;
            }

            if (!string.IsNullOrWhiteSpace(Timeout)) {
                yield return "--timeout";
                yield return Timeout!;
            }

            if (WaitForLive) {
                yield return "--waitForLive";
            }
        }

        return [.. GetArguments()];
    }
}