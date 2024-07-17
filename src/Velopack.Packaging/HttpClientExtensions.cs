#if NETFRAMEWORK || NETSTANDARD2_0

#nullable enable
using System.Net.Http;
using System.Text;

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

    public static async Task<HttpResponseMessage> PostAsJsonAsync<TValue>(
        this HttpClient client,
        Uri? requestUri,
        TValue value,
        CancellationToken cancellationToken = default)
    {
        var content = new StringContent(Newtonsoft.Json.JsonConvert.SerializeObject(value), Encoding.UTF8, "application/json");
        return await client.PostAsync(requestUri, content, cancellationToken);
    }
}
#endif
