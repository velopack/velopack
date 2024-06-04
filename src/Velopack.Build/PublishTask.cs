using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Velopack.Packaging.Flow;

namespace Velopack.Build;

public class PublishTask : MSBuildAsyncTask
{
    private static HttpClient HttpClient { get; } = new(new HmacAuthHttpClientHandler {
        InnerHandler = new HttpClientHandler()
    }) {
        Timeout = TimeSpan.FromMinutes(10)
    };

    [Required]
    public string ReleaseDirectory { get; set; } = "";

    public string ServiceUrl { get; set; } = VelopackServiceOptions.DefaultBaseUrl;

    public string? Channel { get; set; }

    public string? ApiKey { get; set; }

    protected override async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        //System.Diagnostics.Debugger.Launch();
        IVelopackFlowServiceClient client = new VelopackFlowServiceClient(HttpClient, Logger);
        if (!await client.LoginAsync(new() {
            AllowDeviceCodeFlow = false,
            AllowInteractiveLogin = false,
            VelopackBaseUrl = ServiceUrl,
            ApiKey = ApiKey
        }, cancellationToken).ConfigureAwait(false)) {
            Logger.LogWarning("Not logged into Velopack Flow service, skipping publish. Please run vpk login.");
            return true;
        }

        // todo: currently it's not possible to cross-compile for different OSes using Velopack.Build
        var targetOs = VelopackRuntimeInfo.SystemOs;

        await client.UploadLatestReleaseAssetsAsync(Channel, ReleaseDirectory, ServiceUrl, targetOs, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }
}
