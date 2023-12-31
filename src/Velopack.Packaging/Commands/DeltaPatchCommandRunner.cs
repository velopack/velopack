using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Squirrel.Compression;

namespace Squirrel.Packaging.Commands
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
            if (SquirrelRuntimeInfo.IsWindows)
                updateExe = helper.UpdatePath;
            else if (SquirrelRuntimeInfo.IsOSX)
                updateExe = helper.UpdateMacPath;
            else
                throw new NotSupportedException("This platform is not supported.");

            var delta = new DeltaPackage(logger, tmp, updateExe);
            EasyZip.ExtractZipToDirectory(logger, options.BasePackage, workDir);

            foreach (var f in options.PatchFiles) {
                logger.Info($"Applying delta patch {f.Name}");
                delta.ApplyDeltaPackageFast(workDir, f.FullName);
            }

            return Task.CompletedTask;
        }
    }
}
