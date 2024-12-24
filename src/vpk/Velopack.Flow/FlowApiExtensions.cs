using System.Globalization;
using System.Net.Http;
using System.Text;

namespace Velopack.Flow;

public partial class Profile
{
    public string GetDisplayName()
    {
        return DisplayName ?? Email ?? "<unknown>";
    }
}

public partial class FlowApi
{
    public virtual async Task DownloadInstallerLatestToFileAsync(string packageId, string channel, DownloadAssetType? assetType, string localFilePath,
        CancellationToken cancellationToken)
    {
        if (packageId == null)
            throw new ArgumentNullException("packageId");

        if (channel == null)
            throw new ArgumentNullException("channel");

        var client_ = _httpClient;
        var disposeClient_ = false;
        try {
            using (var request_ = new HttpRequestMessage()) {
                request_.Method = new HttpMethod("GET");

                var urlBuilder_ = new StringBuilder();
                if (!string.IsNullOrEmpty(_baseUrl)) urlBuilder_.Append(_baseUrl);
                // Operation Path: "v1/download/{packageId}/{channel}"
                urlBuilder_.Append("v1/download/");
                urlBuilder_.Append(Uri.EscapeDataString(ConvertToString(packageId, CultureInfo.InvariantCulture)));
                urlBuilder_.Append('/');
                urlBuilder_.Append(Uri.EscapeDataString(ConvertToString(channel, CultureInfo.InvariantCulture)));
                urlBuilder_.Append('?');
                if (assetType != null) {
                    urlBuilder_.Append(Uri.EscapeDataString("assetType")).Append('=')
                        .Append(Uri.EscapeDataString(ConvertToString(assetType, CultureInfo.InvariantCulture))).Append('&');
                }

                urlBuilder_.Length--;

                PrepareRequest(client_, request_, urlBuilder_);

                var url_ = urlBuilder_.ToString();
                request_.RequestUri = new Uri(url_, UriKind.RelativeOrAbsolute);

                PrepareRequest(client_, request_, url_);

                var response_ = await client_.SendAsync(request_, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                var disposeResponse_ = true;
                try {
                    var headers_ = new Dictionary<string, IEnumerable<string>>();
                    foreach (var item_ in response_.Headers)
                        headers_[item_.Key] = item_.Value;
                    if (response_.Content != null && response_.Content.Headers != null) {
                        foreach (var item_ in response_.Content.Headers)
                            headers_[item_.Key] = item_.Value;
                    }

                    ProcessResponse(client_, response_);

                    var status_ = (int) response_.StatusCode;
                    if (status_ == 404) {
                        string responseText_ = (response_.Content == null) ? string.Empty : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new ApiException("A server side error occurred.", status_, responseText_, headers_, null);
                    } else if (status_ == 200 || status_ == 204) {
                        using var fs = File.Create(localFilePath);
#if NET6_0_OR_GREATER
                        await response_.Content.CopyToAsync(fs, cancellationToken);
#else
                        await response_.Content.CopyToAsync(fs);
#endif
                        return;
                    } else {
                        var responseData_ = response_.Content == null ? null : await response_.Content.ReadAsStringAsync().ConfigureAwait(false);
                        throw new ApiException(
                            "The HTTP status code of the response was not expected (" + status_ + ").",
                            status_,
                            responseData_,
                            headers_,
                            null);
                    }
                } finally {
                    if (disposeResponse_)
                        response_.Dispose();
                }
            }
        } finally {
            if (disposeClient_)
                client_.Dispose();
        }
    }
}