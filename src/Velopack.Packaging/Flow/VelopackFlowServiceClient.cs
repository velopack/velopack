using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;

using Velopack.Packaging.Abstractions;


#if NET6_0_OR_GREATER
using System.Net.Http.Json;
#else
using System.Net.Http;
#endif

#nullable enable
namespace Velopack.Packaging.Flow;

public interface IVelopackFlowServiceClient
{
    Task<bool> LoginAsync(VelopackLoginOptions? options = null);
    Task LogoutAsync(VelopackServiceOptions? options = null);

    Task<Profile?> GetProfileAsync(VelopackServiceOptions? options = null);
    Task UploadReleaseAssetAsync(UploadOptions options);
}

public class VelopackFlowServiceClient(HttpClient HttpClient, IConsole Console) : IVelopackFlowServiceClient
{
    private static readonly string[] Scopes = ["openid", "offline_access"];

    public bool HasAuthentication => HttpClient.DefaultRequestHeaders.Authorization is not null;

    private AuthConfiguration? AuthConfiguration { get; set; }

    public async Task<bool> LoginAsync(VelopackLoginOptions? options = null)
    {
        options ??= new VelopackLoginOptions();
        Console.WriteLine($"Preparing to login to Velopack ({options.VelopackBaseUrl})");

        var authConfiguration = await GetAuthConfigurationAsync(options);

        var pca = await BuildPublicApplicationAsync(authConfiguration);

        if (!string.IsNullOrWhiteSpace(options.ApiKey)) {
            HttpClient.DefaultRequestHeaders.Authorization = new(HmacHelper.HmacScheme, options.ApiKey);
            var profile = await GetProfileAsync(options);
            Console.WriteLine($"{profile?.Name} logged into Velopack with API key");
            return true;
        } else {
            AuthenticationResult? rv = null;
            if (options.AllowCacheCredentials) {
                rv = await AcquireSilentlyAsync(pca);
            }
            if (rv is null && options.AllowInteractiveLogin) {
                rv = await AcquireInteractiveAsync(pca, authConfiguration);
            }
            if (rv is null && options.AllowDeviceCodeFlow) {
                rv = await AcquireByDeviceCodeAsync(pca);
            }

            if (rv != null) {
                HttpClient.DefaultRequestHeaders.Authorization = new("Bearer", rv.IdToken ?? rv.AccessToken);
                var profile = await GetProfileAsync(options);

                Console.WriteLine($"{profile?.Name} logged into Velopack");
                return true;
            } else {
                Console.WriteLine("Failed to login to Velopack");
                return false;
            }
        }
    }

    public async Task LogoutAsync(VelopackServiceOptions? options = null)
    {
        var authConfiguration = await GetAuthConfigurationAsync(options);

        var pca = await BuildPublicApplicationAsync(authConfiguration);

        // clear the cache
        while ((await pca.GetAccountsAsync()).FirstOrDefault() is { } account) {
            await pca.RemoveAsync(account);
            Console.WriteLine($"Logged out of {account.Username}");
        }
        Console.WriteLine("Cleared saved login(s) for Velopack");
    }

    public async Task<Profile?> GetProfileAsync(VelopackServiceOptions? options = null)
    {
        AssertAuthenticated();
        var endpoint = GetEndpoint("v1/user/profile", options);

        return await HttpClient.GetFromJsonAsync<Profile>(endpoint);
    }

    public async Task UploadReleaseAssetAsync(UploadOptions options)
    {
        AssertAuthenticated();

        using var formData = new MultipartFormDataContent
        {
            { new StringContent(options.Channel ?? ""), "Channel" }
        };

        using var fileContent = new StreamContent(options.ReleaseData);
        formData.Add(fileContent, "File", options.FileName);

        var endpoint = GetEndpoint("v1/upload-release", options);

        var response = await HttpClient.PostAsync(endpoint, formData);

        response.EnsureSuccessStatusCode();
    }

    public async Task UploadInstallerAssetAsync(UploadInstallerOptions options)
    {
        AssertAuthenticated();

        using var formData = new MultipartFormDataContent
        {
            { new StringContent(options.PackageId ?? ""), "PackageId" },
            { new StringContent(options.Channel ?? ""), "Channel" },
            { new StringContent(options.Version.ToNormalizedString() ?? ""), "Version" },
        };

        using var fileContent = new StreamContent(options.ReleaseData);
        formData.Add(fileContent, "File", options.FileName);

        var endpoint = GetEndpoint("v1/upload-installer", options);

        var response = await HttpClient.PostAsync(endpoint, formData);

        response.EnsureSuccessStatusCode();
    }

    private async Task<AuthConfiguration> GetAuthConfigurationAsync(VelopackServiceOptions? options)
    {
        if (AuthConfiguration is not null)
            return AuthConfiguration;

        var endpoint = GetEndpoint("v1/auth/config", options);

        var authConfig = await HttpClient.GetFromJsonAsync<AuthConfiguration>(endpoint);
        if (authConfig is null)
            throw new Exception("Failed to get auth configuration.");
        if (authConfig.B2CAuthority is null)
            throw new Exception("B2C Authority not provided.");
        if (authConfig.RedirectUri is null)
            throw new Exception("Redirect URI not provided.");
        if (authConfig.ClientId is null)
            throw new Exception("Client ID not provided.");

        return authConfig;
    }

    private static Uri GetEndpoint(string relativePath, VelopackServiceOptions? options)
    {
        var baseUrl = options?.VelopackBaseUrl ?? VelopackServiceOptions.DefaultBaseUrl;
        var endpoint = new Uri(relativePath, UriKind.Relative);
        return new(new Uri(baseUrl), endpoint);
    }

    private void AssertAuthenticated()
    {
        if (!HasAuthentication) {
            throw new InvalidOperationException($"{nameof(VelopackFlowServiceClient)} has not been authenticated, call {nameof(LoginAsync)} first.");
        }
    }

    private static async Task<AuthenticationResult?> AcquireSilentlyAsync(IPublicClientApplication pca)
    {
        foreach (var account in await pca.GetAccountsAsync()) {
            try {
                if (account is not null) {
                    return await pca.AcquireTokenSilent(Scopes, account)
                        .ExecuteAsync();
                }
            } catch (MsalException) {
                await pca.RemoveAsync(account);
                // No token found in the cache or Azure AD insists that a form interactive auth is required (e.g. the tenant admin turned on MFA)
            }
        }
        return null;
    }

    private static async Task<AuthenticationResult?> AcquireInteractiveAsync(IPublicClientApplication pca, AuthConfiguration authConfiguration)
    {
        try {
            return await pca.AcquireTokenInteractive(Scopes)
                        .WithB2CAuthority(authConfiguration.B2CAuthority)
                        .ExecuteAsync();
        } catch (MsalException) {
        }
        return null;
    }

    private async Task<AuthenticationResult?> AcquireByDeviceCodeAsync(IPublicClientApplication pca)
    {
        try {
            var result = await pca.AcquireTokenWithDeviceCode(Scopes,
                deviceCodeResult => {
                    // This will print the message on the logger which tells the user where to go sign-in using 
                    // a separate browser and the code to enter once they sign in.
                    // The AcquireTokenWithDeviceCode() method will poll the server after firing this
                    // device code callback to look for the successful login of the user via that browser.
                    // This background polling (whose interval and timeout data is also provided as fields in the 
                    // deviceCodeCallback class) will occur until:
                    // * The user has successfully logged in via browser and entered the proper code
                    // * The timeout specified by the server for the lifetime of this code (typically ~15 minutes) has been reached
                    // * The developing application calls the Cancel() method on a CancellationToken sent into the method.
                    //   If this occurs, an OperationCanceledException will be thrown (see catch below for more details).
                    Console.WriteLine(deviceCodeResult.Message);
                    return Task.FromResult(0);
                }).ExecuteAsync();

            Console.WriteLine(result.Account.Username);
            return result;
        } catch (MsalException) {
        }
        return null;
    }

    private static async Task<IPublicClientApplication> BuildPublicApplicationAsync(AuthConfiguration authConfiguration)
    {
        //https://learn.microsoft.com/entra/msal/dotnet/how-to/token-cache-serialization?tabs=desktop&WT.mc_id=DT-MVP-5003472
        var userPath = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var vpkPath = Path.Combine(userPath, ".vpk");

        var storageProperties =
             new StorageCreationPropertiesBuilder("creds.bin", vpkPath)
             .WithLinuxKeyring(
                 schemaName: "com.velopack.app",
                 collection: "default",
                 secretLabel: "Credentials for Velopack",
                 new KeyValuePair<string, string>("vpk.client-id", authConfiguration.ClientId ?? ""),
                 new KeyValuePair<string, string>("vpk.version", "v1")
              )
             .WithMacKeyChain(
                 serviceName: "velopack",
                 accountName: "vpk")
             .Build();
        var cacheHelper = await MsalCacheHelper.CreateAsync(storageProperties);

        var pca = PublicClientApplicationBuilder
                .Create(authConfiguration.ClientId)
                .WithB2CAuthority(authConfiguration.B2CAuthority)
                .WithRedirectUri(authConfiguration.RedirectUri)
                //.WithLogging((LogLevel level, string message, bool containsPii) => System.Console.WriteLine($"[{level}]: {message}"))
                .WithClientName("velopack")
                .Build();

        cacheHelper.RegisterCache(pca.UserTokenCache);
        return pca;
    }
}
