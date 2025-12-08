using Microsoft.Build.Framework;

namespace Velopack.Build;

public abstract class VpkTask : MSBuildAsyncTask
{
    protected sealed override async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        try {
            var toolRunner = new VpkToolRunner(Log);
            
            if (!await PreExecuteAsync(toolRunner, cancellationToken).ConfigureAwait(false)) {
                return false;
            }
            
            var args = BuildArguments();

            Dictionary<string, string> envVars = BuildEnvironmentVariables();

            Log.LogMessage(MessageImportance.High, $"Executing: vpk {string.Join(" ", args)}");

            var exitCode = await toolRunner.RunVpk(args, envVars, cancellationToken)
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

    protected abstract string[] BuildArguments();

    protected virtual Dictionary<string, string> BuildEnvironmentVariables()
    {
        return [];
    }
}
