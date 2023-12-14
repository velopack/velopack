using Microsoft.Extensions.Logging;
using INugetLogger = NuGet.Common.ILogger;
using NugetLogLevel = NuGet.Common.LogLevel;
using INugetLogMessage = NuGet.Common.ILogMessage;

namespace Squirrel.Packaging;

public class NugetLoggingWrapper : INugetLogger
{
    private readonly ILogger _logger;

    public NugetLoggingWrapper(ILogger logger)
    {
        _logger = logger;
    }

    private LogLevel ToMsLevel(NugetLogLevel level)
    {
        return level switch {
            NugetLogLevel.Debug => LogLevel.Debug,
            NugetLogLevel.Error => LogLevel.Error,
            NugetLogLevel.Information => LogLevel.Information,
            NugetLogLevel.Minimal => LogLevel.Information,
            NugetLogLevel.Verbose => LogLevel.Information,
            NugetLogLevel.Warning => LogLevel.Warning,
            _ => LogLevel.Information,
        };
    }

    public void Log(NugetLogLevel level, string data)
    {
        _logger.Log(ToMsLevel(level), data);
    }

    public void Log(INugetLogMessage message)
    {
        _logger.Log(ToMsLevel(message.Level), message.Message);
    }

    public Task LogAsync(NugetLogLevel level, string data)
    {
        _logger.Log(ToMsLevel(level), data);
        return Task.CompletedTask;
    }

    public Task LogAsync(INugetLogMessage message)
    {
        _logger.Log(ToMsLevel(message.Level), message.Message);
        return Task.CompletedTask;
    }

    public void LogDebug(string data)
    {
        _logger.LogDebug(data);
    }

    public void LogError(string data)
    {
        _logger.LogError(data);
    }

    public void LogInformation(string data)
    {
        _logger.LogInformation(data);
    }

    public void LogInformationSummary(string data)
    {
        _logger.LogInformation(data);
    }

    public void LogMinimal(string data)
    {
        _logger.LogInformation(data);
    }

    public void LogVerbose(string data)
    {
        _logger.LogInformation(data);
    }

    public void LogWarning(string data)
    {
        _logger.LogWarning(data);
    }
}
