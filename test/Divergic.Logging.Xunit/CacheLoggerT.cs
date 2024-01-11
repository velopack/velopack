namespace Divergic.Logging.Xunit
{
    using System;
    using Microsoft.Extensions.Logging;

    /// <summary>
    ///     The <see cref="CacheLogger{T}" />
    ///     class provides a cache logger for <see cref="ILogger{TCategoryName}" />.
    /// </summary>
    /// <typeparam name="T">The generic type of logger.</typeparam>
    public class CacheLogger<T> : CacheLogger, ICacheLogger<T>
    {
        /// <summary>
        ///     Creates a new instance of the <see cref="CacheLogger{T}" /> class.
        /// </summary>
        public CacheLogger()
        {
        }

        /// <summary>
        ///     Creates a new instance of the <see cref="CacheLogger{T}" /> class.
        /// </summary>
        /// <param name="logger">The source logger.</param>
        /// <exception cref="ArgumentNullException">The <paramref name="logger" /> is <c>null</c>.</exception>
        public CacheLogger(ILogger logger)
            : base(logger)
        {
        }

        internal CacheLogger(ILogger logger, ILoggerFactory factory)
            : base(logger, factory)
        {
        }
    }
}