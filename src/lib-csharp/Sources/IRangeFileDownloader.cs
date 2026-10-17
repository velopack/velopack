using System.Collections.Generic;

namespace Velopack.Sources
{
    /// <summary>
    /// Optional capability of an <see cref="IFileDownloader"/>: opening a byte-range reader over a URL.
    /// </summary>
    public interface IRangeFileDownloader
    {
        /// <summary>
        /// Opens a ranged reader over the specified URL. The reader owns its connection pool and must be disposed.
        /// </summary>
        /// <param name="url">The url to read from.</param>
        /// <param name="headers">Headers to send with every request, e.g. Accept or Authorization.</param>
        /// <param name="timeoutMinutes">The maximum time in minutes a single ranged request may take.</param>
        IRangeReader OpenRangeReader(string url, IDictionary<string, string>? headers, double timeoutMinutes);
    }
}
