using System.Runtime.Versioning;
using System.Text;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Squirrel.Packaging.Windows;

public class HelperExe : HelperFile
{
    public HelperExe(ILogger logger) : base(logger)
    {
    }

    public static string SetupPath => FindHelperFile("Setup.exe");

    public static string UpdatePath => FindHelperFile("Update.exe");

    public static string StubExecutablePath => FindHelperFile("StubExecutable.exe");

    private static string SignToolPath => FindHelperFile("signtool.exe");

    private static string RceditPath => FindHelperFile("rcedit.exe");

    [SupportedOSPlatform("windows")]
    private bool CheckIsAlreadySigned(string filePath)
    {
        if (String.IsNullOrWhiteSpace(filePath)) return true;

        if (!File.Exists(filePath)) {
            Log.Warn($"Cannot sign '{filePath}', file does not exist.");
            return true;
        }

        try {
            if (AuthenticodeTools.IsTrusted(filePath)) {
                Log.Debug($"'{filePath}' is already signed, skipping...");
                return true;
            }
        } catch (Exception ex) {
            Log.Error(ex, "Failed to determine signing status for " + filePath);
        }

        return false;
    }

    [SupportedOSPlatform("windows")]
    public void SignPEFilesWithSignTool(string rootDir, string[] filePaths, string signArguments, int parallelism)
    {
        Queue<string> pendingSign = new Queue<string>();

        foreach (var f in filePaths) {
            if (!CheckIsAlreadySigned(f)) {
                // try to find the path relative to rootDir
                if (String.IsNullOrEmpty(rootDir)) {
                    pendingSign.Enqueue(f);
                } else {
                    var partialPath = Utility.NormalizePath(f).Substring(Utility.NormalizePath(rootDir).Length).Trim('/', '\\');
                    pendingSign.Enqueue(partialPath);
                }
            } else {
                Log.Debug($"'{f}' is already signed, and will not be signed again.");
            }
        }

        if (filePaths.Length != pendingSign.Count) {
            var diff = filePaths.Length - pendingSign.Count;
            Log.Info($"{pendingSign.Count} files will be signed, {diff} will be skipped because they are already signed.");
        }

        var totalToSign = pendingSign.Count;
        var baseSignArgs = PlatformUtil.CommandLineToArgvW(signArguments);

        do {
            List<string> args = new List<string>();
            args.Add("sign");
            args.AddRange(baseSignArgs);
            for (int i = Math.Min(pendingSign.Count, parallelism); i > 0; i--) {
                args.Add(pendingSign.Dequeue());
            }

            var result = PlatformUtil.InvokeProcess(SignToolPath, args, rootDir, CancellationToken.None);
            if (result.ExitCode != 0) {
                var cmdWithPasswordHidden = new Regex(@"\/p\s+?[^\s]+").Replace(result.Command, "/p ********");
                Log.Debug($"Signing command failed: {cmdWithPasswordHidden}");
                throw new Exception(
                    $"Signing command failed. Specify --verbose argument to print signing command.\n\n" +
                    $"Output was:\n" + result.StdOutput);
            }

            Log.Info($"Signed {totalToSign - pendingSign.Count}/{totalToSign} successfully.\r\n" + result.StdOutput);

        } while (pendingSign.Count > 0);
    }

    public void SignPEFileWithTemplate(string filePath, string signTemplate)
    {
        if (SquirrelRuntimeInfo.IsWindows && CheckIsAlreadySigned(filePath)) {
            Log.Debug($"'{filePath}' is already signed, and will not be signed again.");
            return;
        }

        var command = signTemplate.Replace("\"{{file}}\"", "{{file}}").Replace("{{file}}", $"\"{filePath}\"");

        var result = PlatformUtil.InvokeProcess(command, null, null, CancellationToken.None);
        if (result.ExitCode != 0) {
            var cmdWithPasswordHidden = new Regex(@"\/p\s+?[^\s]+").Replace(result.Command, "/p ********");
            Log.Debug($"Signing command failed: {cmdWithPasswordHidden}");
            throw new Exception(
                $"Signing command failed. Specify --verbose argument to print signing command.\n\n" +
                $"Output was:\n" + result.StdOutput);
        }

        Log.Info("Sign successful: " + result.StdOutput);
    }

    [SupportedOSPlatform("windows")]
    public void SetExeIcon(string exePath, string iconPath)
    {
        Log.Info("Updating PE icon for: " + exePath);
        var args = new[] { Path.GetFullPath(exePath), "--set-icon", iconPath };
        Utility.Retry(() => InvokeAndThrowIfNonZero(RceditPath, args, null));
    }

    [SupportedOSPlatform("windows")]
    public void SetPEVersionBlockFromPackageInfo(string exePath, NuGet.IPackage package, string iconPath = null)
    {
        Log.Info("Updating StringTable resources for: " + exePath);
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

        Utility.Retry(() => InvokeAndThrowIfNonZero(RceditPath, args, null));
    }
}