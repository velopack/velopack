using System;
using System.Diagnostics.CodeAnalysis;

namespace Velopack.Exceptions
{
    /// <summary>
    /// Thrown when an operation can not be performed in an application that is not installed.
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class NotInstalledException : Exception
    {
        private const string DEFAULT_MESSAGE =
            "This operation can not be performed in an application that is not installed. Please install the application and try again.";

        internal NotInstalledException() : base(DEFAULT_MESSAGE) { }
        internal NotInstalledException(Exception innerException) : base(DEFAULT_MESSAGE, innerException) { }
        internal NotInstalledException(string message) : base(message) { }
        internal NotInstalledException(string message, Exception innerException) : base(message, innerException) { }
    }
}