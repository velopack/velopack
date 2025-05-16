// ReSharper disable UseAwaitUsing

extern alias HttpFormatting;
using Microsoft.Identity.Client;
using Microsoft.Identity.Client.Extensions.Msal;
using NuGet.Versioning;
using Microsoft.Extensions.Logging;
using System.Text;
using System.Net.Http.Headers;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.NuGet;
using Velopack.Packaging;
using Velopack.Util;
using System.Net;
using Velopack.Packaging.Compression;

#if !NET6_0_OR_GREATER
using System.Net.Http;
#endif

namespace Velopack.Flow;

public class VelopackFlowServiceClient(
    VelopackFlowServiceOptions Options,
    ILogger Logger,
    IFancyConsole Console)
{
    private static readonly string[] Scopes = ["openid", "offline_access"];

    private AuthenticationHeaderValue? Authorization = null;

    private AuthConfiguration? AuthConfiguration { get; set; }

    public async Task<bool> LoginAsync(VelopackFlowLoginOptions? loginOptions, bool suppressOutput, CancellationToken cancellationToken)
    {
        loginOptions ??= new VelopackFlowLoginOptions();

        var authConfiguration = await GetAuthConfigurationAsync(suppressOutput, cancellationToken);
        var pca = await BuildPublicApplicationAsync(authConfiguration);

        if (!string.IsNullOrWhiteSpace(Options.ApiKey)) {
            Authorization = new(HmacHelper.HmacScheme, Options.ApiKey);
        } else {
            AuthenticationResult? rv = null;
            if (loginOptions.AllowCacheCredentials) {
                rv = await AcquireSilentlyAsync(pca, cancellationToken);
            }

            if (rv is null && loginOptions.AllowInteractiveLogin) {
                rv = await AcquireInteractiveAsync(pca, authConfiguration, cancellationToken);
            }

            if (rv is null && loginOptions.AllowDeviceCodeFlow) {
                rv = await AcquireByDeviceCodeAsync(pca, cancellationToken);
            }

            if (rv is null) {
                if (!loginOptions.AllowInteractiveLogin && !loginOptions.AllowDeviceCodeFlow) {
                    Logger.LogError("Failed to login to Velopack, no credentials found or they have expired. Please run 'vpk login' to authenticate.");
                } else {
                    Logger.LogError("Failed to login to Velopack");
                }

                return false;
            }

            Authorization = new("Bearer", rv.IdToken ?? rv.AccessToken);
        }

        var profile = await GetProfileAsync(GetFlowApi(), cancellationToken);

        if (!suppressOutput) {
            Logger.LogInformation("{UserName} logged into Velopack", profile?.GetDisplayName());
        }

        return true;
    }

    public async Task LogoutAsync(CancellationToken cancellationToken)
    {
        var authConfiguration = await GetAuthConfigurationAsync(false, cancellationToken);

        var pca = await BuildPublicApplicationAsync(authConfiguration);

        // clear the cache
        while ((await pca.GetAccountsAsync()).FirstOrDefault() is { } account) {
            await pca.RemoveAsync(account);
            Logger.LogInformation("Logged out of {Username}", account.Username);
        }

        Logger.LogInformation("Cleared saved login(s) for Velopack");
    }

    public async Task<string> InvokeEndpointAsync(
        string endpointUri,
        string method,
        string? body,
        CancellationToken cancellationToken)
    {
        AssertAuthenticated();

        var client = GetHttpClient();
        Uri baseUri = new(GetFlowApi().BaseUrl);
        Uri endpoint = new(baseUri, endpointUri);

        HttpRequestMessage request = new(new HttpMethod(method), endpoint);
        if (body is not null) {
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
        }

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

    public async Task UploadLatestReleaseAssetsAsync(string? channel, string releaseDirectory,
        RuntimeOs os, bool waitForLive, int tieredRolloutPercentage, CancellationToken cancellationToken)
    {
        AssertAuthenticated();

        channel ??= DefaultName.GetDefaultChannel(os);
        BuildAssets assets = BuildAssets.Read(releaseDirectory, channel);
        var fullAsset = assets.GetReleaseEntries().SingleOrDefault(a => a.Type == Velopack.VelopackAssetType.Full);

        if (fullAsset is null) {
            Logger.LogError("No full asset found in release directory {ReleaseDirectory} (or it's missing from assets file)", releaseDirectory);
            return;
        }

        var fullAssetPath = Path.Combine(releaseDirectory, fullAsset.FileName);
        var packageId = fullAsset.PackageId;
        var version = fullAsset.Version;

        var filesToUpload = assets.GetAssets()
            .Where(p => p.Type is not Velopack.VelopackAssetType.Delta)
            .Select(p => (p.Path, p.Type.ToFileType()))
            .ToArray();

        Logger.LogInformation("Beginning upload to Velopack Flow");

        FlowApi client = GetFlowApi();

        try {
            await Console.ExecuteProgressAsync(
                async (progress) => {
                    ReleaseGroup releaseGroup = await progress.RunTask(
                        $"Creating release {version}",
                        async (report) => {
                            report(-1);
                            await CreateChannelIfNotExists(client, packageId, channel, cancellationToken);
                            report(50);
                            var result = await CreateReleaseGroupAsync(client, packageId, version, channel, tieredRolloutPercentage / 100.0, cancellationToken);
                            Logger.LogInformation("Created release {Version} ({ReleaseGroupId})", version, result.Id);
                            report(100);
                            return result;
                        });

                    var backgroundTasks = new List<Task>();
                    foreach (var (filePath, fileType) in filesToUpload) {
                        backgroundTasks.Add(
                            progress.RunTask(
                                $"Uploading {Path.GetFileName(filePath)}",
                                async (report) => {
                                    await UploadReleaseAssetAsync(
                                        filePath,
                                        releaseGroup.Id,
                                        fileType,
                                        report,
                                        cancellationToken);
                                    report(100);
                                })
                        );
                    }

                    using var _1 = TempUtil.GetTempDirectory(out var deltaGenTempDir);
                    var prevVersion = Path.Combine(deltaGenTempDir, "prev.nupkg");

                    ZipPackage? prevZip = await progress.RunTask(
                        $"Downloading delta base for {version}",
                        async (report) => {
                            return await DownloadLatestRelease(packageId, channel, prevVersion, report, cancellationToken);
                        });

                    if (prevZip is not null) {
                        if (prevZip.Version! >= version) {
                            throw new InvalidOperationException(
                                $"Latest version in channel {channel} is greater than or equal to local (remote={prevZip.Version}, local={version})");
                        }

                        var suggestedDeltaName = DefaultName.GetSuggestedReleaseName(packageId, version.ToFullString(), channel, true, RuntimeOs.Unknown);
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
                                        releaseGroup.Id,
                                        FileType.Delta,
                                        report,
                                        cancellationToken);
                                    report(100);
                                })
                        );
                    }

                    await Task.WhenAll(backgroundTasks);

                    var publishedGroup = await progress.RunTask(
                        $"Publishing release {version}",
                        async (report) => {
                            report(-1);
                            var result = await PublishReleaseGroupAsync(client, releaseGroup.Id, cancellationToken);
                            report(100);
                            return result;
                        });

                    if (waitForLive) {
                        await progress.RunTask(
                            "Waiting for release to go live",
                            async (report) => {
                                report(-1);
                                await WaitUntilReleaseGroupLive(client, publishedGroup.Id, cancellationToken);
                                report(100);
                            });
                    }
                });
        } catch (ApiException e) {
            var userInfo = e.ToErrorResult()?.ToUserInfoException();
            if (userInfo is not null) {
                throw userInfo;
            } else {
                throw;
            }
        }
    }

    private async Task<Profile?> GetProfileAsync(FlowApi client, CancellationToken cancellationToken)
    {
        AssertAuthenticated();
        return await client.GetUserProfileAsync(cancellationToken);
    }

    private async Task<ZipPackage?> DownloadLatestRelease(string packageId, string channel, string localPath, Action<int> progress,
        CancellationToken cancellationToken)
    {
        try {
            var client = GetFlowApi(progress);
            using (var localFile = File.Create(localPath)) {
                using var file = await client.DownloadInstallerLatestAsync(packageId, channel, DownloadAssetType.Full, cancellationToken);
                await file.Stream.CopyToAsync(localFile, 81920, cancellationToken).ConfigureAwait(false);
            }

            return new ZipPackage(localPath);
        } catch (ApiException e) when (e.StatusCode == (int) HttpStatusCode.NotFound) {
            return null;
        }
    }

    private async Task WaitUntilReleaseGroupLive(FlowApi client, Guid releaseGroupId, CancellationToken cancellationToken)
    {
        for (int i = 0; i < 300; i++) {
            var releaseGroup = await client.GetReleaseGroupAsync(releaseGroupId, cancellationToken);

            if (releaseGroup.ProcessingState is ReleaseGroupProcessingState.Completed) {
                Logger.LogInformation("Release is now live.");
                return;
            }

            if (releaseGroup.ProcessingState is ReleaseGroupProcessingState.Failed) {
                foreach (var file in releaseGroup.FileUploads) {
                    if (file.State == FileUploadState.Failed) {
                        Logger.LogError("File {FileName} failed to upload: {Error}", file.FileName, file.StateMessage);
                    }
                }

                throw new UserInfoException("There were one or more errors publishing this release.");
            }

            await Task.Delay(1000, cancellationToken);
        }

        Logger.LogWarning("Release did not go live within 5 minutes (timeout).");
    }

    private static async Task CreateChannelIfNotExists(FlowApi client, string packageId, string channel, CancellationToken cancellationToken)
    {
        var request = new CreateChannelRequest() {
            PackageId = packageId,
            Name = channel,
        };
        await client.CreateChannelAsync(request, cancellationToken);
    }

    private static async Task<ReleaseGroup> CreateReleaseGroupAsync(FlowApi client, string packageId, SemanticVersion version, string channel,
        double tieredRolloutPercentage, CancellationToken cancellationToken)
    {
        CreateReleaseGroupRequest request = new() {
            ChannelIdentifier = channel,
            PackageId = packageId,
            Version = version.ToNormalizedString(),
            TieredRolloutPercentage = tieredRolloutPercentage
        };

        return await client.CreateReleaseGroupAsync(request, cancellationToken);
    }

    private async Task UploadReleaseAssetAsync(string filePath, Guid releaseGroupId, FileType fileType, Action<int> progress,
        CancellationToken cancellationToken)
    {
        var client = GetFlowApi(progress);
        using var stream = File.OpenRead(filePath);
        var file = new FileParameter(stream, Path.GetFileName(filePath));
        await client.UploadReleaseAsync(releaseGroupId, fileType, file, cancellationToken);
    }

    private static async Task<ReleaseGroup> PublishReleaseGroupAsync(FlowApi client, Guid releaseGroupId, CancellationToken cancellationToken)
    {
        UpdateReleaseGroupRequest request = new() {
            State = ReleaseGroupPublishState.Published,
        };

        return await client.UpdateReleaseGroupAsync(releaseGroupId, request, cancellationToken);
    }

    private async Task<AuthConfiguration> GetAuthConfigurationAsync(bool suppressOutput, CancellationToken cancellationToken)
    {
        FlowApi client = GetFlowApi();

        if (!suppressOutput) {
            Logger.LogInformation("Preparing to login to Velopack ({serviceUrl})", client.BaseUrl);
        }

        if (AuthConfiguration is { } authConfiguration)
            return authConfiguration;

        var authConfig = await client.GetV1AuthConfigAsync(cancellationToken);

        if (authConfig is null)
            throw new Exception("Failed to get auth configuration.");
        if (authConfig.B2CAuthority is null)
            throw new Exception("B2C Authority not provided.");
        if (authConfig.RedirectUri is null)
            throw new Exception("Redirect URI not provided.");
        if (authConfig.ClientId is null)
            throw new Exception("Client ID not provided.");

        return AuthConfiguration = authConfig;
    }

    private void AssertAuthenticated()
    {
        if (Authorization is null) {
            throw new UserInfoException($"{nameof(VelopackFlowServiceClient)} has not been authenticated, call {nameof(LoginAsync)} first.");
        }
    }

    private HttpClient GetHttpClient(Action<int>? progress = null)
    {
        HttpMessageHandler handler;

        if (progress != null) {
            var ph = new HttpFormatting::System.Net.Http.Handlers.ProgressMessageHandler(
                new HmacAuthHttpClientHandler(new HttpClientHandler() { AllowAutoRedirect = true }));
            ph.HttpSendProgress += (_, args) => {
                progress(args.ProgressPercentage);
                // Console.WriteLine($"upload progress: {((double)args.BytesTransferred / args.TotalBytes) * 100.0}");
            };
            ph.HttpReceiveProgress += (_, args) => {
                progress(args.ProgressPercentage);
            };
            handler = ph;
        } else {
            handler = new HmacAuthHttpClientHandler(new HttpClientHandler() { AllowAutoRedirect = true });
        }

        var client = new HttpClient(handler);
        client.DefaultRequestHeaders.Authorization = Authorization;
        client.Timeout = TimeSpan.FromMinutes(Options.Timeout);
        return client;
    }

    private FlowApi GetFlowApi(Action<int>? progress = null)
    {
        var client = GetHttpClient(progress);
        var api = new FlowApi(client);
        if (!String.IsNullOrWhiteSpace(Options.VelopackBaseUrl)) {
            api.BaseUrl = Options.VelopackBaseUrl;
        }

        return api;
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
            .WithLogging(
                (Microsoft.Identity.Client.LogLevel level, string message, bool containsPii) => System.Console.WriteLine($"[{level}]: {message}"),
                enablePiiLogging: true,
                enableDefaultPlatformLogging: true)
#endif
            .WithClientName("velopack")
            .Build();

        cacheHelper.RegisterCache(pca.UserTokenCache);
        return pca;
    }
}