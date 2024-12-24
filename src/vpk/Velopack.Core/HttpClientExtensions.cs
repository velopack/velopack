#if NETFRAMEWORK || NETSTANDARD2_0

#nullable enable
using System.Net.Http;
using System.Text;

namespace Velopack.Core;

public static class HttpClientExtensions
{
    public static async Task<TValue?> GetFromJsonAsync<TValue>(
        this HttpClient client,
        Uri? requestUri,
        CancellationToken cancellationToken = default)
    {
        var response = await client.GetAsync(requestUri, cancellationToken);
        response.EnsureSuccessStatusCode();

        return SimpleJson.DeserializeObject<TValue>(await response.Content.ReadAsStringAsync());
    }

    public static async Task<HttpResponseMessage> PostAsJsonAsync<TValue>(
        this HttpClient client,
        Uri? requestUri,
        TValue value,
        CancellationToken cancellationToken = default)
    {
        var content = new StringContent(SimpleJson.SerializeObject(value), Encoding.UTF8, "application/json");
        return await client.PostAsync(requestUri, content, cancellationToken);
    }

    public static async Task<HttpResponseMessage> PutAsJsonAsync<TValue>(
        this HttpClient client,
        Uri? requestUri,
        TValue value,
        CancellationToken cancellationToken = default)
    {
        var content = new StringContent(SimpleJson.SerializeObject(value), Encoding.UTF8, "application/json");
        return await client.PutAsync(requestUri, content, cancellationToken);
    }

    public static async Task<TValue?> ReadFromJsonAsync<TValue>(
        this HttpContent content,
        CancellationToken cancellationToken = default)
    {
        var json = await content.ReadAsStringAsync();
        return SimpleJson.DeserializeObject<TValue>(json);
    }

    public static async Task<string> ReadAsStringAsync(this HttpContent content, CancellationToken _)
    {
        return await content.ReadAsStringAsync();
    }
}
#endif
