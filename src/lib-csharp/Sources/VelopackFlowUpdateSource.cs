using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack.Util;
using Velopack.Locators;

namespace Velopack.Sources
{
    /// <summary>
    /// Retrieves updates from the hosted Velopack service.
    /// </summary>
    public sealed class VelopackFlowUpdateSource : IUpdateSource
    {
        /// <inheritdoc cref="SimpleWebSource" />
        public VelopackFlowUpdateSource(
            string baseUri = "https://api.velopack.io/",
            IFileDownloader? downloader = null)
        {
            BaseUri = new Uri(baseUri);
            Downloader = downloader ?? HttpUtil.CreateDefaultDownloader();
        }

        /// <summary> The URL of the server hosting packages to update to. </summary>
        public Uri BaseUri { get; }

        /// <summary> The <see cref="IFileDownloader"/> to be used for performing http requests. </summary>
        public IFileDownloader Downloader { get; }

        /// <inheritdoc />
        public async Task<VelopackAssetFeed> GetReleaseFeed(ILogger logger, string channel, Guid? stagingId = null,
            VelopackAsset? latestLocalRelease = null)
        {
            string? packageId = latestLocalRelease?.PackageId ?? VelopackLocator.GetDefault(logger).AppId;
            if (string.IsNullOrWhiteSpace(packageId)) {
                //Without a package id, we can't get a feed.
                return new VelopackAssetFeed();
            }

            Uri baseUri = new(BaseUri, $"v1.0/manifest/");
            Uri uri = HttpUtil.AppendPathToUri(baseUri, $"{packageId}/{channel}");
            var args = new Dictionary<string, string>();

            if (VelopackRuntimeInfo.SystemArch != RuntimeCpu.Unknown) {
                args.Add("arch", VelopackRuntimeInfo.SystemArch.ToString());
            }

            if (VelopackRuntimeInfo.SystemOs != RuntimeOs.Unknown) {
                args.Add("os", VelopackRuntimeInfo.SystemOs.GetOsShortName());
                args.Add("rid", VelopackRuntimeInfo.SystemRid);
            }

            if (latestLocalRelease != null) {
                args.Add("localVersion", latestLocalRelease.Version.ToString());
            }

            if (stagingId != null) {
                args.Add("stagingId", stagingId.Value.ToString());
            }

            var uriAndQuery = HttpUtil.AddQueryParamsToUri(uri, args);

            logger.LogInformation("Downloading releases from '{Uri}'.", uriAndQuery);

            var json = await Downloader.DownloadString(uriAndQuery.ToString()).ConfigureAwait(false);

            var releaseAssets = CompiledJson.DeserializeVelopackFlowAssetArray(json);
            return new VelopackAssetFeed() {
                Assets = releaseAssets
            };
        }

        /// <inheritdoc />
        public async Task DownloadReleaseEntry(ILogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default)
        {
            if (releaseEntry is null) throw new ArgumentNullException(nameof(releaseEntry));
            if (releaseEntry is not VelopackFlowReleaseAsset velopackRelease) {
                throw new ArgumentException($"Expected {nameof(releaseEntry)} to be {nameof(VelopackFlowReleaseAsset)} but was {releaseEntry.GetType().FullName}");
            }
            if (localFile is null) throw new ArgumentNullException(nameof(localFile));

            Uri sourceBaseUri = HttpUtil.EnsureTrailingSlash(BaseUri);

            Uri downloadUri = new(sourceBaseUri, $"v1.0/download/{velopackRelease.Id}");

            logger.LogInformation("Downloading '{ReleaseFileName}' from '{Uri}'.", releaseEntry.FileName, downloadUri);
            await Downloader.DownloadFile(downloadUri.AbsoluteUri, localFile, progress, cancelToken: cancelToken).ConfigureAwait(false);
        }
    }

    internal record VelopackFlowReleaseAsset : VelopackAsset
    {
        public string? Id { get; set; }
    }
}
