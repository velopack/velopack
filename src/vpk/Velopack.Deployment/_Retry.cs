using Microsoft.Extensions.Logging;
using Velopack.Core;

namespace Velopack.Deployment;

public static class Retry
{
    public static async Task<T> RetryAsyncRet<T>(ILogger log, Func<Task<T>> block, string message, int maxRetries = 1)
    {
        int ctry = 0;
        while (true) {
            try {
                log.Info((ctry > 0 ? $"(retry {ctry}) " : "") + message);
                return await block().ConfigureAwait(false);
            } catch (Exception ex) {
                if (ctry++ > maxRetries) {
                    log.Error(ex.Message + ", will not try again.");
                    throw;
                }

                log.Error($"{ex.Message}, retrying in 1 second.");
                await Task.Delay(1000).ConfigureAwait(false);
            }
        }
    }

    public static Task RetryAsync(ILogger log, Func<Task> block, string message, int maxRetries = 1)
    {
        return RetryAsyncRet<object>(
            log,
            async () => {
                await block().ConfigureAwait(false);
                return null;
            },
            message,
            maxRetries);
    }
}
