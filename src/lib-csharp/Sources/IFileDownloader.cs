using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Velopack.Sources
{
    /// <summary>
    /// A simple abstractable file downloader
    /// </summary>
    public interface IFileDownloader
    {
        /// <summary>
        /// Downloads a remote file to the specified local path
        /// </summary>
        /// <param name="url">The url which will be downloaded.</param>
        /// <param name="targetFile">
        ///     The local path where the file will be stored
        ///     If a file exists at this path, it will be overwritten.</param>
        /// <param name="progress">
        ///     A delegate for reporting download progress, with expected values from 0-100.
        /// </param>
        /// <param name="headers">Headers that can be passed to Http Downloader, e.g. Accept or Authorization.</param>
        /// <param name="timeout">
        ///     The maximum time in minutes to wait for the download to complete.
        /// </param>
        /// <param name="cancelToken">Optional token to cancel the request.</param>
        Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers = null, double timeout = 30, CancellationToken cancelToken = default);

        /// <summary>
        /// Returns a byte array containing the contents of the file at the specified url
        /// </summary>
        Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers = null, double timeout = 30);

        /// <summary>
        /// Returns a string containing the contents of the specified url
        /// </summary>
        Task<string> DownloadString(string url, IDictionary<string, string>? headers = null, double timeout = 30);
    }
}
