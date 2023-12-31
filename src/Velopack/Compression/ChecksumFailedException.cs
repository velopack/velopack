using System;
using System.Diagnostics.CodeAnalysis;

namespace Squirrel.Compression
{
    /// <summary>
    /// Represents an error that occurs when a package does not match it's expected SHA checksum
    /// </summary>
    [ExcludeFromCodeCoverage]
    public class ChecksumFailedException : Exception
    {
        /// <summary>
        /// The filename of the package which failed validation
        /// </summary>
        public string FilePath { get; }

        /// <inheritdoc cref="ChecksumFailedException"/>
        public ChecksumFailedException(string filePath)
            : this(filePath, "Checksum failed")
        {
        }

        /// <inheritdoc cref="ChecksumFailedException"/>
        public ChecksumFailedException(string filePath, string message)
            : base(message + $" ({filePath})")
        {
            FilePath = filePath;
        }
    }
}
