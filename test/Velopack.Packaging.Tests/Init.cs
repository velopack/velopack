using Xunit.Sdk;

[assembly: TestFramework("Velopack.Packaging.Tests.TestsInit", "Velopack.Packaging.Tests")]

namespace Velopack.Packaging.Tests;

public class TestsInit : XunitTestFramework
{
    public TestsInit(IMessageSink messageSink)
      : base(messageSink)
    {
        HelperFile.AddSearchPath(PathHelper.GetRustBuildOutputDir());
        HelperFile.AddSearchPath(PathHelper.GetVendorLibDir());
    }
}
