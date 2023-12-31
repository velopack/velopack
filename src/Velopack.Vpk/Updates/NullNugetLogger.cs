using NugetLevel = NuGet.Common.LogLevel;
using NugetLogger = NuGet.Common.ILogger;
using NugetMessage = NuGet.Common.ILogMessage;

namespace Squirrel.Csq.Updates;

class NullNugetLogger : NugetLogger
{
    void NugetLogger.LogDebug(string data)
    {
    }

    void NugetLogger.LogVerbose(string data)
    {
    }

    void NugetLogger.LogInformation(string data)
    {
    }

    void NugetLogger.LogMinimal(string data)
    {
    }

    void NugetLogger.LogWarning(string data)
    {
    }

    void NugetLogger.LogError(string data)
    {
    }

    void NugetLogger.LogInformationSummary(string data)
    {
    }

    void NugetLogger.Log(NugetLevel level, string data)
    {
    }

    Task NugetLogger.LogAsync(NugetLevel level, string data)
    {
        return Task.CompletedTask;
    }

    void NugetLogger.Log(NugetMessage message)
    {
    }

    Task NugetLogger.LogAsync(NugetMessage message)
    {
        return Task.CompletedTask;
    }
}