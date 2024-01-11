// ReSharper disable once CheckNamespace
namespace Microsoft.Extensions.Logging
{
    using System;
    using Divergic.Logging.Xunit;
    using Xunit.Abstractions;

    /// <summary>
    ///     The <see cref="LoggingBuilderExtensions" />
    ///     class provides extension methods for the <see cref="ILoggingBuilder" /> interface.
    /// </summary>
    public static class LoggingBuilderExtensions
    {
        /// <summary>
        ///     Adds a logger to writes to the xUnit test output to the specified logging builder.
        /// </summary>
        /// <param name="builder">The logging builder.</param>
        /// <param name="output">The xUnit test output helper.</param>
        /// <param name="config">Optional logging configuration.</param>
        public static void AddXunit(this ILoggingBuilder builder, ITestOutputHelper output,
            LoggingConfig? config = null)
        {
            builder = builder ?? throw new ArgumentNullException(nameof(builder));
            output = output ?? throw new ArgumentNullException(nameof(output));

            // Object is added as a provider to the builder and cannot be disposed of here
#pragma warning disable CA2000 // Dispose objects before losing scope
            var provider = new TestOutputLoggerProvider(output, config);
#pragma warning restore CA2000 // Dispose objects before losing scope

            builder.AddProvider(provider);
        }
    }
}