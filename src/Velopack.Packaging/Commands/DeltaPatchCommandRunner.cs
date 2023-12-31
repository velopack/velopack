﻿using Microsoft.Extensions.Logging;
using Velopack.Compression;

namespace Velopack.Packaging.Commands
{
    public class DeltaPatchCommandRunner : ICommand<DeltaPatchOptions>
    {
        public Task Run(DeltaPatchOptions options, ILogger logger)
        {
            if (options.PatchFiles.Length == 0) {
                throw new ArgumentException("Must specify at least one patch file.");
            }

            if (options.PatchFiles.Any(x => x == null || !x.Exists)) {
                throw new ArgumentException("One or more patch files do not exist.");
            }

            var tmp = Utility.GetDefaultTempBaseDirectory();
            using var _1 = Utility.GetTempDirectory(out var workDir);
            var helper = new HelperFile(logger);

            string updateExe;
            if (VelopackRuntimeInfo.IsWindows)
                updateExe = helper.UpdatePath;
            else if (VelopackRuntimeInfo.IsOSX)
                updateExe = helper.UpdateMacPath;
            else
                throw new NotSupportedException("This platform is not supported.");

            var delta = new DeltaPackage(logger, tmp, updateExe);
            EasyZip.ExtractZipToDirectory(logger, options.BasePackage, workDir);

            foreach (var f in options.PatchFiles) {
                logger.Info($"Applying delta patch {f.Name}");
                delta.ApplyDeltaPackageFast(workDir, f.FullName);
            }

            EasyZip.CreateZipFromDirectory(logger, options.OutputFile, workDir);

            return Task.CompletedTask;
        }
    }
}
