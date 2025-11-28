using Microsoft.Build.Framework;

namespace Velopack.Build;

public abstract class VpkTask : MSBuildAsyncTask
{
    protected sealed override async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        try {
            var toolRunner = new VpkToolRunner(Log);
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

    protected abstract string GetSuccesMessage();

    protected abstract string[] BuildArguments();

    protected virtual Dictionary<string, string> BuildEnvironmentVariables()
    {
        return [];
    }
}
