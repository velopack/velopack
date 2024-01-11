namespace Divergic.Logging.Xunit
{
    using System;
    using System.Collections.Generic;
    using System.Globalization;
    using Microsoft.Extensions.Logging;

    /// <summary>
    ///     The <see cref="DefaultFormatter" />
    ///     class provides the default formatting of log messages for xUnit test output.
    /// </summary>
    public class DefaultFormatter : ILogFormatter
    {
        private readonly LoggingConfig _config;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DefaultFormatter" /> class.
        /// </summary>
        /// <param name="config">The logging configuration.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="config" /> value is <c>null</c>.</exception>
        public DefaultFormatter(LoggingConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
        }

        /// <inheritdoc />
        public virtual string Format(
            int scopeLevel,
            string categoryName,
            LogLevel logLevel,
            EventId eventId,
            string message,
            Exception? exception)
        {
            var padding = new string(' ', scopeLevel * _config.ScopePaddingSpaces);
            var parts = new List<string>(2);

            if (string.IsNullOrWhiteSpace(message) == false)
            {
                var part = string.Format(CultureInfo.InvariantCulture, FormatMask, padding, logLevel, eventId.Id,
                    message);

                part = MaskSensitiveValues(part);

                parts.Add(part);
            }

            if (exception != null)
            {
                var part = string.Format(
                    CultureInfo.InvariantCulture,
                    FormatMask,
                    padding,
                    logLevel,
                    eventId.Id,
                    exception);

                part = MaskSensitiveValues(part);

                parts.Add(part);
            }

            return string.Join(Environment.NewLine, parts);
        }

        private string MaskSensitiveValues(string value)
        {
            const string mask = "****";

            for (var index = 0; index < _config.SensitiveValues.Count; index++)
            {
                var sensitiveValue = _config.SensitiveValues[index];

                value = value.Replace(sensitiveValue, mask);
            }

            return value;
        }

        /// <summary>
        /// Returns the string format mask used to generate a log message.
        /// </summary>
        /// <remarks>The format values are:
        /// <ul>
        ///     <li>0: Padding</li>
        ///     <li>1: Level</li>
        ///     <li>2: Event Id</li>
        ///     <li>3: Message</li>
        /// </ul>
        /// </remarks>
        protected virtual string FormatMask { get; } = "{0}{1} [{2}]: {3}";
    }
}