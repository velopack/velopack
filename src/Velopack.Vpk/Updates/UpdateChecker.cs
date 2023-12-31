using System.Threading;
using Velopack.Packaging;

namespace Velopack.Vpk.Updates;

public class UpdateChecker
{
    private readonly ILogger _logger;

    public UpdateChecker(ILogger logger)
    {
        _logger = logger;
    }

    public async Task CheckForUpdates()
    {
        try {
            var cancel = new CancellationTokenSource(3000);
            var myVer = VelopackRuntimeInfo.VelopackNugetVersion;
            var dl = new NugetDownloader(new NugetLoggingWrapper(_logger));
            var package = await dl.GetPackageMetadata("csq", (myVer.IsPrerelease || myVer.HasMetadata) ? "pre" : "latest", cancel.Token).ConfigureAwait(false);
            if (package.Identity.Version > myVer)
                _logger.Warn($"There is a newer version of csq available ({package.Identity.Version})");
            else
                _logger.Debug($"csq is up to date (latest online = {package.Identity.Version})");
        } catch (Exception ex) {
            _logger.Debug(ex, "Failed to check for updates.");
        }
    }
}
