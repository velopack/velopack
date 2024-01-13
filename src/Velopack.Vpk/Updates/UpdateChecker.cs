using System.Threading;

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
            var dl = new NugetDownloader(new NullNugetLogger());
            var isPre = myVer.IsPrerelease || myVer.HasMetadata;
            var package = await dl.GetPackageMetadata("vpk", isPre ? "pre" : "latest", cancel.Token).ConfigureAwait(false);
            if (package.Identity.Version > myVer) {
                if (isPre) {
                    _logger.Warn($"[bold underline]There is a newer version of vpk available ({package.Identity.Version}). Run 'dotnet tool update -g vpk'[/]");
                } else {
                    _logger.Warn($"[bold underline]There is a newer version of vpk available. Run 'dotnet tool update -g vpk --version {package.Identity.Version}'[/]");
                }
            } else {
                _logger.Debug($"vpk is up to date (latest online = {package.Identity.Version})");
            }
        } catch (Exception ex) {
            _logger.Debug(ex, "Failed to check for updates.");
        }
    }
}
