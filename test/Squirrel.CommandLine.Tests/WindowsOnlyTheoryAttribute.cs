using System.Runtime.InteropServices;
using Xunit;

namespace Squirrel.CommandLine.Tests
{
    public class WindowsOnlyTheoryAttribute : TheoryAttribute
    {
        public WindowsOnlyTheoryAttribute()
        {
            if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows)) {
                Skip = "Only run on Windows";
            }
        }
    }
}
