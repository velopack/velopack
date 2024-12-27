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
            throw new ArgumentNullException(nameof(packageId));

        if (channel == null)
            throw new ArgumentNullException(nameof(channel));

        var client = _httpClient;

        using var request = new HttpRequestMessage();
        request.Method = new HttpMethod("GET");

        var urlBuilder = new StringBuilder();
        if (!string.IsNullOrEmpty(_baseUrl)) urlBuilder.Append(_baseUrl);
        // Operation Path: "v1/download/{packageId}/{channel}"
        urlBuilder.Append("v1/download/");
        urlBuilder.Append(Uri.EscapeDataString(ConvertToString(packageId, CultureInfo.InvariantCulture)));
        urlBuilder.Append('/');
        urlBuilder.Append(Uri.EscapeDataString(ConvertToString(channel, CultureInfo.InvariantCulture)));
        urlBuilder.Append('?');
        if (assetType != null) {
            urlBuilder.Append(Uri.EscapeDataString("assetType")).Append('=')
                .Append(Uri.EscapeDataString(ConvertToString(assetType, CultureInfo.InvariantCulture))).Append('&');
        }

        urlBuilder.Length--;

        PrepareRequest(client, request, urlBuilder);

        var url = urlBuilder.ToString();
        request.RequestUri = new Uri(url, UriKind.RelativeOrAbsolute);

        PrepareRequest(client, request, url);

        using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);

        var headers = new Dictionary<string, IEnumerable<string>>();
        foreach (var item in response.Headers)
            headers[item.Key] = item.Value;
        if (response.Content != null && response.Content.Headers != null) {
            foreach (var item in response.Content.Headers)
                headers[item.Key] = item.Value;
        }

        ProcessResponse(client, response);

        var status = (int) response.StatusCode;
        if (status == 404) {
            string responseText_ = (response.Content == null) ? string.Empty :
#if NET6_0_OR_GREATER
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
                await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif

            throw new ApiException("A server side error occurred.", status, responseText_, headers, null);
        } else if (status == 200 || status == 204) {
            using var fs = File.Create(localFilePath);
            if (response.Content != null) {
#if NET6_0_OR_GREATER
                await response.Content.CopyToAsync(fs, cancellationToken);
#else
                await response.Content.CopyToAsync(fs);
#endif
            }
            return;
        } else {
            var responseData = response.Content == null ? null :
#if NET6_0_OR_GREATER
                await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
#else
                await response.Content.ReadAsStringAsync().ConfigureAwait(false);
#endif
            throw new ApiException(
                "The HTTP status code of the response was not expected (" + status + ").",
                status,
                responseData,
                headers,
                null);
        }

    }
}