using Velopack.Packaging;

namespace Velopack.TestCommon;

public static class TestsInit
{
    public static void Init()
    {
        HelperFile.AddSearchPath(PathHelper.GetRustBuildOutputDir());
        HelperFile.AddSearchPath(PathHelper.GetVendorLibDir());
        HelperFile.AddSearchPath(PathHelper.GetArtworkDir());
    }
}
