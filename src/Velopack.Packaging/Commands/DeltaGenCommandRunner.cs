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

        public Task Run(DeltaGenOptions options)
        {
            var pold = new ReleasePackage(options.BasePackage);
            var pnew = new ReleasePackage(options.NewPackage);
            var delta = new DeltaPackageBuilder(_logger);
            delta.CreateDeltaPackage(pnew, pold, options.OutputFile, options.DeltaMode, (x) => { });
            return Task.CompletedTask;
        }
    }
}
