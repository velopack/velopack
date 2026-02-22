using System.Diagnostics.CodeAnalysis;

namespace Velopack.Core;

/// <summary>
/// Denotes that an error has occurred for which a stack trace should not be printed.
/// </summary>
[ExcludeFromCodeCoverage]
public class UserInfoException : Exception
{
    public UserInfoException()
    {
    }

    public UserInfoException(string message) : base(message)
    {
    }

    public UserInfoException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
