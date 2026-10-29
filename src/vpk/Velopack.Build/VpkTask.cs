using Microsoft.Build.Framework;

namespace Velopack.Build;

public abstract class VpkTask : MSBuildAsyncTask
{
    [Required]
    public string VpkVersion { get; set; } = "";

    public string? VpkNugetSource { get; set; }

    public string? NugetConfigFile { get; set; }

    [Required]
    public string ProjectDirectory { get; set; } = "";

    protected sealed override async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        try {
            var toolRunner = new VpkToolRunner(VpkVersion, VpkNugetSource, Log) {
                ProjectDirectory = ProjectDirectory,
                NugetConfigFile = NugetConfigFile,
            };
            
            if (!await PreExecuteAsync(toolRunner, cancellationToken).ConfigureAwait(false)) {
                return false;
            }
            
            var options = BuildJsonOptions();

            Log.LogMessage(MessageImportance.High, $"Executing: vpk [json] {CommandName} <config>");

            var exitCode = await toolRunner.RunVpkJson([CommandName], options, BuildEnvironmentVariables(), cancellationToken)
                .ConfigureAwait(false);

            if (exitCode == 0) {
                Log.LogMessage(MessageImportance.High, GetSuccesMessage());
                return true;
            } else {
                Log.LogError($"VPK tool exited with code {exitCode}");
                return false;
            }
        } catch (Exception ex) {
            Log.LogErrorFromException(ex, true, true, null);
            return false;
        }
    }

    protected virtual Task<bool> PreExecuteAsync(VpkToolRunner toolRunner, CancellationToken cancellationToken)
    {
        return Task.FromResult(true);
    }

    protected abstract string GetSuccesMessage();

    protected abstract string CommandName { get; }

    protected abstract Dictionary<string, object?> BuildJsonOptions();

    protected virtual Dictionary<string, string> BuildEnvironmentVariables() => [];
}
