using System.Threading;
using NuGet.Protocol.Core.Types;
using Velopack.Core;
using Velopack.Packaging.NuGet;
using Velopack.Util;

namespace Velopack.Vpk.Updates;

public class UpdateChecker
{
    private readonly ILogger _logger;
    private readonly VelopackDefaults _defaults;
    private IPackageSearchMetadata _cache;

    public UpdateChecker(ILogger logger, VelopackDefaults defaults)
    {
        _logger = logger;
        _defaults = defaults;
    }

    public async Task<bool> CheckForUpdates()
    {
        if (_defaults.SkipUpdates) return false;
        try {
            var myVer = VelopackRuntimeInfo.VelopackNugetVersion;
            var isPre = myVer.IsPrerelease || myVer.HasMetadata;

            if (_cache == null) {
                var cancel = new CancellationTokenSource(3000);
                var dl = new NuGetDownloader();
                _cache = await dl.GetPackageMetadata("vpk", isPre ? "pre" : "latest", cancel.Token).ConfigureAwait(false);
            }

            var cacheVersion = _cache.Identity.Version;
            if (cacheVersion > myVer) {
                if (!isPre) {
                    _logger.Warn($"[bold]There is a newer version of vpk available ({cacheVersion}). Run 'dotnet tool update -g vpk'[/]");
                } else {
                    _logger.Warn($"[bold]There is a newer version of vpk available. Run 'dotnet tool update -g vpk --version {cacheVersion}'[/]");
                }

                return true;
            } else {
                _logger.Debug($"vpk is up to date (latest online = {cacheVersion})");
            }
        } catch (Exception ex) {
            _logger.Debug(ex, "Failed to check for updates.");
        }

        return false;
    }
}