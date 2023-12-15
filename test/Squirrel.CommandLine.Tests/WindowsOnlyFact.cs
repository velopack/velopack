using System.Runtime.InteropServices;

namespace Squirrel.CommandLine.Tests;

public class WindowsOnlyFactAttribute : FactAttribute
{
    public WindowsOnlyFactAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            Skip = "Only run on Windows";
        }
    }
}
