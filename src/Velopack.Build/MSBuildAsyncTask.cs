using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Velopack.Build;

public abstract class MSBuildAsyncTask : MSBuildTask, ICancelableTask
{
    protected MSBuildLogger Logger { get; }

    private CancellationTokenSource CancellationTokenSource { get; } = new();

    protected MSBuildAsyncTask()
    {
        Logger = new MSBuildLogger(Log);
    }

    public sealed override bool Execute()
    {
        CancellationToken token = CancellationTokenSource.Token;
        try {
            return Task.Run(async () => {
                try {
                    return await ExecuteAsync(token).ConfigureAwait(false);
                } catch (OperationCanceledException) {
                    return false;
                } catch (Exception ex) {
                    Log.LogErrorFromException(ex, true, true, null);
                    return false;
                }
            }, token).Result;
        } catch (AggregateException ex) {
            ex.Flatten().Handle((x) => {
                Log.LogError(x.Message);
                return true;
            });
            return false;
        }
    }

    protected abstract Task<bool> ExecuteAsync(CancellationToken cancellationToken);

    public void Cancel() => CancellationTokenSource.Cancel();
}
