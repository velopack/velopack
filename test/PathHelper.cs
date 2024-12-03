using System.Diagnostics;
using Velopack;

public static class PathHelper
{
    public static bool IsCI => Environment.GetEnvironmentVariable("CI") != null;
    
    public static string GetFixturesDir()
        => Path.Combine(GetTestRoot(), "fixtures");

    public static string GetProjectDir()
        => Path.Combine(GetTestRoot(), "..");

    public static string GetAvaloniaSample()
        => Path.Combine(GetProjectDir(), "samples", "CSharpAvalonia");

    public static string GetWpfSample()
        => Path.Combine(GetProjectDir(), "samples", "CSharpWpf");

    public static string GetVendorLibDir()
        => Path.Combine(GetProjectDir(), "vendor");

    public static string GetArtworkDir()
        => Path.Combine(GetProjectDir(), "artwork");

    public static string GetFixture(params string[] names)
        => Path.Combine([GetTestRoot(), "fixtures", .. names]);

    public static string GetTestRootPath(params string[] names)
        => Path.Combine([GetTestRoot(), .. names]);

#if DEBUG
    public static string GetRustBuildOutputDir()
        => Path.Combine(GetProjectDir(), "target", "debug");
#else
    public static string GetRustBuildOutputDir()
        => Path.Combine(GetProjectDir(), "target", "release");
#endif

    public static string GetRustAsset(params string[] names)
        => Path.Combine([GetRustBuildOutputDir(), .. names]);

    public static string CopyRustAssetTo(string assetName, string dir)
    {
        var path = GetRustAsset(assetName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");
        var newPath = Path.Combine(dir, assetName);
        File.Copy(path, newPath);
        return newPath;
    }

    public static string CopyFixtureTo(string fixtureName, string dir)
    {
        var path = GetFixture(fixtureName);
        if (!File.Exists(path))
            throw new FileNotFoundException($"File not found: {path}");
        var newPath = Path.Combine(dir, fixtureName);
        File.Copy(path, newPath);
        return newPath;
    }

    public static string CopyUpdateTo(string dir)
    {
        static string GetUpdatePath()
        {
            if (VelopackRuntimeInfo.IsWindows && File.Exists(GetRustAsset("update.exe"))) {
                return GetRustAsset("update.exe");
            }

            if (VelopackRuntimeInfo.IsLinux && File.Exists(GetRustAsset("UpdateNix"))) {
                return GetRustAsset("UpdateNix");
            }

            if (VelopackRuntimeInfo.IsOSX && File.Exists(GetRustAsset("UpdateMac"))) {
                return GetRustAsset("UpdateMac");
            }

            if (!VelopackRuntimeInfo.IsWindows && File.Exists(GetRustAsset("update"))) {
                return GetRustAsset("update");
            }

            throw new FileNotFoundException("update.exe not found");
        }

        var path = GetUpdatePath();
        var newPath = Path.Combine(dir, Path.GetFileName(path));
        File.Copy(path, newPath);
        return newPath;
    }

    public static string GetTestRoot()
    {
        // XXX: This is an evil hack, but it's okay for a unit test
        // We can't use Assembly.Location because unit test runners love
        // to move stuff to temp directories
        var st = new StackFrame(true);
#pragma warning disable CS8604 // Possible null reference argument.
        var di = new DirectoryInfo(Path.Combine(Path.GetDirectoryName(st.GetFileName())));
#pragma warning restore CS8604 // Possible null reference argument.
        return di.FullName;
    }
}