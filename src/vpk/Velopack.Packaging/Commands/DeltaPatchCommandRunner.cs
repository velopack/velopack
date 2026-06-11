using Microsoft.Extensions.Logging;
using Velopack.Core;
using Velopack.Core.Abstractions;
using Velopack.Packaging.Compression;
using Velopack.Util;

namespace Velopack.Packaging.Commands;

public class DeltaPatchCommandRunner : ValidatedCommand<DeltaPatchOptions, DeltaPatchOptionsValidator>
{
    private readonly ILogger _logger;
    private readonly IFancyConsole _console;

    public DeltaPatchCommandRunner(ILogger logger, IFancyConsole console)
    {
        _logger = logger;
        _console = console;
    }

    protected override async Task RunCoreAsync(DeltaPatchOptions options)
    {
        var tmp = TempUtil.GetDefaultTempBaseDirectory();
        using var _1 = TempUtil.GetTempDirectory(out var workDir);

        var delta = new DeltaEmbedded(HelperFile.GetZstdPath(), _logger, tmp);
        var veloLogger = _logger.ToVelopackLogger();
        EasyZip.ExtractZipToDirectory(veloLogger, options.BasePackage, workDir);

        await _console.ExecuteProgressAsync(
            async (ctx) => {
                foreach (var f in options.PatchFiles) {
                    await ctx.RunTask(
                        $"Applying {f.Name}",
                        (progress) => {
                            delta.ApplyDeltaPackageFast(workDir, f.FullName, progress);
                            progress(100);
                            return Task.CompletedTask;
                        });
                }

                await ctx.RunTask(
                    $"Building {Path.GetFileName(options.OutputFile)}",
                    async (progress) => {
                        await EasyZip.CreateZipFromDirectoryAsync(veloLogger, options.OutputFile, workDir, progress);
                        progress(100);
                    });
            });
    }
}