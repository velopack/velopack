using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Velopack.Packaging.Exceptions;

namespace Velopack.Packaging.Unix
{
    public class AppImageTool
    {
        [SupportedOSPlatform("linux")]
        public static void CreateLinuxAppImage(string appDir, string outputFile, RuntimeCpu machine, ILogger logger)
        {
            var tool = HelperFile.AppImageToolX64;

            string arch = machine switch {
                RuntimeCpu.x86 => "i386",
                RuntimeCpu.x64 => "x86_64",
                RuntimeCpu.arm64 => "arm_aarch64",
                _ => throw new ArgumentOutOfRangeException(nameof(machine), machine, null)
            };

            var envVar = new Dictionary<string, string>() {
                { "ARCH", arch }
            };
            
            logger.Info("About to create .AppImage for architecture: " + arch);

            Chmod.ChmodFileAsExecutable(tool);
            Exe.InvokeAndThrowIfNonZero(tool, new[] { appDir, outputFile }, null, envVar);
            Chmod.ChmodFileAsExecutable(outputFile);
        }
    }
}
