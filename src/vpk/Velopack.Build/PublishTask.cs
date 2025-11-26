using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Framework;

namespace Velopack.Build;

public class PublishTask : MSBuildAsyncTask
{
    [Required]
    public string ReleaseDirectory { get; set; } = "";

    public string? ServiceUrl { get; set; }

    public string? Channel { get; set; }

    public string? ApiKey { get; set; }

    public string? Timeout { get; set; }

    public bool WaitForLive { get; set; }

    protected override async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        try {
            // Resolve VPK tool
            var toolRunner = new VpkToolRunner(Log);

            // Build VPK flow publish command arguments
            var args = BuildPublishArguments();

            // Setup environment variables for API configuration
            var envVars = new System.Collections.Generic.Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(ServiceUrl))
            {
                envVars["VPK_FLOW_SERVICE_URL"] = ServiceUrl!;
            }
            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                envVars["VPK_FLOW_API_KEY"] = ApiKey!;
            }

            Log.LogMessage(MessageImportance.High, $"Executing: vpk publish {string.Join(" ", args)}");

            // Run VPK tool
            var exitCode = await toolRunner.RunVpk(args, envVars, cancellationToken)
                .ConfigureAwait(false);

            if (exitCode == 0)
            {
                Log.LogMessage(MessageImportance.High, "Successfully published release to Velopack Flow");
                return true;
            }
            else
            {
                Log.LogError($"VPK tool exited with code {exitCode}");
                return false;
            }
        } catch (Exception ex) {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    private string[] BuildPublishArguments()
    {
        IEnumerable<string> GetArguments()
        {
            yield return "flow";
            yield return "publish";

            if (!string.IsNullOrWhiteSpace(ReleaseDirectory))
            {
                yield return "--outputDir";
                yield return ReleaseDirectory;
            }

            if (!string.IsNullOrWhiteSpace(Channel))
            {
                yield return "--channel";
                yield return Channel!;
            }

            if (!string.IsNullOrWhiteSpace(Timeout))
            {
                yield return "--timeout";
                yield return Timeout!;
            }

            if (WaitForLive)
            {
                yield return "--waitForLive";
            }
        }

        return [..GetArguments()];
    }
}