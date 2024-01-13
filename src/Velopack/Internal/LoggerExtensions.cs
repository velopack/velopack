using System;
using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;

namespace Velopack
{
    [ExcludeFromCodeCoverage]
    internal static class LoggerExtensions
    {
        public static void Trace(this ILogger logger, string message)
        {
            logger.LogTrace(message);
        }

        public static void Trace(this ILogger logger, Exception ex, string message)
        {
            logger.LogTrace(ex, message);
        }
        public static void Trace(this ILogger logger, Exception ex)
        {
            logger.LogTrace(ex, ex.Message);
        }

        public static void Debug(this ILogger logger, string message)
        {
            logger.LogDebug(message);
        }

        public static void Debug(this ILogger logger, Exception ex, string message)
        {
            logger.LogDebug(ex, message);
        }

        public static void Debug(this ILogger logger, Exception ex)
        {
            logger.LogDebug(ex, ex.Message);
        }

        public static void Info(this ILogger logger, string message)
        {
            logger.LogInformation(message);
        }

        public static void Info(this ILogger logger, Exception ex, string message)
        {
            logger.LogInformation(ex, message);
        }

        public static void Info(this ILogger logger, Exception ex)
        {
            logger.LogInformation(ex, ex.Message);
        }

        public static void Warn(this ILogger logger, string message)
        {
            logger.LogWarning(message);
        }

        public static void Warn(this ILogger logger, Exception ex, string message)
        {
            logger.LogWarning(ex, message);
        }

        public static void Warn(this ILogger logger, Exception ex)
        {
            logger.LogWarning(ex, ex.Message);
        }

        public static void Error(this ILogger logger, string message)
        {
            logger.LogError(message);
        }

        public static void Error(this ILogger logger, Exception ex, string message)
        {
            logger.LogError(ex, message);
        }

        public static void Error(this ILogger logger, Exception ex)
        {
            logger.LogError(ex, ex.Message);
        }

        public static void Fatal(this ILogger logger, string message)
        {
            logger.LogCritical(message);
        }

        public static void Fatal(this ILogger logger, Exception ex, string message)
        {
            logger.LogCritical(ex, message);
        }

        public static void Fatal(this ILogger logger, Exception ex)
        {
            logger.LogCritical(ex, ex.Message);
        }
    }
}