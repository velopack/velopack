using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Util;
using Velopack.Logging;

namespace Velopack.Sources
{
    /// <summary>
    /// Retrieves updates from the hosted Velopack service.
    /// </summary>
    public sealed class VelopackFlowSource : IUpdateSource
    {
        /// <inheritdoc cref="SimpleWebSource" />
        public VelopackFlowSource(
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
        public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null,
            VelopackAsset? latestLocalRelease = null)
        {
            if (appId is null) return new VelopackAssetFeed(); // without an appId, we can't get any releases.

            Uri baseUri = new(BaseUri, $"v1.0/manifest/");
            Uri uri = HttpUtil.AppendPathToUri(baseUri, $"{appId}/{channel}");
            var args = new Dictionary<string, string>();

            if (VelopackRuntimeInfo.SystemArch != RuntimeCpu.Unknown) {
                args.Add("arch", VelopackRuntimeInfo.SystemArch.ToString());
            }

            if (VelopackRuntimeInfo.SystemOs != RuntimeOs.Unknown) {
                args.Add("os", VelopackRuntimeInfo.SystemOs.GetOsShortName());
                args.Add("rid", VelopackRuntimeInfo.SystemRid);
            }

            if (latestLocalRelease?.Version != null) {
                args.Add("localVersion", latestLocalRelease.Version.ToString());
            }

            if (stagingId != null) {
                args.Add("stagingId", stagingId.Value.ToString());
            }

            var uriAndQuery = HttpUtil.AddQueryParamsToUri(uri, args);

            logger.LogInformation($"Downloading releases from '{uriAndQuery}'.");

            var json = await Downloader.DownloadString(uriAndQuery.ToString()).ConfigureAwait(false);

            var releaseAssets = CompiledJson.DeserializeVelopackFlowAssetArray(json);
            if (releaseAssets is null) {
                return new VelopackAssetFeed();
            }

            return new VelopackAssetFeed() {
                Assets = releaseAssets.Cast<VelopackAsset>().ToArray()
            };
        }

        /// <inheritdoc />
        public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress,
            CancellationToken cancelToken = default)
        {
            if (releaseEntry is null) throw new ArgumentNullException(nameof(releaseEntry));
            if (releaseEntry is not VelopackFlowReleaseAsset velopackRelease) {
                throw new ArgumentException(
                    $"Expected {nameof(releaseEntry)} to be {nameof(VelopackFlowReleaseAsset)} but was {releaseEntry.GetType().FullName}");
            }

            if (localFile is null) throw new ArgumentNullException(nameof(localFile));

            Uri sourceBaseUri = HttpUtil.EnsureTrailingSlash(BaseUri);

            Uri downloadUri = new(sourceBaseUri, $"v1.0/download/{velopackRelease.Id}");

            logger.LogInformation($"Downloading '{releaseEntry.FileName}' from '{downloadUri}'.");
            await Downloader.DownloadFile(downloadUri.AbsoluteUri, localFile, progress, cancelToken: cancelToken).ConfigureAwait(false);
        }
    }

    internal record VelopackFlowReleaseAsset : VelopackAsset
    {
        public string? Id { get; set; }
    }
}