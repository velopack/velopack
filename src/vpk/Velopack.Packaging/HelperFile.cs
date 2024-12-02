using System.Runtime.Versioning;
using Microsoft.Extensions.Logging;

#if !DEBUG
using Velopack.Util;
#endif

namespace Velopack.Packaging;

public static class HelperFile
{
    public static string GetUpdatePath(RID target, ILogger log)
    {
        switch (target.BaseRID) {
#if DEBUG
        case RuntimeOs.Windows:
            return FindHelperFile("update.exe");
        case RuntimeOs.Linux:
            return FindHelperFile("update");
        case RuntimeOs.OSX:
            return FindHelperFile("update");
#else
        case RuntimeOs.Windows:
            if (!target.HasArchitecture) {
                log.Warn(
                    "No architecture specified with --runtime, Update defaulting to x86. If this was not intended please specify via the --runtime parameter");
                return FindHelperFile("Update_x86.exe");
            }

            return target.Architecture switch {
                RuntimeCpu.arm64 => FindHelperFile("Update_arm64.exe"),
                RuntimeCpu.x64 => FindHelperFile("Update_x64.exe"),
                RuntimeCpu.x86 => FindHelperFile("Update_x86.exe"),
                _ => throw new PlatformNotSupportedException($"Update binary is not available for this platform ({target}).")
            };
        case RuntimeOs.Linux:
            if (!target.HasArchitecture) {
                log.Warn("No architecture specified with --runtime, defaulting to x64. If this was not intended please specify via the --runtime parameter");
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

    public static string GetSetupPath(RID target, ILogger log)
    {
        if (target.BaseRID != RuntimeOs.Windows)
            throw new PlatformNotSupportedException("Setup binary is not available for this platform.");

#if DEBUG
        return FindHelperFile("setup.exe");
#else
        if (!target.HasArchitecture) {
            log.Warn("No architecture specified with --runtime, Setup defaulting to x86. If this was not intended please specify via the --runtime parameter");
            return FindHelperFile("Setup_x86.exe");
        }

        return target.Architecture switch {
            RuntimeCpu.arm64 => FindHelperFile("Setup_arm64.exe"),
            RuntimeCpu.x64 => FindHelperFile("Setup_x64.exe"),
            RuntimeCpu.x86 => FindHelperFile("Setup_x86.exe"),
            _ => throw new PlatformNotSupportedException($"Update binary is not available for this platform ({target}).")
        };
#endif
    }

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

    public static string StubExecutablePath => FindHelperFile("stub.exe");

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
        if (result == null && throwWhenNotFound)
            throw new Exception($"HelperFile could not find '{toFind}'.");

        return result;
    }
}