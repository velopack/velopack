using Microsoft.Extensions.Logging;
using Velopack.Compression;
using Velopack.Packaging.Exceptions;
using Velopack.Packaging.Abstractions;

namespace Velopack.Packaging.Commands
{
    public class DeltaPatchCommandRunner : ICommand<DeltaPatchOptions>
    {
        private readonly ILogger _logger;
        private readonly IFancyConsole _console;

        public DeltaPatchCommandRunner(ILogger logger, IFancyConsole console)
        {
            _logger = logger;
            _console = console;
        }

        public async Task Run(DeltaPatchOptions options)
        {
            if (options.PatchFiles.Length == 0) {
                throw new UserInfoException("Must specify at least one patch file.");
            }

            foreach (var p in options.PatchFiles) {
                if (p == null || !p.Exists) {
                    throw new UserInfoException($"Patch file '{p.FullName}' does not exist.");
                }
            }

            var tmp = Utility.GetDefaultTempBaseDirectory();
            using var _1 = Utility.GetTempDirectory(out var workDir);

            var delta = new DeltaEmbedded(HelperFile.GetZstdPath(), _logger, tmp);
            EasyZip.ExtractZipToDirectory(_logger, options.BasePackage, workDir);

            await _console.ExecuteProgressAsync(async (ctx) => {
                foreach (var f in options.PatchFiles) {
                    await ctx.RunTask($"Applying {f.Name}", (progress) => {
                        delta.ApplyDeltaPackageFast(workDir, f.FullName, progress);
                        progress(100);
                        return Task.CompletedTask;
                    });
                }
                await ctx.RunTask($"Building {Path.GetFileName(options.OutputFile)}", async (progress) => {
                    await EasyZip.CreateZipFromDirectoryAsync(_logger, options.OutputFile, workDir, progress);
                    progress(100);
                });
            });
        }
    }
}
