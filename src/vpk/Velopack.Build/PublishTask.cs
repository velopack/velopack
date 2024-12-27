using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Build.Framework;
using Microsoft.Extensions.Logging;
using Velopack.Flow;

namespace Velopack.Build;

public class PublishTask : MSBuildAsyncTask
{
    [Required]
    public string ReleaseDirectory { get; set; } = "";

    public string? ServiceUrl { get; set; }

    public string? Channel { get; set; }

    public string? ApiKey { get; set; }

    public string? Timeout { get; set; }

    public bool WaitForLive { get; set; }

    protected override async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        double timeout;
        if (double.TryParse(Timeout, out var parsedTimeout)) {
            timeout = parsedTimeout;
        } else {
            timeout = 30d;
        }

        //System.Diagnostics.Debugger.Launch();
        var options = new VelopackFlowServiceOptions {
            VelopackBaseUrl = ServiceUrl,
            ApiKey = ApiKey,
            Timeout = timeout,
        };

        var loginOptions = new VelopackFlowLoginOptions() {
            AllowCacheCredentials = true,
            AllowDeviceCodeFlow = false,
            AllowInteractiveLogin = false,
        };

        var client = new VelopackFlowServiceClient(options, Logger, Logger);
        if (!await client.LoginAsync(loginOptions, false, cancellationToken).ConfigureAwait(false)) {
            Logger.LogWarning("Not logged into Velopack Flow service, skipping publish. Please run vpk login.");
            return true;
        }

        // todo: currently it's not possible to cross-compile for different OSes using Velopack.Build
        var targetOs = VelopackRuntimeInfo.SystemOs;

        await client.UploadLatestReleaseAssetsAsync(Channel, ReleaseDirectory, targetOs, WaitForLive, cancellationToken)
            .ConfigureAwait(false);

        return true;
    }
}