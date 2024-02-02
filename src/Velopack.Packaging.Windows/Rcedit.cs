using System.Runtime.Versioning;
using Velopack.NuGet;

namespace Velopack.Packaging.Windows;

[SupportedOSPlatform("windows")]
public class Rcedit
{
    public static void SetExeIcon(string exePath, string iconPath)
    {
        var args = new[] { Path.GetFullPath(exePath), "--set-icon", iconPath };
        Utility.Retry(() => Exe.InvokeAndThrowIfNonZero(HelperFile.RceditPath, args, null));
    }

    [SupportedOSPlatform("windows")]
    public static void SetPEVersionBlockFromPackageInfo(string exePath, PackageManifest package, string iconPath = null)
    {
        var realExePath = Path.GetFullPath(exePath);

        List<string> args = new List<string>() {
            realExePath,
            "--set-version-string", "CompanyName", package.ProductCompany,
            "--set-version-string", "LegalCopyright", package.ProductCopyright,
            "--set-version-string", "FileDescription", package.ProductDescription,
            "--set-version-string", "ProductName", package.ProductName,
            "--set-file-version", package.Version.ToString(),
            "--set-product-version", package.Version.ToString(),
        };

        if (iconPath != null) {
            args.Add("--set-icon");
            args.Add(Path.GetFullPath(iconPath));
        }

        Utility.Retry(() => Exe.InvokeAndThrowIfNonZero(HelperFile.RceditPath, args, null));
    }
}
