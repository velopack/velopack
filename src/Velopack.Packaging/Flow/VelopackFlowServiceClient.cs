using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using NuGet.Versioning;
using Microsoft.Extensions.Logging;
using System.Text;
using System.IO;


#if NET6_0_OR_GREATER
using System.Net.Http.Json;
#else
using System.Net.Http;
#endif

#nullable enable
namespace Velopack.Packaging.Flow;

public interface IVelopackFlowServiceClient
{
    Task<bool> LoginAsync(VelopackLoginOptions? options, CancellationToken cancellationToken);
    Task LogoutAsync(VelopackServiceOptions? options, CancellationToken cancellationToken);

    Task<Profile?> GetProfileAsync(VelopackServiceOptions? options, CancellationToken cancellationToken);

    Task UploadLatestReleaseAssetsAsync(string? channel, string releaseDirectory, string? serviceUrl, RuntimeOs os, CancellationToken cancellationToken);
}

public class VelopackFlowServiceClient(HttpClient HttpClient, ILogger Logger) : IVelopackFlowServiceClient
{
    private static readonly string[] Scopes = ["openid", "offline_access"];

    public bool HasAuthentication => HttpClient.DefaultRequestHeaders.Authorization is not null;

    private AuthConfiguration? AuthConfiguration { get; set; }

    public async Task<bool> LoginAsync(VelopackLoginOptions? options, CancellationToken cancellationToken)
    {
        options ??= new VelopackLoginOptions();
        Logger.LogInformation("Preparing to login to Velopack ({ServiceUrl})", options.VelopackBaseUrl);

        var authConfiguration = await GetAuthConfigurationAsync(options, cancellationToken);

        var pca = await BuildPublicApplicationAsync(authConfiguration);

        if (!string.IsNullOrWhiteSpace(options.ApiKey)) {
            HttpClient.DefaultRequestHeaders.Authorization = new(HmacHelper.HmacScheme, options.ApiKey);
            var profile = await GetProfileAsync(options, cancellationToken);
            Logger.LogInformation("{UserName} logged into Velopack with API key", profile?.GetDisplayName());
            return true;
        } else {
            AuthenticationResult? rv = null;
            if (options.AllowCacheCredentials) {
                rv = await AcquireSilentlyAsync(pca, cancellationToken);
            }
            if (rv is null && options.AllowInteractiveLogin) {
                rv = await AcquireInteractiveAsync(pca, authConfiguration, cancellationToken);
            }
            if (rv is null && options.AllowDeviceCodeFlow) {
                rv = await AcquireByDeviceCodeAsync(pca, cancellationToken);
            }

            if (rv != null) {
                HttpClient.DefaultRequestHeaders.Authorization = new("Bearer", rv.IdToken ?? rv.AccessToken);
                var profile = await GetProfileAsync(options, cancellationToken);

                Logger.LogInformation("{UserName} logged into Velopack", profile?.GetDisplayName());
                return true;
            } else {
                Logger.LogError("Failed to login to Velopack");
                return false;
            }
        }
    }

    public async Task LogoutAsync(VelopackServiceOptions? options, CancellationToken cancellationToken)
    {
        var authConfiguration = await GetAuthConfigurationAsync(options, cancellationToken);

        var pca = await BuildPublicApplicationAsync(authConfiguration);

        // clear the cache
        while ((await pca.GetAccountsAsync()).FirstOrDefault() is { } account) {
            await pca.RemoveAsync(account);
            Logger.LogInformation("Logged out of {Username}", account.Username);
        }
        Logger.LogInformation("Cleared saved login(s) for Velopack");
    }

    public async Task<Profile?> GetProfileAsync(VelopackServiceOptions? options, CancellationToken cancellationToken)
    {
        AssertAuthenticated();
        var endpoint = GetEndpoint("v1/user/profile", options?.VelopackBaseUrl);

        return await HttpClient.GetFromJsonAsync<Profile>(endpoint, cancellationToken);
    }

    public async Task UploadLatestReleaseAssetsAsync(string? channel, string releaseDirectory, string? serviceUrl,
        RuntimeOs os, CancellationToken cancellationToken)
    {
        AssertAuthenticated();

        channel ??= ReleaseEntryHelper.GetDefaultChannel(os);
        ReleaseEntryHelper helper = new(releaseDirectory, channel, Logger, os);
        var latestAssets = helper.GetLatestAssets().ToList();

        List<string> installers = [];

        List<string> files = latestAssets.Select(x => x.FileName).ToList();
        string? packageId = null;
        SemanticVersion? version = null;
        if (latestAssets.Count > 0) {
            packageId = latestAssets[0].PackageId;
            version = latestAssets[0].Version;

            if (VelopackRuntimeInfo.IsWindows || VelopackRuntimeInfo.IsOSX) {
                var setupName = ReleaseEntryHelper.GetSuggestedSetupName(packageId, channel, os);
                if (File.Exists(Path.Combine(releaseDirectory, setupName))) {
                    installers.Add(setupName);
                }
            }

            var portableName = ReleaseEntryHelper.GetSuggestedPortableName(packageId, channel, os);
            if (File.Exists(Path.Combine(releaseDirectory, portableName))) {
                installers.Add(portableName);
            }
        }

        if (packageId is null) {
            Logger.LogError("No package ID found in release directory {ReleaseDirectory}", releaseDirectory);
            return;
        }
        if (version is null) {
            Logger.LogError("No version found in release directory {ReleaseDirectory}", releaseDirectory);
            return;
        }

        Logger.LogInformation("Uploading {AssetCount} assets to Velopack Flow ({ServiceUrl})", latestAssets.Count + installers.Count, serviceUrl);

        ReleaseGroup releaseGroup = await CreateReleaseGroupAsync(packageId, version, channel, serviceUrl, cancellationToken);

        foreach (var assetFileName in files) {
            await UploadReleaseAssetAsync(releaseDirectory, assetFileName, serviceUrl, releaseGroup.Id, FileType.Release, cancellationToken).ConfigureAwait(false);

            Logger.LogInformation("Uploaded {FileName} to Velopack Flow", assetFileName);
        }

        foreach (var installerFile in installers) {
            await UploadReleaseAssetAsync(releaseDirectory, installerFile, serviceUrl, releaseGroup.Id, FileType.Installer, cancellationToken).ConfigureAwait(false);

            Logger.LogInformation("Uploaded {FileName} installer to Velopack Flow", installerFile);
        }
    }

    private async Task<ReleaseGroup> CreateReleaseGroupAsync(
        string packageId, SemanticVersion version, string channel,
        string? velopackBaseUrl, CancellationToken cancellationToken)
    {
        CreateReleaseGroupRequest request = new() {
            ChannelIdentifier = channel,
            PackageId = packageId,
            Version = version.ToNormalizedString()
        };

        var endpoint = GetEndpoint("v1/releaseGroups/create", velopackBaseUrl);
        var response = await HttpClient.PostAsJsonAsync(endpoint, request, cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<ReleaseGroup>(cancellationToken: cancellationToken)
            ?? throw new InvalidOperationException($"Failed to create release group with version {version.ToNormalizedString()}");
    }

    private async Task UploadReleaseAssetAsync(string releaseDirectory, string fileName,
        string? serviceUrl, Guid releaseGroupId, FileType fileType, CancellationToken cancellationToken)
    {
        using var formData = new MultipartFormDataContent
        {
            { new StringContent(releaseGroupId.ToString()), "ReleaseGroupId" },
            { new StringContent(fileType.ToString()), "FileType" }
        };

        var latestPath = Path.Combine(releaseDirectory, fileName);

        using var fileStream = File.OpenRead(latestPath);
        
        using var fileContent = new StreamContent(fileStream);
        formData.Add(fileContent, "File", fileName);

        var endpoint = GetEndpoint("v1/releases/upload", serviceUrl);

        var response = await HttpClient.PostAsync(endpoint, formData, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<AuthConfiguration> GetAuthConfigurationAsync(VelopackServiceOptions? options, CancellationToken cancellationToken)
    {
        if (AuthConfiguration is not null)
            return AuthConfiguration;

        var endpoint = GetEndpoint("v1/auth/config", options);

        var authConfig = await HttpClient.GetFromJsonAsync<AuthConfiguration>(endpoint, cancellationToken);
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
        => GetEndpoint(relativePath, options?.VelopackBaseUrl);

    private static Uri GetEndpoint(string relativePath, string? velopackBaseUrl)
    {
        var baseUrl = velopackBaseUrl ?? VelopackServiceOptions.DefaultBaseUrl;
        var endpoint = new Uri(relativePath, UriKind.Relative);
        return new(new Uri(baseUrl), endpoint);
    }

    private void AssertAuthenticated()
    {
        if (!HasAuthentication) {
            throw new InvalidOperationException($"{nameof(VelopackFlowServiceClient)} has not been authenticated, call {nameof(LoginAsync)} first.");
        }
    }

    private static async Task<AuthenticationResult?> AcquireSilentlyAsync(IPublicClientApplication pca, CancellationToken cancellationToken)
    {
        foreach (var account in await pca.GetAccountsAsync()) {
            try {
                if (account is not null) {
                    return await pca.AcquireTokenSilent(Scopes, account)
                        .ExecuteAsync(cancellationToken);
                }
            } catch (MsalException) {
                await pca.RemoveAsync(account);
                // No token found in the cache or Azure AD insists that a form interactive auth is required (e.g. the tenant admin turned on MFA)
            }
        }
        return null;
    }

    private static async Task<AuthenticationResult?> AcquireInteractiveAsync(IPublicClientApplication pca, AuthConfiguration authConfiguration, CancellationToken cancellationToken)
    {
        try {
            return await pca.AcquireTokenInteractive(Scopes)
                        .WithB2CAuthority(authConfiguration.B2CAuthority)
                        .ExecuteAsync(cancellationToken);
        } catch (MsalException) {
        }
        return null;
    }

    private async Task<AuthenticationResult?> AcquireByDeviceCodeAsync(IPublicClientApplication pca, CancellationToken cancellationToken)
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
                    Logger.LogInformation(deviceCodeResult.Message);
                    return Task.FromResult(0);
                }).ExecuteAsync(cancellationToken);

            Logger.LogInformation(result.Account.Username);
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
#if DEBUG
                .WithLogging((Microsoft.Identity.Client.LogLevel level, string message, bool containsPii) => System.Console.WriteLine($"[{level}]: {message}"), enablePiiLogging: true, enableDefaultPlatformLogging: true)
#endif
                .WithClientName("velopack")
                .Build();

        cacheHelper.RegisterCache(pca.UserTokenCache);
        return pca;
    }

    private enum FileType
    {
        Unknown,
        Release,
        Installer,
    }
}
