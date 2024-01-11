namespace Microsoft.Extensions.Logging
{
    using System;
    using Divergic.Logging.Xunit;
    using Xunit.Abstractions;

    /// <summary>
    ///     The <see cref="LoggerFactoryExtensions" />
    ///     class provides extension methods for configuring <see cref="ILoggerFactory" /> with providers.
    /// </summary>
    public static class LoggerFactoryExtensions
    {
        /// <summary>
        ///     Registers the <see cref="TestOutputLoggerProvider" /> in the factory using the specified
        ///     <see cref="ITestOutputHelper" />.
        /// </summary>
        /// <param name="factory">The factory to add the provider to.</param>
        /// <param name="output">The test output reference.</param>
        /// <param name="config">Optional logging configuration.</param>
        /// <returns>The logger factory.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="factory" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentNullException">The <paramref name="output" /> is <c>null</c>.</exception>
        public static ILoggerFactory AddXunit(this ILoggerFactory factory, ITestOutputHelper output,
            LoggingConfig? config = null)
        {
            factory = factory ?? throw new ArgumentNullException(nameof(factory));
            output = output ?? throw new ArgumentNullException(nameof(output));

#pragma warning disable CA2000 // Dispose objects before losing scope
            var provider = new TestOutputLoggerProvider(output, config);
#pragma warning restore CA2000 // Dispose objects before losing scope

#pragma warning disable CA1062 // Validate arguments of public methods
            factory.AddProvider(provider);
#pragma warning restore CA1062 // Validate arguments of public methods

            return factory;
        }
    }
}