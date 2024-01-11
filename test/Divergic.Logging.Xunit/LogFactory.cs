namespace Divergic.Logging.Xunit
{
    using System;
    using global::Xunit.Abstractions;
    using Microsoft.Extensions.Logging;

    /// <summary>
    ///     The <see cref="LogFactory" />
    ///     class is used to create <see cref="ILogger" /> instances.
    /// </summary>
    public static class LogFactory
    {
        /// <summary>
        ///     Creates an <see cref="ILoggerFactory" /> instance that is configured for xUnit output.
        /// </summary>
        /// <param name="output">The test output.</param>
        /// <param name="config">Optional logging configuration.</param>
        /// <returns>The logger factory.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="output" /> is <c>null</c>.</exception>
        public static ILoggerFactory Create(
            ITestOutputHelper output, LoggingConfig? config = null)
        {
            output = output ?? throw new ArgumentNullException(nameof(output));

            var factory = new LoggerFactory();

            factory.AddXunit(output, config);

            return factory;
        }
    }
}