using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading;
using System.Threading.Tasks;
using Velopack.Packaging.Flow;

namespace Velopack.Build;

internal class HmacAuthHttpClientHandler : HttpClientHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Debugger.Launch();
        if (request.Headers.Authorization?.Scheme == HmacHelper.HmacScheme &&
            request.Headers.Authorization.Parameter is { } authParameter &&
            authParameter.Split(':') is var keyParts &&
            keyParts.Length == 2) 
        {
            string hashedId = keyParts[0];
            string key = keyParts[1];
            string nonce = Guid.NewGuid().ToString();

            uint secondsSinceEpoch = HmacHelper.GetSecondsSinceEpoch();
            var signature = HmacHelper.BuildSignature(hashedId, request.Method.Method, request.RequestUri?.AbsoluteUri ?? "", secondsSinceEpoch, nonce);
            var secret = HmacHelper.Calculate(Convert.FromBase64String(key), signature);
            request.Headers.Authorization = BuildHeader(hashedId, secret, nonce, secondsSinceEpoch);
        }
        return await base.SendAsync(request, cancellationToken).ConfigureAwait(false);
    }

    private static AuthenticationHeaderValue BuildHeader(string hashedId, string base64Signature, string nonce, uint secondsSinceEpoch)
        => new(HmacHelper.HmacScheme, $"{hashedId}:{base64Signature}:{nonce}:{secondsSinceEpoch}");
}
