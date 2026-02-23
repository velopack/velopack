using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Velopack.CommandLine.Tests;

public class WindowsOnlyTheoryAttribute : TheoryAttribute
{
    public WindowsOnlyTheoryAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = 0)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
            Skip = "Only run on Windows";
        }
    }
}
