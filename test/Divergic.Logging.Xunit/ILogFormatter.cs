namespace Divergic.Logging.Xunit
{
    using System;
    using Microsoft.Extensions.Logging;

    /// <summary>
    ///     The <see cref="ILogFormatter" />
    ///     interface defines the members for formatting log messages.
    /// </summary>
    public interface ILogFormatter
    {
        /// <summary>
        ///     Formats the log message with the specified values.
        /// </summary>
        /// <param name="scopeLevel">The number of active logging scopes.</param>
        /// <param name="categoryName">The logger name.</param>
        /// <param name="logLevel">The log level.</param>
        /// <param name="eventId">The event id.</param>
        /// <param name="message">The log message.</param>
        /// <param name="exception">The exception to be logged.</param>
        /// <returns>The formatted log message.</returns>
        string Format(
            int scopeLevel,
            string categoryName,
            LogLevel logLevel,
            EventId eventId,
            string message,
            Exception? exception);
    }
}