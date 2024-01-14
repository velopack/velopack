using System.Reflection;
using System.Runtime.Versioning;

namespace Velopack.Packaging;

public static class HelperFile
{
    public static string GetUpdateExeName(RuntimeOs? os = null)
    {
        var _os = os ?? VelopackRuntimeInfo.SystemOs;
        switch (_os) {
        case RuntimeOs.Windows:
            return FindHelperFile("Update.exe");
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

    public static string GetUpdatePath(RuntimeOs? os = null) => FindHelperFile(GetUpdateExeName(os));

    public static string GetZstdPath()
    {
        if (VelopackRuntimeInfo.IsWindows)
            return FindHelperFile("zstd.exe");
        Exe.AssertSystemBinaryExists("zstd");
        return "zstd";
    }

    [SupportedOSPlatform("macos")]
    public static string VelopackEntitlements => FindHelperFile("Velopack.entitlements");

    [SupportedOSPlatform("linux")]
    public static string AppImageToolX64 => FindHelperFile("appimagetool-x86_64.AppImage");

    [SupportedOSPlatform("windows")]
    public static string SetupPath => FindHelperFile("Setup.exe");

    [SupportedOSPlatform("windows")]
    public static string StubExecutablePath => FindHelperFile("stub.exe");

    [SupportedOSPlatform("windows")]
    public static string SignToolPath => FindHelperFile("signtool.exe");

    [SupportedOSPlatform("windows")]
    public static string RceditPath => FindHelperFile("rcedit.exe");

    private static List<string> _searchPaths = new List<string>();

    static HelperFile()
    {
#if DEBUG
        AddSearchPath(AppContext.BaseDirectory, "..", "..", "..", "src", "Rust", "target", "debug");
        AddSearchPath(AppContext.BaseDirectory, "..", "..", "..", "vendor");
#else
        AddSearchPath(AppContext.BaseDirectory, "..", "..", "..", "vendor");
#endif
    }

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
            throw new Exception($"Could not find '{toFind}'.");

        return result;
    }
}
