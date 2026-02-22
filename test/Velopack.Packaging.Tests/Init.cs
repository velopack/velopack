using System.Runtime.CompilerServices;

namespace Velopack.Packaging.Tests;

internal static class TestsInit
{
    [ModuleInitializer]
    internal static void Init()
    {
        HelperFile.AddSearchPath(PathHelper.GetRustBuildOutputDir());
        HelperFile.AddSearchPath(PathHelper.GetVendorLibDir());
        HelperFile.AddSearchPath(PathHelper.GetArtworkDir());
    }
}
