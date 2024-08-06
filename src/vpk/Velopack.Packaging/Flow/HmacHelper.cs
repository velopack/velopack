using System.Security.Cryptography;

using System.Text;

#nullable enable
namespace Velopack.Packaging.Flow;

public static class HmacHelper
{
    public const string HmacScheme = "hmacauth";
    public static DateTime EpochStart { get; } = new(1970, 01, 01, 0, 0, 0, 0, DateTimeKind.Utc);

    public static uint GetSecondsSinceEpoch()
        => (uint)(DateTime.UtcNow - EpochStart).TotalSeconds;

    public static string BuildSignature(string hashedId, string httpMethod, string requestUri, uint secondsSinceEpoch, string nonce)
        => $"{hashedId}{httpMethod.ToUpperInvariant()}{requestUri.ToLowerInvariant()}{secondsSinceEpoch}{nonce}";

#if NET6_0_OR_GREATER
    public static async Task<string> GetContentHashAsync(Stream content)
    {
        if (content is null) {
            throw new ArgumentNullException(nameof(content));
        }

        using MD5 md5 = MD5.Create();
        byte[] requestContentHash = await md5.ComputeHashAsync(content);
        return Convert.ToBase64String(requestContentHash);
    }
#else
    public static string GetContentHash(Stream content)
    {
        if (content is null) {
            throw new ArgumentNullException(nameof(content));
        }

        using MD5 md5 = MD5.Create();
        byte[] requestContentHash = md5.ComputeHash(content);
        return Convert.ToBase64String(requestContentHash);
    }
#endif

    public static string Calculate(byte[] secret, string signatureData)
    {
        if (signatureData is null) {
            throw new ArgumentNullException(nameof(signatureData));
        }
        using HMAC hmac = new HMACSHA256();
        hmac.Key = secret ?? throw new ArgumentNullException(nameof(secret));
        return Convert.ToBase64String(hmac.ComputeHash(Encoding.UTF8.GetBytes(signatureData)));
    }
}