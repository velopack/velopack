using Microsoft.Extensions.Logging;
using Velopack.Core.Abstractions;

namespace Velopack.Core;

public class LoggerConsole(ILogger log) : IFancyConsole, IFancyConsoleProgress
{
    public async Task ExecuteProgressAsync(Func<IFancyConsoleProgress, Task> action)
    {
        await action(this).ConfigureAwait(false);
    }

    public async Task RunTask(string name, Func<Action<int>, Task> fn)
    {
        try {
            await fn(x => { }).ConfigureAwait(false);
        } catch (Exception ex) {
            log.LogError(ex, "Error running task {taskName}", name);
            throw;
        }
    }

    public async Task<T> RunTask<T>(string name, Func<Action<int>, Task<T>> fn)
    {
        try {
            return await fn(x => { }).ConfigureAwait(false);
        } catch (Exception ex) {
            log.LogError(ex, "Error running task {taskName}", name);
            throw;
        }
    }

    public void WriteTable(string tableName, IEnumerable<IEnumerable<string>> rows, bool hasHeaderRow = true)
    {
        log.LogInformation(tableName);
        foreach (var row in rows) {
            log.LogInformation("  " + String.Join("    ", row));
        }
    }

    public Task<bool> PromptYesNo(string prompt, bool? defaultValue = null, TimeSpan? timeout = null)
    {
        return Task.FromResult(true);
    }

    public void WriteLine(string text = "")
    {
        log.LogInformation(text);
    }

    public string EscapeMarkup(string text)
    {
        return text;
    }
}