#if NETFRAMEWORK || NETSTANDARD2_0

#nullable enable
using System.Net.Http;

namespace Velopack.Packaging;

public static class HttpClientExtensions
{
    public static async Task<TValue?> GetFromJsonAsync<TValue>(
        this HttpClient client,
        Uri? requestUri,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        return Newtonsoft.Json.JsonConvert.DeserializeObject<TValue>(await response.Content.ReadAsStringAsync());
    }
}
#endif
