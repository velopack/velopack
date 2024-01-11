namespace Divergic.Logging.Xunit
{
    using System;
    using global::Xunit.Abstractions;
    using Microsoft.Extensions.Logging;

    /// <summary>
    ///     The <see cref="TestOutputLoggerProvider" /> class is used to provide Xunit logging to <see cref="ILoggerFactory" />
    ///     .
    /// </summary>
    public sealed class TestOutputLoggerProvider : ILoggerProvider
    {
        private readonly LoggingConfig? _config;
        private readonly ITestOutputHelper _output;

        /// <summary>
        ///     Initializes a new instance of the <see cref="TestOutputLoggerProvider" /> class.
        /// </summary>
        /// <param name="output">The test output helper.</param>
        /// <param name="config">Optional logging configuration.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="output" /> is <c>null</c>.</exception>
        public TestOutputLoggerProvider(ITestOutputHelper output, LoggingConfig? config = null)
        {
            _output = output ?? throw new ArgumentNullException(nameof(output));
            _config = config;
        }

        /// <inheritdoc />
        /// <exception cref="ArgumentException">The <paramref name="categoryName" /> is <c>null</c>, empty or whitespace.</exception>
        public ILogger CreateLogger(string categoryName)
        {
            if (string.IsNullOrWhiteSpace(categoryName))
            {
                throw new ArgumentException("No categoryName value has been supplied", nameof(categoryName));
            }

            return new TestOutputLogger(categoryName, _output, _config);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            // no-op
        }
    }
}