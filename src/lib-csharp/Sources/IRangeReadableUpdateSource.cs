using System;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Logging;

namespace Velopack.Sources
{
    /// <summary>
    /// Optional capability of an <see cref="IUpdateSource"/>: byte-range access to a release asset. Sources which
    /// implement this interface allow <see cref="UpdateManager"/> to download only the parts of a package which
    /// differ from the installed application (chunk-based updates). Sources without it always download the whole package.
    /// </summary>
    public interface IRangeReadableUpdateSource
    {
        /// <summary>
        /// Opens a ranged reader for the specified asset. Returns null when ranged access is not available for this
        /// asset (the caller then downloads the full package).
        /// </summary>
        /// <param name="logger">The logger to use for any diagnostic messages.</param>
        /// <param name="asset">The release asset to read.</param>
        /// <param name="cancelToken">A token to use to cancel the request.</param>
        Task<IRangeReader?> OpenRangeReaderAsync(IVelopackLogger logger, VelopackAsset asset, CancellationToken cancelToken);
    }

    /// <summary>
    /// Byte-range access to a single remote (or local) file. Implementations may be used concurrently from several
    /// threads; each call performs one independent read.
    /// </summary>
    public interface IRangeReader : IDisposable
    {
        /// <summary>
        /// The length of the asset as reported by the origin, or -1 until the first ranged read has completed.
        /// </summary>
        long Length { get; }

        /// <summary>
        /// Performs one request and fills exactly <paramref name="count"/> bytes of <paramref name="buffer"/> starting at
        /// <paramref name="bufferOffset"/> with the asset bytes at <paramref name="offset"/>. Throws
        /// <see cref="RangeUnavailableException"/> when ranged access cannot work for this asset (the caller then abandons
        /// the chunk path), or any other exception for a transient failure (the caller's retry policy decides).
        /// </summary>
        Task ReadExactAsync(long offset, byte[] buffer, int bufferOffset, int count, CancellationToken cancelToken);
    }

    /// <summary>
    /// Thrown by an <see cref="IRangeReader"/> when ranged access is not possible for the asset (for example the server
    /// ignores the Range header). This is fatal for the chunk update path; the full package is downloaded instead.
    /// </summary>
    public class RangeUnavailableException : Exception
    {
        /// <inheritdoc cref="RangeUnavailableException"/>
        public RangeUnavailableException(string message) : base(message)
        {
        }

        /// <inheritdoc cref="RangeUnavailableException"/>
        public RangeUnavailableException(string message, Exception? innerException) : base(message, innerException)
        {
        }
    }
}
