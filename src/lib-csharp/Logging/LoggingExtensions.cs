#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member

using System;
using System.Diagnostics.CodeAnalysis;

namespace Velopack.Logging
{
    [ExcludeFromCodeCoverage]
    public static class LoggerExtensions
    {
        public static void Log(this IVelopackLogger logger, VelopackLogLevel logLevel, string message)
        {
            logger.Log(logLevel, message, null);
        }

        public static void LogTrace(this IVelopackLogger logger, string message)
        {
            logger.Log(VelopackLogLevel.Trace, message, null);
        }

        public static void LogTrace(this IVelopackLogger logger, Exception ex, string message)
        {
            logger.Log(VelopackLogLevel.Trace, message, ex);
        }

        public static void LogDebug(this IVelopackLogger logger, string message)
        {
            logger.Log(VelopackLogLevel.Debug, message, null);
        }

        public static void LogDebug(this IVelopackLogger logger, Exception ex, string message)
        {
            logger.Log(VelopackLogLevel.Debug, message, ex);
        }

        public static void LogInformation(this IVelopackLogger logger, string message)
        {
            logger.Log(VelopackLogLevel.Information, message, null);
        }

        public static void LogInformation(this IVelopackLogger logger, Exception ex, string message)
        {
            logger.Log(VelopackLogLevel.Information, message, ex);
        }

        public static void LogWarning(this IVelopackLogger logger, string message)
        {
            logger.Log(VelopackLogLevel.Warning, message, null);
        }

        public static void LogWarning(this IVelopackLogger logger, Exception ex, string message)
        {
            logger.Log(VelopackLogLevel.Warning, message, ex);
        }

        public static void LogError(this IVelopackLogger logger, string message)
        {
            logger.Log(VelopackLogLevel.Error, message, null);
        }

        public static void LogError(this IVelopackLogger logger, Exception ex, string message)
        {
            logger.Log(VelopackLogLevel.Error, message, ex);
        }

        public static void LogCritical(this IVelopackLogger logger, string message)
        {
            logger.Log(VelopackLogLevel.Critical, message, null);
        }

        public static void LogCritical(this IVelopackLogger logger, Exception ex, string message)
        {
            logger.Log(VelopackLogLevel.Critical, message, ex);
        }

        public static void Trace(this IVelopackLogger logger, string message)
        {
            logger.LogTrace(message);
        }

        public static void Trace(this IVelopackLogger logger, Exception ex, string message)
        {
            logger.LogTrace(ex, message);
        }

        public static void Trace(this IVelopackLogger logger, Exception ex)
        {
            logger.LogTrace(ex, ex.Message);
        }

        public static void Debug(this IVelopackLogger logger, string message)
        {
            logger.LogDebug(message);
        }

        public static void Debug(this IVelopackLogger logger, Exception ex, string message)
        {
            logger.LogDebug(ex, message);
        }

        public static void Debug(this IVelopackLogger logger, Exception ex)
        {
            logger.LogDebug(ex, ex.Message);
        }

        public static void Info(this IVelopackLogger logger, string message)
        {
            logger.LogInformation(message);
        }

        public static void Info(this IVelopackLogger logger, Exception ex, string message)
        {
            logger.LogInformation(ex, message);
        }

        public static void Info(this IVelopackLogger logger, Exception ex)
        {
            logger.LogInformation(ex, ex.Message);
        }

        public static void Warn(this IVelopackLogger logger, string message)
        {
            logger.LogWarning(message);
        }

        public static void Warn(this IVelopackLogger logger, Exception ex, string message)
        {
            logger.LogWarning(ex, message);
        }

        public static void Warn(this IVelopackLogger logger, Exception ex)
        {
            logger.LogWarning(ex, ex.Message);
        }

        public static void Error(this IVelopackLogger logger, string message)
        {
            logger.LogError(message);
        }

        public static void Error(this IVelopackLogger logger, Exception ex, string message)
        {
            logger.LogError(ex, message);
        }

        public static void Error(this IVelopackLogger logger, Exception ex)
        {
            logger.LogError(ex, ex.Message);
        }

        public static void Fatal(this IVelopackLogger logger, string message)
        {
            logger.LogCritical(message);
        }

        public static void Fatal(this IVelopackLogger logger, Exception ex, string message)
        {
            logger.LogCritical(ex, message);
        }

        public static void Fatal(this IVelopackLogger logger, Exception ex)
        {
            logger.LogCritical(ex, ex.Message);
        }
    }
}