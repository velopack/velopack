using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Extensions.Logging;
using Velopack.Core.Abstractions;
using ILogger = Microsoft.Extensions.Logging.ILogger;
using Task = System.Threading.Tasks.Task;

namespace Velopack.Build;

public class MSBuildLogger(TaskLoggingHelper loggingHelper) : ILogger
{
    private TaskLoggingHelper LoggingHelper { get; } = loggingHelper;

    IDisposable ILogger.BeginScope<TState>(TState state)
    {
        throw new NotImplementedException();
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
}