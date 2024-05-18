using System.Net.Http;
using System.Net.Http.Headers;

namespace Velopack.Packaging.Flow;

public class HmacAuthHttpClientHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Headers.Authorization?.Scheme == HmacHelper.HmacScheme &&
            request.Headers.Authorization.Parameter is { } authParameter &&
            authParameter.Split(':') is var keyParts &&
            keyParts.Length == 2) {
            var hashedId = keyParts[0];
            var key = keyParts[1];
            var nonce = Guid.NewGuid().ToString();

            var secondsSinceEpoch = HmacHelper.GetSecondsSinceEpoch();
            var signature = HmacHelper.BuildSignature(hashedId, request.Method.Method, request.RequestUri?.AbsoluteUri ?? "", secondsSinceEpoch, nonce);
            var secret = HmacHelper.Calculate(Convert.FromBase64String(key), signature);
            request.Headers.Authorization = BuildHeader(hashedId, secret, nonce, secondsSinceEpoch);
        }
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static AuthenticationHeaderValue BuildHeader(string hashedId, string base64Signature, string nonce, uint secondsSinceEpoch)
        => new(HmacHelper.HmacScheme, $"{hashedId}:{base64Signature}:{nonce}:{secondsSinceEpoch}");
}
