#nullable enable
// Adapted from AzureSignTool's AccessTokenCredential
// (https://github.com/vcsjones/AzureSignTool, MIT License, Copyright (c) 2017 Kevin Jones and Oren Novotny).

using Azure.Core;

namespace Velopack.Packaging.Windows.Signing;

/// <summary>
/// A <see cref="TokenCredential"/> that always returns a fixed, pre-acquired bearer token.
/// </summary>
public sealed class AccessTokenCredential : TokenCredential
{
    private readonly AccessToken _accessToken;

    public AccessTokenCredential(string token, DateTimeOffset expiresOn)
    {
        if (String.IsNullOrWhiteSpace(token)) {
            throw new ArgumentException("Access token cannot be null or empty.", nameof(token));
        }

        _accessToken = new AccessToken(token, expiresOn);
    }

    public override AccessToken GetToken(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return _accessToken;
    }

    public override ValueTask<AccessToken> GetTokenAsync(TokenRequestContext requestContext, CancellationToken cancellationToken)
    {
        return new ValueTask<AccessToken>(_accessToken);
    }
}
