using System.Runtime.InteropServices;

namespace Velopack.CommandLine.Tests;

public class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            Skip = "Only run on Windows";
        }
    }
}
