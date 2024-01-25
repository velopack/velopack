using System.Diagnostics;
using Velopack;

public static class PathHelper
{
    public static string GetFixturesDir()
        => Path.Combine(GetTestRoot(), "fixtures");

    public static string GetProjectDir()
        => Path.Combine(GetTestRoot(), "..");

    public static string GetAvaloniaSample()
        => Path.Combine(GetProjectDir(), "examples", "AvaloniaCrossPlat");

    public static string GetWpfSample()
        => Path.Combine(GetProjectDir(), "examples", "VeloWpfSample");

    public static string GetVendorLibDir()
        => Path.Combine(GetProjectDir(), "vendor");

    public static string GetFixture(params string[] names)
        => Path.Combine(new string[] { GetTestRoot(), "fixtures" }.Concat(names).ToArray());

    public static string GetTestRootPath(params string[] names)
        => Path.Combine(new string[] { GetTestRoot() }.Concat(names).ToArray());

    public static string GetRustSrcDir()
        => Path.Combine(GetProjectDir(), "src", "Rust");

#if DEBUG
    public static string GetRustBuildOutputDir()
        => Path.Combine(GetRustSrcDir(), "target", "debug");
#else
    public static string GetRustBuildOutputDir()
        => Path.Combine(GetRustSrcDir(), "target", "release");
#endif

    public static string GetRustAsset(params string[] names)
        => Path.Combine(new string[] { GetRustBuildOutputDir() }.Concat(names).ToArray());

    public static string CopyRustAssetTo(string assetName, string dir)
    {
        var path = GetRustAsset(assetName);
        var newPath = Path.Combine(dir, assetName);
        File.Copy(path, newPath);
        return newPath;
    }

    public static string CopyFixtureTo(string fixtureName, string dir)
    {
        var path = GetFixture(fixtureName);
        var newPath = Path.Combine(dir, fixtureName);
        File.Copy(path, newPath);
        return newPath;
    }

    public static string CopyUpdateTo(string dir)
    {
        var updateName = VelopackRuntimeInfo.IsWindows ? "update.exe" : "update";
        var path = GetRustAsset(updateName);
        var newPath = Path.Combine(dir, updateName);
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