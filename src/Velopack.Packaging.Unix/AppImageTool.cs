using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using System.Threading.Tasks;

namespace Velopack.Packaging.Unix
{
    public class AppImageTool
    {
        [SupportedOSPlatform("linux")]
        public static void CreateLinuxAppImage(string appDir, string outputFile)
        {
            var tool = HelperFile.AppImageToolX64;
            Chmod.ChmodFileAsExecutable(tool);
            Exe.InvokeAndThrowIfNonZero(tool, new[] { appDir, outputFile }, null);
            Chmod.ChmodFileAsExecutable(outputFile);
        }
    }
}
