namespace Divergic.Logging.Xunit
{
    using System;
    using System.Collections.Concurrent;
    using System.Diagnostics;
    using System.Threading;
    using global::Xunit.Abstractions;
    using Microsoft.Extensions.Logging;

    /// <summary>
    ///     The <see cref="TestOutputLogger" />
    ///     class is used to provide logging implementation for Xunit.
    /// </summary>
    public class TestOutputLogger : FilterLogger
    {
        private static readonly AsyncLocal<ConcurrentStack<ScopeWriter>> _scopes =
            new AsyncLocal<ConcurrentStack<ScopeWriter>>();

        private readonly LoggingConfig _config;
        private readonly string _categoryName;
        private readonly ITestOutputHelper _output;

        /// <summary>
        ///     Creates a new instance of the <see cref="TestOutputLogger" /> class.
        /// </summary>
        /// <param name="categoryName">The category name of the logger.</param>
        /// <param name="output">The test output helper.</param>
        /// <param name="config">Optional logging configuration.</param>
        /// <exception cref="ArgumentException">The <paramref name="categoryName" /> is <c>null</c>, empty or whitespace.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="output" /> is <c>null</c>.</exception>
        public TestOutputLogger(string categoryName, ITestOutputHelper output, LoggingConfig? config = null)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                throw new ArgumentException("No name value has been supplied", nameof(categoryName));
            }
            
            _categoryName = categoryName;
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _config = config ?? new LoggingConfig();
        }

        /// <inheritdoc />
        public override IDisposable BeginScope<TState>(TState state)
        {
            var scopeWriter = new ScopeWriter(_output, state, Scopes.Count, _categoryName, () => Scopes.TryPop(out _), _config);

            Scopes.Push(scopeWriter);

            return scopeWriter;
        }

        /// <inheritdoc />
        public override bool IsEnabled(LogLevel logLevel)
        {
            if (logLevel == LogLevel.None)
            {
                return false;
            }

            return logLevel >= _config.LogLevel;
        }

        /// <inheritdoc />
        protected override void WriteLogEntry<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            string message,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            try
            {
                WriteLog(logLevel, eventId, message, exception);
            }
            catch (InvalidOperationException)
            {
                if (_config.IgnoreTestBoundaryException == false)
                {
                    throw;
                }
            }
        }

        private void WriteLog(LogLevel logLevel, EventId eventId, string message, Exception? exception)
        {
            var formattedMessage = _config.Formatter.Format(Scopes.Count, _categoryName, logLevel, eventId, message, exception);

            _output.WriteLine(formattedMessage);

            // Write the message to the output window
            Trace.WriteLine(formattedMessage);
        }

        private static ConcurrentStack<ScopeWriter> Scopes
        {
            get
            {
                var scopes = _scopes.Value;

                if (scopes == null)
                {
                    scopes = new ConcurrentStack<ScopeWriter>();

                    _scopes.Value = scopes;
                }

                return scopes;
            }
        }
    }
}