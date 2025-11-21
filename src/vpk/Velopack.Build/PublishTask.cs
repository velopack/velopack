using System;
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

    // Tool configuration properties
    public string VelopackToolMode { get; set; } = "Auto";
    public string? VelopackToolVersion { get; set; }
    public bool VelopackToolPrerelease { get; set; }
    public string? VelopackToolSource { get; set; }
    public bool VelopackSkipToolInstall { get; set; }

    protected override async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        try {
            // Resolve VPK tool
            var resolver = new VpkToolResolver(Log);
            var config = new VpkToolConfiguration
            {
                Mode = ParseToolMode(VelopackToolMode),
                Version = VelopackToolVersion,
                AllowPrerelease = VelopackToolPrerelease,
                Source = VelopackToolSource,
                SkipInstall = VelopackSkipToolInstall,
                WorkingDirectory = System.Environment.CurrentDirectory
            };

            var tool = await resolver.ResolveToolAsync(config, cancellationToken);
            var toolRunner = new DotNetToolRunner(Log);

            // Build VPK flow publish command arguments
            var args = BuildPublishArguments();

            // Setup environment variables for API configuration
            var envVars = new System.Collections.Generic.Dictionary<string, string>();
            if (!string.IsNullOrWhiteSpace(ServiceUrl))
            {
                envVars["VPK_FLOW_SERVICE_URL"] = ServiceUrl;
            }
            if (!string.IsNullOrWhiteSpace(ApiKey))
            {
                envVars["VPK_FLOW_API_KEY"] = ApiKey;
            }

            Log.LogMessage(MessageImportance.High, $"Executing: dotnet {tool.ExecutionPrefix} {string.Join(" ", args)}");

            // Run VPK tool
            var exitCode = await toolRunner.RunToolAsync("vpk", args, tool.IsLocal, config.WorkingDirectory, envVars, cancellationToken);

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
        var builder = new ArgumentBuilder();
        
        // Add flow publish command
        builder.AddCommand("flow");
        builder.AddCommand("publish");

        // Required arguments
        builder.AddOption("--outputDir", ReleaseDirectory);

        // Optional arguments
        builder.AddOption("--channel", Channel);
        
        // Wait for live flag
        builder.AddOption("--waitForLive", WaitForLive);

        return builder.Build();
    }

    private static VpkToolConfiguration.ToolMode ParseToolMode(string mode)
    {
        return mode?.ToLowerInvariant() switch
        {
            "local" => VpkToolConfiguration.ToolMode.Local,
            "global" => VpkToolConfiguration.ToolMode.Global,
            _ => VpkToolConfiguration.ToolMode.Auto
        };
    }
}