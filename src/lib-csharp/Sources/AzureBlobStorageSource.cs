using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Logging;
using Velopack.Util;

namespace Velopack.Sources
{
    /// <summary>
    /// Retrieves updates from an Azure Blob Storage container using Shared Key authentication.
    /// Will perform a request for '{baseUri}/{containerName}/RELEASES' to locate the available packages,
    /// and signs each request according to the Azure Storage Shared Key specification.
    /// 
    /// See <see href="https://learn.microsoft.com/en-us/rest/api/storageservices/authorize-with-shared-key">
    /// Authorize with Shared Key (Azure Storage REST API)
    /// </see> for details on how the authentication signature is constructed.
    /// </summary>
    public class AzureBlobStorageSource : IUpdateSource
    {
        private readonly string _accountName;
        private readonly string _accountKey;

        /// <summary> The base URL of the Azure Blob Storage container hosting update packages. </summary>
        public virtual Uri BaseUri { get; }

        /// <summary> The timeout for http requests, in minutes. </summary>
        public double Timeout { get; set; }

        /// <summary> The <see cref="IFileDownloader"/> to be used for performing http requests. </summary>
        public virtual IFileDownloader Downloader { get; }

        /// <inheritdoc cref="AzureBlobStorageSource" />
        public AzureBlobStorageSource(string baseUri, string accountName, string containerName, string accountKey, IFileDownloader? downloader = null, double timeout = 30)
            : this(new Uri(baseUri), accountName, containerName, accountKey, downloader, timeout)
        {
        }

        /// <inheritdoc cref="AzureBlobStorageSource" />
        public AzureBlobStorageSource(Uri baseUri, string accountName, string containerName, string accountKey, IFileDownloader? downloader = null, double timeout = 30)
        {
            _accountName = accountName;
            _accountKey = accountKey;
            BaseUri = HttpUtil.AppendPathToUri(baseUri, containerName);
            Downloader = downloader ?? HttpUtil.CreateDefaultDownloader();
            Timeout = timeout;
        }

        /// <inheritdoc />
        public async Task<VelopackAssetFeed> GetReleaseFeed(IVelopackLogger logger, string? appId, string channel, Guid? stagingId = null, VelopackAsset? latestLocalRelease = null)
        {
            string releaseFilename = CoreUtil.GetVeloReleaseIndexName(channel);
            Uri uri = HttpUtil.AppendPathToUri(BaseUri, releaseFilename);
            Dictionary<string, string> args = new();

            if (VelopackRuntimeInfo.SystemArch != RuntimeCpu.Unknown)
            {
                args.Add("arch", VelopackRuntimeInfo.SystemArch.ToString());
            }

            if (VelopackRuntimeInfo.SystemOs != RuntimeOs.Unknown)
            {
                args.Add("os", VelopackRuntimeInfo.SystemOs.GetOsShortName());
                args.Add("rid", VelopackRuntimeInfo.SystemRid);
            }

            if (latestLocalRelease != null)
            {
                args.Add("id", latestLocalRelease.PackageId);
                args.Add("localVersion", latestLocalRelease.Version.ToString());
            }

            Uri uriAndQuery = HttpUtil.AddQueryParamsToUri(uri, args);
            Dictionary<string, string> headers = BuildHeaders(uriAndQuery);

            logger.Info($"Downloading release file '{releaseFilename}' from '{uriAndQuery}'.");

            string? json = await Downloader.DownloadString(uriAndQuery.ToString(), headers, timeout: Timeout).ConfigureAwait(false);
            return VelopackAssetFeed.FromJson(json);
        }

        /// <inheritdoc />
        public async Task DownloadReleaseEntry(IVelopackLogger logger, VelopackAsset releaseEntry, string localFile, Action<int> progress, CancellationToken cancelToken = default)
        {
            if (releaseEntry == null) throw new ArgumentNullException(nameof(releaseEntry));
            if (localFile == null) throw new ArgumentNullException(nameof(localFile));

            // releaseUri can be a relative url (eg. "MyPackage.nupkg") or it can be an
            // absolute url (eg. "https://example.com/MyPackage.nupkg"). In the former case
            // it is resolved relative to the configured Azure Blob container base uri.
            Uri sourceBaseUri = HttpUtil.EnsureTrailingSlash(BaseUri);

            Uri source = HttpUtil.IsHttpUrl(releaseEntry.FileName)
                ? new Uri(releaseEntry.FileName)
                : HttpUtil.AppendPathToUri(sourceBaseUri, releaseEntry.FileName);

            Dictionary<string, string> headers = BuildHeaders(source);

            logger.Info($"Downloading '{releaseEntry.FileName}' from '{source}'.");

            foreach (KeyValuePair<string, string> kvp in headers)
            {
                logger.Info($"Key = {kvp.Key}, Value = {kvp.Value}");
            }

            await Downloader.DownloadFile(source.ToString(), localFile, progress, headers, timeout: Timeout, cancelToken: cancelToken).ConfigureAwait(false);
        }

        /// <summary>
        /// Builds the required Azure Storage authentication headers for the specified request uri.
        /// </summary>
        /// <param name="uri">The target blob storage request uri.</param>
        /// <returns>A dictionary containing the Azure Storage request headers.</returns>
        private Dictionary<string, string> BuildHeaders(Uri uri)
        {
            string date = DateTime.UtcNow.ToString("R");
            string stringToSign = BuildStringToSign(date, uri);
            string signature = ComputeSignature(stringToSign);

            return new Dictionary<string, string>
            {
                { "x-ms-date", date },
                { "x-ms-version", "2020-10-02" },
                { "Authorization", $"SharedKey {_accountName}:{signature}" }
            };
        }

        /// <summary>
        /// Builds the canonical string that must be signed for an Azure Blob Storage GET request.
        /// </summary>
        /// <param name="date">The RFC1123 formatted request date.</param>
        /// <param name="uri">The target blob storage request uri.</param>
        /// <returns>The string to sign for Shared Key authentication.</returns>
        private string BuildStringToSign(string date, Uri uri)
        {
            StringBuilder sb = new();

            sb.Append("GET\n");
            sb.Append("\n"); // Content-Encoding
            sb.Append("\n"); // Content-Language
            sb.Append("\n"); // Content-Length
            sb.Append("\n"); // Content-MD5
            sb.Append("\n"); // Content-Type
            sb.Append("\n"); // Date
            sb.Append("\n"); // If-Modified-Since
            sb.Append("\n"); // If-Match
            sb.Append("\n"); // If-None-Match
            sb.Append("\n"); // If-Unmodified-Since
            sb.Append("\n"); // Range

            sb.Append($"x-ms-date:{date}\n");
            sb.Append("x-ms-version:2020-10-02\n");
            sb.Append(BuildCanonicalizedResource(uri));

            return sb.ToString();
        }

        /// <summary>
        /// Builds the canonicalized resource string used by Azure Storage Shared Key authentication.
        /// Includes the account name, absolute path, and sorted query parameters.
        /// </summary>
        /// <param name="uri">The target blob storage request uri.</param>
        /// <returns>The canonicalized resource string.</returns>
        private string BuildCanonicalizedResource(Uri uri)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("/" + _accountName + uri.AbsolutePath);

            Dictionary<string, string> queryParams = new Dictionary<string, string>();
            string query = uri.Query;

            if (!string.IsNullOrEmpty(query))
            {
                string trimmed = query.StartsWith("?") ? query.Substring(1) : query;
                string[] parts = trimmed.Split('&');

                foreach (string part in parts)
                {
                    if (string.IsNullOrEmpty(part))
                        continue;

                    string[] kv = part.Split(new[] { '=' }, 2);
                    string key = kv[0].ToLowerInvariant();
                    string value = kv.Length > 1 ? Uri.UnescapeDataString(kv[1]) : "";

                    queryParams[key] = value;
                }
            }

            IOrderedEnumerable<string> keys = queryParams.Keys
                .OrderBy(k => k, StringComparer.Ordinal);

            foreach (string key in keys)
            {
                sb.Append("\n" + key + ":" + queryParams[key]);
            }

            return sb.ToString();
        }

        /// <summary>
        /// Computes the HMAC-SHA256 signature for the specified string using the configured Azure Storage account key.
        /// </summary>
        /// <param name="stringToSign">The canonical string to sign.</param>
        /// <returns>The base64-encoded request signature.</returns>
        private string ComputeSignature(string stringToSign)
        {
            byte[] keyBytes = Convert.FromBase64String(_accountKey);
            using HMACSHA256 hmac = new(keyBytes);
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(stringToSign));
            return Convert.ToBase64String(hash);
        }
    }
}