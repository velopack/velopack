using System.Runtime.Versioning;
using System.Text;
using Microsoft.Extensions.Logging;

namespace Velopack.Packaging;

public static class HelperFile
{
    private static string GetUpdateExeName(RID target, ILogger log)
    {
        switch (target.BaseRID) {
        case RuntimeOs.Windows:
            return FindHelperFile("update.exe");
#if DEBUG
        case RuntimeOs.Linux:
            return FindHelperFile("update");
        case RuntimeOs.OSX:
            return FindHelperFile("update");
#else
        case RuntimeOs.Linux:
            if (!target.HasArchitecture) {
                log.LogWarning("No architecture specified with --runtime, defaulting to x64. If this was not intended please specify via the --runtime parameter");
                return FindHelperFile("UpdateNix_x64");
            }

            return target.Architecture switch {
                RuntimeCpu.arm64 => FindHelperFile("UpdateNix_arm64"),
                RuntimeCpu.x64 => FindHelperFile("UpdateNix_x64"),
                _ => throw new PlatformNotSupportedException($"Update binary is not available for this platform ({target}).")
            };
        case RuntimeOs.OSX:
            return FindHelperFile("UpdateMac");
#endif
        }

        throw new PlatformNotSupportedException($"Update binary is not available for this platform ({target}).");
    }

    public static string GetUpdatePath(RID target, ILogger log) => FindHelperFile(GetUpdateExeName(target, log));

    public static string GetZstdPath()
    {
        if (VelopackRuntimeInfo.IsWindows)
            return FindHelperFile("zstd.exe");
        Exe.AssertSystemBinaryExists("zstd", "sudo apt install zstd", "brew install zstd");
        return "zstd";
    }

    public static string GetMkSquashFsPath()
    {
        if (VelopackRuntimeInfo.IsWindows)
            return FindHelperFile("squashfs-tools\\gensquashfs.exe");
        Exe.AssertSystemBinaryExists("mksquashfs", "sudo apt install squashfs-tools", "brew install squashfs");
        return "mksquashfs";
    }

    [SupportedOSPlatform("macos")]
    public static string VelopackEntitlements => FindHelperFile("Velopack.entitlements");

    public static string AppImageRuntimeArm64 => FindHelperFile("appimagekit-runtime-aarch64");

    public static string AppImageRuntimeX64 => FindHelperFile("appimagekit-runtime-x86_64");

    public static string AppImageRuntimeX86 => FindHelperFile("appimagekit-runtime-i686");

    public static string SetupPath => FindHelperFile("setup.exe");

    public static string StubExecutablePath => FindHelperFile("stub.exe");

    [SupportedOSPlatform("windows")]
    public static string WixTemplatePath => FindHelperFile("wix\\template.wxs");
    [SupportedOSPlatform("windows")]
    public static string WixCandlePath => FindHelperFile("wix\\candle.exe");

    [SupportedOSPlatform("windows")]
    public static string WixLightPath => FindHelperFile("wix\\light.exe");

    [SupportedOSPlatform("windows")]
    public static string WixPath => FindHelperFile("wix\\5.0.2\\wix.exe");


    [SupportedOSPlatform("windows")]
    public static string SignToolPath => FindHelperFile("signing\\signtool.exe");

    [SupportedOSPlatform("windows")]
    public const string AzureDlibFileName = "Azure.CodeSigning.Dlib.dll";

    public static string GetDefaultAppIcon(RuntimeOs os)
    {
        switch (os) {
        case RuntimeOs.Windows:
            return null;
        case RuntimeOs.Linux:
            return FindHelperFile("DefaultApp.png");
        case RuntimeOs.OSX:
            return FindHelperFile("DefaultApp.icns");
        default:
            throw new PlatformNotSupportedException("Default Icon is not available for this platform.");
        }
    }

    private static readonly List<string> _searchPaths = [];

    static HelperFile()
    {
#if DEBUG
        AddSearchPath(AppContext.BaseDirectory, "..", "..", "..", "target", "debug");
        AddSearchPath(AppContext.BaseDirectory, "..", "..", "..", "vendor");
        AddSearchPath(AppContext.BaseDirectory, "..", "..", "..", "artwork");
#else
        AddSearchPath(AppContext.BaseDirectory, "..", "..", "..", "vendor");
#endif
    }

    public static void ClearSearchPaths() => _searchPaths.Clear();

    public static void AddSearchPath(params string[] pathParts)
    {
        AddSearchPath(Path.Combine(pathParts));
    }

    public static void AddSearchPath(string path)
    {
        if (Directory.Exists(path))
            _searchPaths.Insert(0, path);
    }

    public static string FindHelperFile(string toFind, Func<string, bool> predicate = null, bool throwWhenNotFound = true)
    {
        //var baseDirs = new[] {
        //    AppContext.BaseDirectory,
        //    Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location),
        //    Environment.CurrentDirectory,
        //};

        var files = _searchPaths
            .Where(d => !String.IsNullOrEmpty(d))
            .Distinct()
            .Select(d => Path.Combine(d, toFind))
            .Where(d => File.Exists(d))
            .Select(Path.GetFullPath);

        if (predicate != null)
            files = files.Where(predicate);

        var result = files.FirstOrDefault();
        if (result == null && throwWhenNotFound) {
            StringBuilder msg = new();
            msg.AppendLine($"HelperFile could not find '{toFind}'.");
            msg.AppendLine("Search paths:");
            foreach (var path in _searchPaths)
                msg.AppendLine($"  {Path.GetFullPath(path)}");
            throw new Exception(msg.ToString());
        }

        return result;
    }
}