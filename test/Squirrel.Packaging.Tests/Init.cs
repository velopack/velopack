using System.IO;
using System.Reflection;
using Squirrel.Packaging;
using Xunit.Abstractions;
using Xunit.Sdk;

[assembly: Xunit.TestFramework("Squirrel.Packaging.Tests.TestsInit", "Squirrel.Packaging.Tests")]

namespace Squirrel.Packaging.Tests
{
    public class TestsInit : XunitTestFramework
    {
        public TestsInit(IMessageSink messageSink)
          : base(messageSink)
        {
            var baseDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location.Replace("file:///", ""));
            var projectdir = Path.Combine(baseDir, "..", "..", "..", "..", "..");
            HelperFile.AddSearchPath(Path.Combine(projectdir, "src\\Rust\\target\\debug"));
            HelperFile.AddSearchPath(Path.Combine(projectdir, "vendor"));
        }
    }
}
