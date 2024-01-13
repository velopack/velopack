using Microsoft.Extensions.Logging;

namespace Velopack.Packaging.Commands
{
    public class DeltaGenCommandRunner : ICommand<DeltaGenOptions>
    {
        private readonly ILogger _logger;

        public DeltaGenCommandRunner(ILogger logger)
        {
            _logger = logger;
        }

        public async Task Run(DeltaGenOptions options)
        {
            await Progress.ExecuteAsync(_logger, async (ctx) => {
                var pold = new ReleasePackage(options.BasePackage);
                var pnew = new ReleasePackage(options.NewPackage);
                await ctx.RunTask($"Building delta {pold.Version} -> {pnew.Version}", (progress) => {
                    var delta = new DeltaPackageBuilder(_logger);
                    delta.CreateDeltaPackage(pold, pnew, options.OutputFile, options.DeltaMode, progress);
                    progress(100);
                    return Task.CompletedTask;
                });
            });
        }
    }
}
