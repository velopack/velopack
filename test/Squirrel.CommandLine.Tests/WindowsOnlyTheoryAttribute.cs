using System.Runtime.InteropServices;

namespace Squirrel.CommandLine.Tests;

public class WindowsOnlyTheoryAttribute : TheoryAttribute
{
    public WindowsOnlyTheoryAttribute()
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            Skip = "Only run on Windows";
        }
    }
}
