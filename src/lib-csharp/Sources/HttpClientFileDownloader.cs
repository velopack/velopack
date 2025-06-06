﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;

namespace Velopack.Sources
{
    /// <inheritdoc cref="IFileDownloader"/>
    public class HttpClientFileDownloader : IFileDownloader
    {
        /// <summary>
        /// The User-Agent sent with requests
        /// </summary>
        public static ProductInfoHeaderValue UserAgent => new("Velopack", VelopackRuntimeInfo.VelopackNugetVersion.ToFullString());

        /// <inheritdoc />
        public virtual async Task DownloadFile(string url, string targetFile, Action<int> progress, IDictionary<string, string>? headers, double timeout,
            CancellationToken cancelToken = default)
        {
            using var client = CreateHttpClient(headers, timeout);
            await TryDownloadThenLowercase(
                async (reqUrl) => {
                    using (var fs = File.Open(targetFile, FileMode.Create)) {
                        await DownloadToStreamInternal(client, reqUrl, fs, progress, cancelToken).ConfigureAwait(false);
                    }

                    return true;
                },
                url).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public virtual async Task<byte[]> DownloadBytes(string url, IDictionary<string, string>? headers, double timeout)
        {
            using var client = CreateHttpClient(headers, timeout);
            return await TryDownloadThenLowercase(client.GetByteArrayAsync, url).ConfigureAwait(false);
        }

        /// <inheritdoc />
        public virtual async Task<string> DownloadString(string url, IDictionary<string, string>? headers, double timeout)
        {
            using var client = CreateHttpClient(headers, timeout);
            return await TryDownloadThenLowercase(client.GetStringAsync, url).ConfigureAwait(false);
        }

        /// <summary>
        /// Tries to download a string from the specified url. If it fails, it will attempt to
        /// download the string again with the url lowercased. This is useful for services that
        /// are case-sensitive yet corrupt the case on upload.
        /// </summary>
        protected virtual async Task<T> TryDownloadThenLowercase<T>(Func<string, Task<T>> downloadFunc, string url)
        {
            try {
                return await downloadFunc(url).ConfigureAwait(false);
            } catch {
                try {
                    // NB: Some super brain-dead services are case-sensitive yet 
                    // corrupt case on upload. I can't even.
                    return await downloadFunc(url.ToLower()).ConfigureAwait(false);
                } catch {
                    // we don't want to throw the "fallback" exception
                }

                throw; // rethrow the original exception
            }
        }

        /// <summary>
        /// Asynchronously downloads a remote url to the specified destination stream while 
        /// providing progress updates.
        /// </summary>
        protected virtual async Task DownloadToStreamInternal(HttpClient client, string requestUri, Stream destination, Action<int>? progress = null,
            CancellationToken cancelToken = default)
        {
            // https://stackoverflow.com/a/46497896/184746
            // Get the http headers first to examine the content length
            using var response = await client.GetAsync(requestUri, HttpCompletionOption.ResponseHeadersRead, cancelToken).ConfigureAwait(false);
            response.EnsureSuccessStatusCode();

            var contentLength = response.Content.Headers.ContentLength;
            using var download = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);

            // Ignore progress reporting when no progress reporter was 
            // passed or when the content length is unknown
            if (progress == null || !contentLength.HasValue) {
                await download.CopyToAsync(destination, 81920, cancelToken).ConfigureAwait(false);
                return;
            }

            var buffer = new byte[81920];
            long totalBytesRead = 0;
            int bytesRead;
            int lastProgress = 0;
            while ((bytesRead = await download.ReadAsync(buffer, 0, buffer.Length, cancelToken).ConfigureAwait(false)) != 0) {
                await destination.WriteAsync(buffer, 0, bytesRead, cancelToken).ConfigureAwait(false);
                totalBytesRead += bytesRead;
                cancelToken.ThrowIfCancellationRequested();

                // Convert absolute progress (bytes downloaded) into relative progress (0% - 100%)
                // and don't report progress < 3% difference, kind of like a shitty debounce.
                var curProgress = (int) ((double) totalBytesRead / contentLength.Value * 100);
                if (curProgress - lastProgress >= 3) {
                    lastProgress = curProgress;
                    progress(curProgress);
                }
            }

            if (lastProgress < 100)
                progress(100);
        }

        /// <summary>
        /// Creates a new <see cref="HttpClientHandler"/> with default settings, used for
        /// new <see cref="HttpClient"/>'s. Override this function to add client certificates,
        /// proxy configurations, cookies, or change other http behaviors.
        /// </summary>
        protected virtual HttpClientHandler CreateHttpClientHandler()
        {
            return new HttpClientHandler() {
                AllowAutoRedirect = true,
                MaxAutomaticRedirections = 10,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
            };
        }

        /// <summary>
        /// Creates a new <see cref="HttpClient"/> for every request.
        /// </summary>
        protected virtual HttpClient CreateHttpClient(IDictionary<string, string>? headers, double timeout)
        {
            var client = new HttpClient(CreateHttpClientHandler());
            client.DefaultRequestHeaders.UserAgent.Add(UserAgent);

            foreach (var header in headers ?? new Dictionary<string, string>()) {
                client.DefaultRequestHeaders.Add(header.Key, header.Value);
            }

            client.Timeout = TimeSpan.FromMinutes(timeout);
            return client;
        }
    }
}