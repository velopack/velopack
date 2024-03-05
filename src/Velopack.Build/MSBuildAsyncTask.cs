using System;
using System.Threading.Tasks;
using MSBuildTask = Microsoft.Build.Utilities.Task;

namespace Velopack.Build;

public abstract class MSBuildAsyncTask : MSBuildTask
{
    protected MSBuildLogger Logger { get; }

    protected MSBuildAsyncTask()
    {
        Logger = new MSBuildLogger(Log);
    }

    public sealed override bool Execute()
    {
        try {
            return Task.Run(ExecuteAsync).Result;
        } catch (AggregateException ex) {
            ex.Flatten().Handle((x) => {
                Log.LogError(x.Message);
                return true;
            });
            return false;
        }
    }

    protected abstract Task<bool> ExecuteAsync();
}
