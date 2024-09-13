using System;
using System.Diagnostics.CodeAnalysis;

namespace Velopack.Compression
{
    /// <summary>
    /// Represents an error that occurs when a package does not match it's expected SHA checksum
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
            : this(filePath, "Checksum failed")
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