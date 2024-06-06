using System.Runtime.Versioning;

namespace Velopack.Packaging;

public static class HelperFile
{
    public static string GetUpdateExeName(RuntimeOs os)
    {
        switch (os) {
        case RuntimeOs.Windows:
            return FindHelperFile("update.exe");
#if DEBUG
        case RuntimeOs.Linux:
            return FindHelperFile("update");
        case RuntimeOs.OSX:
            return FindHelperFile("update");
#else
        case RuntimeOs.Linux:
            return FindHelperFile("UpdateNix");
        case RuntimeOs.OSX:
            return FindHelperFile("UpdateMac");
#endif
        default:
            throw new PlatformNotSupportedException("Update binary is not available for this platform.");
        }
    }

    public static string GetUpdatePath(RuntimeOs os) => FindHelperFile(GetUpdateExeName(os));

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
    public static string SignToolPath => FindHelperFile("signtool.exe");

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

    private static List<string> _searchPaths = new List<string>();

    static HelperFile()
    {
#if DEBUG
        AddSearchPath(AppContext.BaseDirectory, "..", "..", "..", "src", "Rust", "target", "debug");
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
