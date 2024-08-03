using Microsoft.Extensions.Logging;
using Velopack.Packaging.Abstractions;

namespace Velopack.Packaging.Commands;

public class DeltaGenCommandRunner : ICommand<DeltaGenOptions>
{
    private readonly ILogger _logger;
    private readonly IFancyConsole _console;

    public DeltaGenCommandRunner(ILogger logger, IFancyConsole console)
    {
        _logger = logger;
        _console = console;
    }

    public async Task Run(DeltaGenOptions options)
    {
        await _console.ExecuteProgressAsync(async (ctx) => {
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
