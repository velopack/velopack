extern alias HttpFormatting;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using NuGet.Versioning;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Net.Http.Headers;
using Velopack.NuGet;
using Velopack.Packaging.Abstractions;
using Velopack.Util;

#if NET6_0_OR_GREATER
using System.Net.Http.Json;
#else
using System.Net.Http;
#endif

#nullable enable
namespace Velopack.Packaging.Flow;

public interface IVelopackFlowServiceClient
{
    Task<bool> LoginAsync(VelopackLoginOptions? options, bool suppressOutput, CancellationToken cancellationToken);

    Task LogoutAsync(VelopackServiceOptions? options, CancellationToken cancellationToken);

    Task<Profile?> GetProfileAsync(VelopackServiceOptions? options, CancellationToken cancellationToken);

    Task<string> InvokeEndpointAsync(VelopackServiceOptions? options, string endpointUri,
        string method,
        string? body,
        CancellationToken cancellationToken);

    Task UploadLatestReleaseAssetsAsync(string? channel, string releaseDirectory, string? serviceUrl, RuntimeOs os,
        bool noWaitForLive, CancellationToken cancellationToken);
}

public class VelopackFlowServiceClient(
    IHttpMessageHandlerFactory HttpMessageHandlerFactory,
    ILogger Logger,
    IFancyConsole Console) : IVelopackFlowServiceClient
{
    private static readonly string[] Scopes = ["openid", "offline_access"];

    private AuthenticationHeaderValue? Authorization = null;

    private AuthConfiguration? AuthConfiguration { get; set; }

    private HttpClient GetHttpClient(Action<int>? progress = null)
    {
        HttpMessageHandler primaryHandler = HttpMessageHandlerFactory.CreateHandler("flow");

        if (progress != null) {
            var ph = new HttpFormatting::System.Net.Http.Handlers.ProgressMessageHandler(primaryHandler);
            ph.HttpSendProgress += (_, args) => {
                progress(args.ProgressPercentage);
                // Console.WriteLine($"upload progress: {((double)args.BytesTransferred / args.TotalBytes) * 100.0}");
            };
            ph.HttpReceiveProgress += (_, args) => {
                progress(args.ProgressPercentage);
            };
            primaryHandler = ph;
        }

        var client = new HttpClient(primaryHandler);
        client.DefaultRequestHeaders.Authorization = Authorization;
        return client;
    }

    public async Task<bool> LoginAsync(VelopackLoginOptions? options, bool suppressOutput, CancellationToken cancellationToken)
    {
        options ??= new VelopackLoginOptions();
        if (!suppressOutput) {
            Logger.LogInformation("Preparing to login to Velopack ({ServiceUrl})", options.VelopackBaseUrl);
        }

        var authConfiguration = await GetAuthConfigurationAsync(options, cancellationToken);
        var pca = await BuildPublicApplicationAsync(authConfiguration);

        if (!string.IsNullOrWhiteSpace(options.ApiKey)) {
            Authorization = new(HmacHelper.HmacScheme, options.ApiKey);
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

            if (rv is null) {
                Logger.LogError("Failed to login to Velopack");
                return false;
            }

            Authorization = new("Bearer", rv.IdToken ?? rv.AccessToken);
        }

        var profile = await GetProfileAsync(options, cancellationToken);

        if (!suppressOutput) {
            Logger.LogInformation("{UserName} logged into Velopack", profile?.GetDisplayName());
        }

        return true;
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

        var client = GetHttpClient();
        return await client.GetFromJsonAsync<Profile>(endpoint, cancellationToken);
    }

    public async Task<string> InvokeEndpointAsync(
        VelopackServiceOptions? options,
        string endpointUri,
        string method,
        string? body,
        CancellationToken cancellationToken)
    {
        AssertAuthenticated();
        var endpoint = GetEndpoint(endpointUri, options?.VelopackBaseUrl);

        HttpRequestMessage request = new(new HttpMethod(method), endpoint);
        if (body is not null) {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

        var client = GetHttpClient();
        HttpResponseMessage response = await client.SendAsync(request, cancellationToken);

#if NET6_0_OR_GREATER
        string responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
#else
        string responseBody = await response.Content.ReadAsStringAsync();
#endif

        if (response.IsSuccessStatusCode) {
            return responseBody;
        } else {
            throw new InvalidOperationException(
                $"Failed to invoke endpoint {endpointUri} with status code {response.StatusCode}{Environment.NewLine}{responseBody}");
        }
    }

    public async Task UploadLatestReleaseAssetsAsync(string? channel, string releaseDirectory, string? serviceUrl,
        RuntimeOs os, bool noWaitForLive, CancellationToken cancellationToken)
    {
        AssertAuthenticated();

        channel ??= ReleaseEntryHelper.GetDefaultChannel(os);
        BuildAssets assets = BuildAssets.Read(releaseDirectory, channel);
        var fullAsset = assets.GetReleaseEntries().SingleOrDefault(a => a.Type == VelopackAssetType.Full);

        if (fullAsset is null) {
            Logger.LogError("No full asset found in release directory {ReleaseDirectory} (or it's missing from assets file)", releaseDirectory);
            return;
        }

        var fullAssetPath = Path.Combine(releaseDirectory, fullAsset.FileName);
        var packageId = fullAsset.PackageId;
        var version = fullAsset.Version;

        var filesToUpload = assets.GetNonReleaseAssetPaths().Select(p => (p, FileType.Installer))
            .Concat([(fullAssetPath, FileType.Release)])
            .ToArray();

        Logger.LogInformation("Beginning upload to Velopack Flow (serviceUrl={ServiceUrl})", serviceUrl);

        await Console.ExecuteProgressAsync(
            async (progress) => {
                ReleaseGroup releaseGroup = await progress.RunTask(
                    $"Creating release {version}",
                    async (report) => {
                        report(-1);
                        var result = await CreateReleaseGroupAsync(packageId, version, channel, serviceUrl, cancellationToken);
                        report(100);
                        return result;
                    });

                var backgroundTasks = new List<Task>();
                foreach (var assetTuple in filesToUpload) {
                    backgroundTasks.Add(
                        progress.RunTask(
                            $"Uploading {Path.GetFileName(assetTuple.Item1)}",
                            async (report) => {
                                await UploadReleaseAssetAsync(
                                    assetTuple.Item1,
                                    serviceUrl,
                                    releaseGroup.Id,
                                    assetTuple.Item2,
                                    report,
                                    cancellationToken);
                                report(100);
                            })
                    );
                }

                using var _1 = TempUtil.GetTempDirectory(out var deltaGenTempDir);
                var prevVersion = Path.Combine(deltaGenTempDir, "prev.nupkg");

                var prevZip = await progress.RunTask(
                    $"Downloading delta base for {version}",
                    async (report) => {
                        await DownloadLatestRelease(packageId, channel, serviceUrl, prevVersion, report, cancellationToken);
                        return new ZipPackage(prevVersion);
                    });

                if (prevZip.Version! >= version) {
                    throw new InvalidOperationException(
                        $"Latest version in channel {channel} is greater than or equal to local (remote={prevZip.Version}, local={version})");
                }

                var suggestedDeltaName = ReleaseEntryHelper.GetSuggestedReleaseName(packageId, version.ToFullString(), channel, true, RuntimeOs.Unknown);
                var deltaPath = Path.Combine(releaseDirectory, suggestedDeltaName);

                await progress.RunTask(
                    $"Building delta {prevZip.Version} -> {version}",
                    (report) => {
                        var delta = new DeltaPackageBuilder(Logger);
                        var pOld = new ReleasePackage(prevVersion);
                        var pNew = new ReleasePackage(fullAssetPath);
                        delta.CreateDeltaPackage(pOld, pNew, deltaPath, DeltaMode.BestSpeed, report);
                        report(100);
                        return Task.CompletedTask;
                    });

                backgroundTasks.Add(
                    progress.RunTask(
                        $"Uploading {Path.GetFileName(deltaPath)}",
                        async (report) => {
                            await UploadReleaseAssetAsync(
                                deltaPath,
                                serviceUrl,
                                releaseGroup.Id,
                                FileType.Release,
                                report,
                                cancellationToken);
                            report(100);
                        })
                );

                await Task.WhenAll(backgroundTasks);

                var publishedGroup = await progress.RunTask(
                    $"Publishing release {version}",
                    async (report) => {
                        report(-1);
                        var result = await PublishReleaseGroupAsync(releaseGroup, serviceUrl, cancellationToken);
                        report(100);
                        return result;
                    });

                if (!noWaitForLive) {
                    await progress.RunTask(
                        "Waiting for release to go live",
                        async (report) => {
                            report(-1);
                            await WaitUntilReleaseGroupLive(publishedGroup.Id, serviceUrl, cancellationToken);
                            report(100);
                        });
                }
            });
    }

    private async Task DownloadLatestRelease(string packageId, string channel, string? velopackBaseUrl, string localPath,
        Action<int> progress, CancellationToken cancellationToken)
    {
        var client = GetHttpClient(progress);
        var endpoint = GetEndpoint($"v1/download/{packageId}/{channel}", velopackBaseUrl) + $"?assetType=Full";

        using var fs = File.Create(localPath);

        var response = await client.GetAsync(endpoint, cancellationToken);
        response.EnsureSuccessStatusCode();

#if NET6_0_OR_GREATER
        await response.Content.CopyToAsync(fs, cancellationToken);
#else
        await response.Content.CopyToAsync(fs);
#endif
    }

    private async Task WaitUntilReleaseGroupLive(Guid releaseGroupId, string? velopackBaseUrl, CancellationToken cancellationToken)
    {
        var client = GetHttpClient();
        var endpoint = GetEndpoint($"v1/releaseGroups/{releaseGroupId}", velopackBaseUrl);

        for (int i = 0; i < 300; i++) {
            var response = await client.GetAsync(endpoint, cancellationToken);
            response.EnsureSuccessStatusCode();
            var releaseGroup = await response.Content.ReadFromJsonAsync<ReleaseGroup>(cancellationToken: cancellationToken);
            if (releaseGroup?.FileUploads == null) {
                Logger.LogWarning("Failed to get release group status, it may not be live yet.");
                return;
            }

            if (releaseGroup.FileUploads.All(f => f.Status?.ToLower().Equals("processed") == true)) {
                Logger.LogInformation("Release is now live.");
                return;
            }

            await Task.Delay(1000, cancellationToken);
        }

        Logger.LogWarning("Release did not go live within 5 minutes (timeout).");
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

        var client = GetHttpClient();
        var endpoint = GetEndpoint("v1/releaseGroups/create", velopackBaseUrl);
        var response = await client.PostAsJsonAsync(endpoint, request, cancellationToken);

        if (!response.IsSuccessStatusCode) {
            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Failed to create release group with version {version.ToNormalizedString()}" +
                $"{Environment.NewLine}Response status code: {response.StatusCode}{Environment.NewLine}{content}");
        }

        return await response.Content.ReadFromJsonAsync<ReleaseGroup>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException($"Failed to create release group with version {version.ToNormalizedString()}");
    }

    private async Task UploadReleaseAssetAsync(string filePath, string? velopackBaseUrl, Guid releaseGroupId,
        FileType fileType, Action<int> progress, CancellationToken cancellationToken)
    {
        using var formData = new MultipartFormDataContent();
        formData.Add(new StringContent(releaseGroupId.ToString()), "ReleaseGroupId");
        formData.Add(new StringContent(fileType.ToString()), "FileType");

        using var fileStream = File.OpenRead(filePath);

        using var fileContent = new StreamContent(fileStream);
        formData.Add(fileContent, "File", Path.GetFileName(filePath));

        var endpoint = GetEndpoint("v1/releases/upload", velopackBaseUrl);

        var client = GetHttpClient(progress);
        var response = await client.PostAsync(endpoint, formData, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private async Task<ReleaseGroup> PublishReleaseGroupAsync(
        ReleaseGroup releaseGroup, string? velopackBaseUrl, CancellationToken cancellationToken)
    {
        UpdateReleaseGroupRequest request = new() {
            State = ReleaseGroupState.Published
        };

        var client = GetHttpClient();
        var endpoint = GetEndpoint($"v1/releaseGroups/{releaseGroup.Id}", velopackBaseUrl);
        var response = await client.PutAsJsonAsync(endpoint, request, cancellationToken);

        if (!response.IsSuccessStatusCode) {
            string content = await response.Content.ReadAsStringAsync(cancellationToken);
            throw new InvalidOperationException(
                $"Failed to publish release group with id {releaseGroup.Id}.{Environment.NewLine}Response status code: {response.StatusCode}{Environment.NewLine}{content}");
        }

        return await response.Content.ReadFromJsonAsync<ReleaseGroup>(cancellationToken: cancellationToken)
               ?? throw new InvalidOperationException($"Failed to publish release group with id {releaseGroup.Id}");
    }

    private async Task<AuthConfiguration> GetAuthConfigurationAsync(VelopackServiceOptions? options, CancellationToken cancellationToken)
    {
        if (AuthConfiguration is not null)
            return AuthConfiguration;

        var endpoint = GetEndpoint("v1/auth/config", options);

        var client = GetHttpClient();
        var authConfig = await client.GetFromJsonAsync<AuthConfiguration>(endpoint, cancellationToken);
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
        if (Authorization is null) {
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

    private static async Task<AuthenticationResult?> AcquireInteractiveAsync(IPublicClientApplication pca, AuthConfiguration authConfiguration,
        CancellationToken cancellationToken)
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
            var result = await pca.AcquireTokenWithDeviceCode(
                Scopes,
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