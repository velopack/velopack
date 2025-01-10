using System;
using System.Diagnostics.CodeAnalysis;

namespace Velopack.Exceptions
{
    /// <summary>
    /// Thrown when an exclusive lock for an application cannot be acquired. Usually this means another
    /// instance of the application is running Velopack operations and the current instance cannot proceed.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class AcquireLockFailedException : Exception
    {
        private const string DEFAULT_MESSAGE = "Failed to acquire exclusive lock file. Is another operation currently running?";

        internal AcquireLockFailedException() : base(DEFAULT_MESSAGE) { }
        internal AcquireLockFailedException(Exception innerException) : base(DEFAULT_MESSAGE, innerException) { }
        internal AcquireLockFailedException(string message) : base(message) { }
        internal AcquireLockFailedException(string message, Exception innerException) : base(message, innerException) { }
    }
}