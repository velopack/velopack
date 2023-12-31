using System.IO;
using System.Reflection;
using Squirrel.Packaging;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: TestFramework("Squirrel.Packaging.Tests.TestsInit", "Squirrel.Packaging.Tests")]

namespace Squirrel.Packaging.Tests
{
    public class TestsInit : XunitTestFramework
    {
        public TestsInit(IMessageSink messageSink)
          : base(messageSink)
        {
            HelperFile.AddSearchPath(PathHelper.GetRustBuildOutputDir());
            HelperFile.AddSearchPath(PathHelper.GetVendorLibDir());
        }
    }
}
