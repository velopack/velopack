using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;

namespace Velopack.Packaging.Windows;

public class HelperExe : HelperFile
{
    public HelperExe(ILogger logger) : base(logger)
    {
    }

    public string SetupPath => FindHelperFile("Setup.exe");

    public string StubExecutablePath => FindHelperFile("StubExecutable.exe");

    private string SignToolPath => FindHelperFile("signtool.exe");

    private string RceditPath => FindHelperFile("rcedit.exe");

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
        var baseSignArgs = CommandLineToArgvW(signArguments);

        do {
            List<string> args = new List<string>();
            args.Add("sign");
            args.AddRange(baseSignArgs);
            for (int i = Math.Min(pendingSign.Count, parallelism); i > 0; i--) {
                args.Add(pendingSign.Dequeue());
            }

            var result = InvokeProcess(SignToolPath, args, rootDir);
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
        if (VelopackRuntimeInfo.IsWindows && CheckIsAlreadySigned(filePath)) {
            Log.Debug($"'{filePath}' is already signed, and will not be signed again.");
            return;
        }

        var command = signTemplate.Replace("\"{{file}}\"", "{{file}}").Replace("{{file}}", $"\"{filePath}\"");

        var result = InvokeProcess(command, null, null);
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

    private const string WIN_KERNEL32 = "kernel32.dll";
    private const string WIN_SHELL32 = "shell32.dll";

    [SupportedOSPlatform("windows")]
    [DllImport(WIN_KERNEL32, EntryPoint = "LocalFree", SetLastError = true)]
    private static extern IntPtr _LocalFree(IntPtr hMem);

    [SupportedOSPlatform("windows")]
    [DllImport(WIN_SHELL32, EntryPoint = "CommandLineToArgvW", CharSet = CharSet.Unicode)]
    private static extern IntPtr _CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string cmdLine, out int numArgs);

    [SupportedOSPlatform("windows")]
    protected static string[] CommandLineToArgvW(string cmdLine)
    {
        IntPtr argv = IntPtr.Zero;
        try {
            argv = _CommandLineToArgvW(cmdLine, out var numArgs);
            if (argv == IntPtr.Zero) {
                throw new Win32Exception();
            }
            var result = new string[numArgs];

            for (int i = 0; i < numArgs; i++) {
                IntPtr currArg = Marshal.ReadIntPtr(argv, i * Marshal.SizeOf(typeof(IntPtr)));
                result[i] = Marshal.PtrToStringUni(currArg);
            }

            return result;
        } finally {
            _LocalFree(argv);
        }
    }
}