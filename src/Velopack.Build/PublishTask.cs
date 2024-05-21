using System;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Velopack.Packaging.Flow;

namespace Velopack.Build;

public class PublishTask : MSBuildAsyncTask
{
    private static HttpClient HttpClient { get; } = new(new HmacAuthHttpClientHandler()) {
        Timeout = TimeSpan.FromMinutes(10)
    };

    [Required]
    public string ReleaseDirectory { get; set; } = "";

    public string ServiceUrl { get; set; } = VelopackServiceOptions.DefaultBaseUrl;

    public string? Channel { get; set; }

    public string? ApiKey { get; set; }

    protected override async Task<bool> ExecuteAsync()
    {
        //System.Diagnostics.Debugger.Launch();
        IVelopackFlowServiceClient client = new VelopackFlowServiceClient(HttpClient, Logger);
        if (!await client.LoginAsync(new() {
            AllowDeviceCodeFlow = false,
            AllowInteractiveLogin = false,
            VelopackBaseUrl = ServiceUrl,
            ApiKey = ApiKey
        }).ConfigureAwait(false)) {
            Logger.LogWarning("Not logged into Velopack service, skipping publish. Please run vpk login.");
            return true;
        }

        await client.UploadLatestReleaseAssetsAsync(Channel, ReleaseDirectory, ServiceUrl)
            .ConfigureAwait(false);

        return true;
    }
}
