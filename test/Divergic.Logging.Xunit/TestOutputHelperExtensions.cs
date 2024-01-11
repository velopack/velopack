// ReSharper disable once CheckNamespace
namespace Xunit.Abstractions
{
    using System;
    using System.Runtime.CompilerServices;
    using Divergic.Logging.Xunit;
    using Microsoft.Extensions.Logging;

    /// <summary>
    ///     The <see cref="TestOutputHelperExtensions" /> class provides extension methods for the
    ///     <see cref="ITestOutputHelper" />.
    /// </summary>
    public static class TestOutputHelperExtensions
    {
        /// <summary>
        ///     Builds a logger from the specified test output helper.
        /// </summary>
        /// <param name="output">The test output helper.</param>
        /// <param name="memberName">
        ///     The member to create the logger for. This is automatically populated using <see cref="CallerMemberNameAttribute" />
        ///     .
        /// </param>
        /// <returns>The logger.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="output" /> is <c>null</c>.</exception>
        public static ICacheLogger BuildLogger(
            this ITestOutputHelper output,
            [CallerMemberName] string memberName = "")
        {
            return BuildLogger(output, null, memberName);
        }

        /// <summary>
        ///     Builds a logger from the specified test output helper.
        /// </summary>
        /// <param name="output">The test output helper.</param>
        /// <param name="logLevel">The minimum log level to output.</param>
        /// <param name="memberName">
        ///     The member to create the logger for. This is automatically populated using <see cref="CallerMemberNameAttribute" />
        ///     .
        /// </param>
        /// <returns>The logger.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="output" /> is <c>null</c>.</exception>
        public static ICacheLogger BuildLogger(
            this ITestOutputHelper output,
            LogLevel logLevel,
            [CallerMemberName] string memberName = "")
        {
            var config = new LoggingConfig
            {
                LogLevel = logLevel
            };

            return BuildLogger(output, config, memberName);
        }

        /// <summary>
        ///     Builds a logger from the specified test output helper.
        /// </summary>
        /// <param name="output">The test output helper.</param>
        /// <param name="config">Optional logging configuration.</param>
        /// <param name="memberName">
        ///     The member to create the logger for. This is automatically populated using <see cref="CallerMemberNameAttribute" />
        ///     .
        /// </param>
        /// <returns>The logger.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="output" /> is <c>null</c>.</exception>
        public static ICacheLogger BuildLogger(
            this ITestOutputHelper output,
            LoggingConfig? config,
            [CallerMemberName] string memberName = "")
        {
            output = output ?? throw new ArgumentNullException(nameof(output));

            // Do not use the using keyword here because the factory will be disposed before the cache logger is finished with it
            var factory = LogFactory.Create(output, config);

            var logger = factory.CreateLogger(memberName);

            return logger.WithCache(factory);
        }

        /// <summary>
        ///     Builds a logger from the specified test output helper for the specified type.
        /// </summary>
        /// <typeparam name="T">The type to create the logger for.</typeparam>
        /// <param name="output">The test output helper.</param>
        /// <returns>The logger.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="output" /> is <c>null</c>.</exception>
        public static ICacheLogger<T> BuildLoggerFor<T>(this ITestOutputHelper output)
        {
            return BuildLoggerFor<T>(output, null);
        }

        /// <summary>
        ///     Builds a logger from the specified test output helper for the specified type.
        /// </summary>
        /// <typeparam name="T">The type to create the logger for.</typeparam>
        /// <param name="output">The test output helper.</param>
        /// <param name="logLevel">The minimum log level to output.</param>
        /// <returns>The logger.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="output" /> is <c>null</c>.</exception>
        public static ICacheLogger<T> BuildLoggerFor<T>(this ITestOutputHelper output, LogLevel logLevel)
        {
            var config = new LoggingConfig
            {
                LogLevel = logLevel
            };

            return BuildLoggerFor<T>(output, config);
        }

        /// <summary>
        ///     Builds a logger from the specified test output helper for the specified type.
        /// </summary>
        /// <typeparam name="T">The type to create the logger for.</typeparam>
        /// <param name="output">The test output helper.</param>
        /// <param name="config">Optional logging configuration.</param>
        /// <returns>The logger.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="output" /> is <c>null</c>.</exception>
        public static ICacheLogger<T> BuildLoggerFor<T>(this ITestOutputHelper output, LoggingConfig? config)
        {
            output = output ?? throw new ArgumentNullException(nameof(output));

            // Do not use the using keyword here because the factory will be disposed before the cache logger is finished with it
            var factory = LogFactory.Create(output, config);

            var logger = factory.CreateLogger<T>();

            return logger.WithCache(factory);
        }
        
        /// <summary>
        ///     Builds a logger factory from the specified test output helper.
        /// </summary>
        /// <param name="output">The test output helper.</param>
        /// <returns>The logger factory.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="output" /> is <c>null</c>.</exception>
        public static ILoggerFactory BuildLoggerFactory(
            this ITestOutputHelper output)
        {
            return BuildLoggerFactory(output, null);
        }

        /// <summary>
        ///     Builds a logger factory from the specified test output helper.
        /// </summary>
        /// <param name="output">The test output helper.</param>
        /// <param name="logLevel">The minimum log level to output.</param>
        /// <returns>The logger factory.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="output" /> is <c>null</c>.</exception>
        public static ILoggerFactory BuildLoggerFactory(
            this ITestOutputHelper output,
            LogLevel logLevel)
        {
            var config = new LoggingConfig
            {
                LogLevel = logLevel
            };

            return BuildLoggerFactory(output, config);
        }

        /// <summary>
        ///     Builds a logger factory from the specified test output helper.
        /// </summary>
        /// <param name="output">The test output helper.</param>
        /// <param name="config">Optional logging configuration.</param>
        /// <returns>The logger factory.</returns>
        /// <exception cref="ArgumentNullException">The <paramref name="output" /> is <c>null</c>.</exception>
        public static ILoggerFactory BuildLoggerFactory(
            this ITestOutputHelper output,
            LoggingConfig? config)
        {
            output = output ?? throw new ArgumentNullException(nameof(output));

            // Do not use the using keyword here because the factory will be disposed before the cache logger is finished with it
            var factory = LogFactory.Create(output, config);

            return factory;
        }

    }
}