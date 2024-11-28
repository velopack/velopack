using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack.Util;

namespace Velopack.Sources
{
    /// <summary>
    /// Retrieves updates from a static file host or other web server. 
    /// Will perform a request for '{baseUri}/RELEASES' to locate the available packages,
    /// and provides query parameters to specify the name of the requested package.
    /// </summary>
    public class SimpleWebSource : IUpdateSource
    {
        /// <summary> The URL of the server hosting packages to update to. </summary>
        public virtual Uri BaseUri { get; }

        /// <summary> The <see cref="IFileDownloader"/> to be used for performing http requests. </summary>
        public virtual IFileDownloader Downloader { get; }
        
        /// <summary> The mode determining how to deal with query string parameters. </summary>
        public QueryMode QueryMode { get; }

        /// <inheritdoc cref="SimpleWebSource" />
        public SimpleWebSource(string baseUrl, IFileDownloader? downloader = null, QueryMode queryMode = QueryMode.ReleaseFeedOnly)
            : this(new Uri(baseUrl), downloader, queryMode)
        { }

        /// <inheritdoc cref="SimpleWebSource" />
        public SimpleWebSource(Uri baseUri, IFileDownloader? downloader = null, QueryMode queryMode = QueryMode.ReleaseFeedOnly)
        {
            BaseUri = baseUri;
            Downloader = downloader ?? HttpUtil.CreateDefaultDownloader();
            QueryMode = queryMode;
        }

        /// <inheritdoc />
        public async virtual Task<VelopackAssetFeed> GetReleaseFeed(ILogger logger, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            var releaseFilename = CoreUtil.GetVeloReleaseIndexName(channel);
            var uri = HttpUtil.AppendPathToUri(BaseUri, releaseFilename);

            if (QueryMode == QueryMode.ReleaseFeedOnly || QueryMode == QueryMode.AllRequests) {
                var args = new Dictionary<string, string>();

                if (VelopackRuntimeInfo.SystemArch != RuntimeCpu.Unknown) {
                    args.Add("arch", VelopackRuntimeInfo.SystemArch.ToString());
                }

                if (VelopackRuntimeInfo.SystemOs != RuntimeOs.Unknown) {
                    args.Add("os", VelopackRuntimeInfo.SystemOs.GetOsShortName());
                    args.Add("rid", VelopackRuntimeInfo.SystemRid);
                }

                if (latestLocalRelease != null) {
                    args.Add("id", latestLocalRelease.PackageId);
                    args.Add("localVersion", latestLocalRelease.Version.ToString());
                }
                uri = HttpUtil.AddQueryParamsToUri(uri, args);
            }

            logger.Info($"Downloading release file '{releaseFilename}' from '{uri}'.");

            var json = await Downloader.DownloadString(uri.ToString()).ConfigureAwait(false);
            return VelopackAssetFeed.FromJson(json);
        }

        /// <inheritdoc />
        public async virtual Task DownloadReleaseEntry(ILogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken)
        {
            if (releaseEntry == null) throw new ArgumentNullException(nameof(releaseEntry));
            if (localFile == null) throw new ArgumentNullException(nameof(localFile));

            // releaseUri can be a relative url (eg. "MyPackage.nupkg") or it can be an 
            // absolute url (eg. "https://example.com/MyPackage.nupkg"). In the former case
            var sourceBaseUri = HttpUtil.EnsureTrailingSlash(BaseUri);

            var source = HttpUtil.IsHttpUrl(releaseEntry.FileName)
                ? new Uri(releaseEntry.FileName)
                : HttpUtil.AppendPathToUri(sourceBaseUri, releaseEntry.FileName);

            if (QueryMode == QueryMode.AllRequests) {
                var args = new Dictionary<string, string>() {
                    { "id", releaseEntry.PackageId }
                };
                source = HttpUtil.AddQueryParamsToUri(source, args);
            }

            logger.Info($"Downloading '{releaseEntry.FileName}' from '{source}'.");
            await Downloader.DownloadFile(source.ToString(), localFile, progress, cancelToken: cancelToken).ConfigureAwait(false);
        }
    }
}
