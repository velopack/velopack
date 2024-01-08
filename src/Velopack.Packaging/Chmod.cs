using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Velopack.Packaging;

[SupportedOSPlatform("linux")]
[SupportedOSPlatform("macos")]
public class Chmod
{
    private const string OSX_CSTD_LIB = "libSystem.dylib";
    private const string NIX_CSTD_LIB = "libc";

    [SupportedOSPlatform("osx")]
    [DllImport(OSX_CSTD_LIB, EntryPoint = "chmod", SetLastError = true)]
    private static extern int osx_chmod(string pathname, int mode);

    [SupportedOSPlatform("linux")]
    [DllImport(NIX_CSTD_LIB, EntryPoint = "chmod", SetLastError = true)]
    private static extern int nix_chmod(string pathname, int mode);

    public static void ChmodFileAsExecutable(string filePath)
    {
        Func<string, int, int> chmod;

        if (VelopackRuntimeInfo.IsOSX) chmod = osx_chmod;
        else if (VelopackRuntimeInfo.IsLinux) chmod = nix_chmod;
        else return; // no-op on windows, all .exe files can be executed.

        var filePermissionOctal = Convert.ToInt32("777", 8);
        const int EINTR = 4;
        int chmodReturnCode;

        do {
            chmodReturnCode = chmod(filePath, filePermissionOctal);
        } while (chmodReturnCode == -1 && Marshal.GetLastWin32Error() == EINTR);

        if (chmodReturnCode == -1) {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"Could not set file permission {filePermissionOctal} for {filePath}.");
        }
    }
}
