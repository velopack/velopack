using System;
using System.Diagnostics.CodeAnalysis;

namespace Velopack.Compression
{
    /// <summary>
    /// Represents an error that occurs when a package is not properly signed per the optional check
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class SigningCheckFailedException : Exception
    {
        /// <summary>
        /// The filename of the package which failed validation
        /// </summary>
        public string FilePath { get; }

        /// <inheritdoc cref="SigningCheckFailedException"/>
        public SigningCheckFailedException(string filePath)
            : this(filePath, "Signature validation failed")
        {
        }

        /// <inheritdoc cref="SigningCheckFailedException"/>
        public SigningCheckFailedException(string filePath, string message)
            : base(message + $" ({filePath})")
        {
            FilePath = filePath;
        }
    }
}