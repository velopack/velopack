using Microsoft.Extensions.Logging;

namespace Velopack.Packaging.Commands
{
    public class DeltaGenCommandRunner : ICommand<DeltaGenOptions>
    {
        public Task Run(DeltaGenOptions options, ILogger logger)
        {
            var pold = new ReleasePackageBuilder(logger, options.BasePackage);
            var pnew = new ReleasePackageBuilder(logger, options.NewPackage);
            var delta = new DeltaPackageBuilder(logger);
            delta.CreateDeltaPackage(pnew, pold, options.OutputFile, options.DeltaMode);
            return Task.CompletedTask;
        }
    }
}
