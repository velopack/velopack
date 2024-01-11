namespace Divergic.Logging.Xunit
{
    using Microsoft.Extensions.Logging;

    /// <summary>
    ///     The <see cref="ICacheLogger" />
    ///     interface defines the members for recording and accessing log entries.
    /// </summary>
    /// <typeparam name="T">The type of class using the cache.</typeparam>
    public interface ICacheLogger<out T> : ICacheLogger, ILogger<T>
    {
    }
}