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

    public double Timeout { get; set; } = 30d;

    public bool WaitForLive { get; set; }

    protected override async Task<bool> ExecuteAsync(CancellationToken cancellationToken)
    {
        //System.Diagnostics.Debugger.Launch();
        var options = new VelopackFlowServiceOptions {
            VelopackBaseUrl = ServiceUrl,
            ApiKey = ApiKey,
            Timeout = Timeout,
        };

        var loginOptions = new VelopackFlowLoginOptions() {
            AllowCacheCredentials = true,
            AllowDeviceCodeFlow = false,
            AllowInteractiveLogin = false,
        };

        var client = new VelopackFlowServiceClient(options, Logger, Logger);
        CancellationToken token = CancellationToken.None;
        if (!await client.LoginAsync(loginOptions, false, token)) {
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