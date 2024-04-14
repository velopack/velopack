using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using Velopack.Packaging;
using Velopack.Packaging.Flow;

namespace Velopack.Build;

public class PublishTask : MSBuildAsyncTask
{
    private static HttpClient HttpClient { get; } = new(new HmacAuthHttpClientHandler());

    [Required]
    public string ReleaseDirectory { get; set; } = "";

    public string ServiceUrl { get; set; } = VelopackServiceOptions.DefaultBaseUrl;

    public string? Channel { get; set; }

    public string? Version { get; set; }

    public string? ApiKey { get; set; }

    protected override async Task<bool> ExecuteAsync()
    {
        //System.Diagnostics.Debugger.Launch();
        VelopackFlowServiceClient client = new(HttpClient, Logger);
        if (!await client.LoginAsync(new() {
            AllowDeviceCodeFlow = false,
            AllowInteractiveLogin = false,
            VelopackBaseUrl = ServiceUrl,
            ApiKey = ApiKey
        }).ConfigureAwait(false)) {
            Logger.LogWarning("Not logged into Velopack service, skipping publish. Please run vpk login.");
            return true;
        }

        Channel ??= ReleaseEntryHelper.GetDefaultChannel(VelopackRuntimeInfo.SystemOs);
        ReleaseEntryHelper helper = new(ReleaseDirectory, Channel, Logger);
        var latestAssets = helper.GetLatestAssets().ToList();

        List<string> installers = [];

        List<string> files = latestAssets.Select(x => x.FileName).ToList();
        string? packageId = null;
        SemanticVersion? version = null;
        if (latestAssets.Count > 0) {
            packageId = latestAssets[0].PackageId;
            version = latestAssets[0].Version;

            if (VelopackRuntimeInfo.IsWindows || VelopackRuntimeInfo.IsOSX) {
                var setupName = ReleaseEntryHelper.GetSuggestedSetupName(packageId, Channel);
                if (File.Exists(Path.Combine(ReleaseDirectory, setupName))) {
                    installers.Add(setupName);
                }
            }

            var portableName = ReleaseEntryHelper.GetSuggestedPortableName(packageId, Channel);
            if (File.Exists(Path.Combine(ReleaseDirectory, portableName))) {
                installers.Add(portableName);
            }
        }

        Logger.LogInformation("Preparing to upload {AssetCount} assets to Velopack ({ServiceUrl})", latestAssets.Count + installers.Count, ServiceUrl);

        foreach (var assetFileName in files) {

            var latestPath = Path.Combine(ReleaseDirectory, assetFileName);

            using var fileStream = File.OpenRead(latestPath);
            var options = new UploadOptions(fileStream, assetFileName, Channel) {
                VelopackBaseUrl = ServiceUrl
            };

            await client.UploadReleaseAssetAsync(options).ConfigureAwait(false);

            Logger.LogInformation("Uploaded {FileName} to Velopack", assetFileName);
        }

        foreach (var installerFile in installers) {
            var latestPath = Path.Combine(ReleaseDirectory, installerFile);

            using var fileStream = File.OpenRead(latestPath);
            var options = new UploadInstallerOptions(packageId!, version!, fileStream, installerFile, Channel) {
                VelopackBaseUrl = ServiceUrl
            };

            await client.UploadInstallerAssetAsync(options).ConfigureAwait(false);

            Logger.LogInformation("Uploaded {FileName} installer to Velopack", installerFile);
        }
        return true;

    }
}
