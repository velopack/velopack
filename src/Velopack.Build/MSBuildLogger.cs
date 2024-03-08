using System;
using System.Collections.Generic;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Extensions.Logging;
using Velopack.Packaging.Abstractions;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Task = System.Threading.Tasks.Task;

namespace Velopack.Build;

public class MSBuildLogger(TaskLoggingHelper loggingHelper) : ILogger, IFancyConsole, IFancyConsoleProgress
{
    private TaskLoggingHelper LoggingHelper { get; } = loggingHelper;

    IDisposable ILogger.BeginScope<TState>(TState state)
    {
        throw new NotImplementedException();
    }

    public async Task ExecuteProgressAsync(Func<IFancyConsoleProgress, Task> action)
    {
        await action(this).ConfigureAwait(false);
    }

    public async Task RunTask(string name, Func<Action<int>, Task> fn)
    {
        try {
            await fn(x => { }).ConfigureAwait(false);
        } catch (Exception ex) {
            this.LogError(ex, "Error running task {0}", name);
        }
    }

    public bool IsEnabled(LogLevel logLevel)
    {
        return logLevel switch {
            LogLevel.Trace => LoggingHelper.LogsMessagesOfImportance(MessageImportance.Low),
            LogLevel.Debug => LoggingHelper.LogsMessagesOfImportance(MessageImportance.Normal),
            LogLevel.Information => LoggingHelper.LogsMessagesOfImportance(MessageImportance.High),
            _ => true,
        };
    }

    public void Log<TState>(LogLevel logLevel, EventId _, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
    {
        string message = formatter(state, exception);
        if (exception != null) {
            message += " " + exception.Message;
        }
        switch (logLevel) {
        case LogLevel.Trace:
            LoggingHelper.LogMessage(MessageImportance.Low, message);
            break;
        case LogLevel.Debug:
            LoggingHelper.LogMessage(MessageImportance.Normal, message);
            break;
        case LogLevel.Information:
            LoggingHelper.LogMessage(MessageImportance.High, message);
            break;
        case LogLevel.Warning:
            LoggingHelper.LogWarning(message);
            break;
        case LogLevel.Error:
        case LogLevel.Critical:
            LoggingHelper.LogError(message);
            break;
        }
    }

    public void WriteTable(string tableName, IEnumerable<IEnumerable<string>> rows, bool hasHeaderRow = true)
    {
        //Do we need this output for MSBuild?
    }

    public System.Threading.Tasks.Task<bool> PromptYesNo(string prompt, bool? defaultValue = null, TimeSpan? timeout = null)
    {
        //TODO: This API is problematic as it assumes interactive.
        return Task.FromResult(true);
    }

    public void WriteLine(string text = "")
    {
        Log(LogLevel.Information, 0, null, null, (object? state, Exception? exception) => text);
    }
}
