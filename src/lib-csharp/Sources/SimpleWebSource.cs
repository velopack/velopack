using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Logging;
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
        /// <summary> The timeout for http requests, in minutes. </summary>
        public double Timeout { get; set; }

        /// <summary> The URL of the server hosting packages to update to. </summary>
        public virtual Uri BaseUri { get; }

        /// <summary> The <see cref="IFileDownloader"/> to be used for performing http requests. </summary>
        public virtual IFileDownloader Downloader { get; }

        /// <inheritdoc cref="SimpleWebSource" />
        public SimpleWebSource(string baseUrl, IFileDownloader? downloader = null, double timeout = 30)
            : this(new Uri(baseUrl), downloader, timeout)
        { }

        /// <inheritdoc cref="SimpleWebSource" />
        public SimpleWebSource(Uri baseUri, IFileDownloader? downloader = null, double timeout = 30)
        {
            BaseUri = baseUri;
            Downloader = downloader ?? HttpUtil.CreateDefaultDownloader();
            Timeout = timeout;
        }

        /// <inheritdoc />
        public async virtual Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null,
            VelopackAsset? latestLocalRelease = null)
        {
            var releaseFilename = CoreUtil.GetVeloReleaseIndexName(channel);
            var uri = HttpUtil.AppendPathToUri(BaseUri, releaseFilename);
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

            var uriAndQuery = HttpUtil.AddQueryParamsToUri(uri, args);

            logger.Info($"Downloading release file '{releaseFilename}' from '{uriAndQuery}'.");

            var json = await Downloader.DownloadString(uriAndQuery.ToString(), timeout: Timeout).ConfigureAwait(false);
            return VelopackAssetFeed.FromJson(json);
        }

        /// <inheritdoc />
        public async virtual Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken)
        {
            if (releaseEntry == null) throw new ArgumentNullException(nameof(releaseEntry));
            if (localFile == null) throw new ArgumentNullException(nameof(localFile));

            // releaseUri can be a relative url (eg. "MyPackage.nupkg") or it can be an 
            // absolute url (eg. "https://example.com/MyPackage.nupkg"). In the former case
            var sourceBaseUri = HttpUtil.EnsureTrailingSlash(BaseUri);

            var source = HttpUtil.IsHttpUrl(releaseEntry.FileName)
                ? releaseEntry.FileName
                : HttpUtil.AppendPathToUri(sourceBaseUri, releaseEntry.FileName).ToString();

            logger.Info($"Downloading '{releaseEntry.FileName}' from '{source}'.");
            await Downloader.DownloadFile(source, localFile, progress, timeout: Timeout, cancelToken: cancelToken).ConfigureAwait(false);
        }
    }
}
