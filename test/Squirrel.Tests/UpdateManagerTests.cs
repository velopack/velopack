using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using NuGet.Versioning;
using Squirrel.Locators;
using Squirrel.Sources;

namespace Squirrel.Tests
{
    public class UpdateManagerTests
    {
        private readonly ITestOutputHelper _output;

        public UpdateManagerTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void CheckForUpdatesFromLocal()
        {
            using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
            using var _1 = Utility.GetTempDirectory(out var tempPath);
            File.WriteAllText(Path.Combine(tempPath, "RELEASES"), """
3a2eadd15dd984e4559f2b4d790ec8badaeb6a39  MyCoolApp-1.1.0.nupkg  1040561
94689fede03fed7ab59c24337673a27837f0c3ec  MyCoolApp-1.0.0.nupkg  1004502
""");
            var locator = new TestSquirrelLocator("MyCoolApp", "1.0.0", tempPath, logger);
            var um = new UpdateManager(tempPath, null, logger, locator);
            var info = um.CheckForUpdates();
            Assert.NotNull(info);
            Assert.True(new SemanticVersion(1, 1, 0) == info.TargetFullRelease.Version);
            Assert.Equal(0, info.DeltasToTarget.Count());
        }

        [Fact]
        public void CheckForUpdatesFromLocalWithChannel()
        {
            using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
            using var _1 = Utility.GetTempDirectory(out var tempPath);
            File.WriteAllText(Path.Combine(tempPath, "RELEASES-osx-x64"), """
3a2eadd15dd984e4559f2b4d790ec8badaeb6a39  MyCoolApp-1.1.0.nupkg  1040561
94689fede03fed7ab59c24337673a27837f0c3ec  MyCoolApp-1.0.0.nupkg  1004502
""");
            var locator = new TestSquirrelLocator("MyCoolApp", "1.0.0", tempPath, logger);
            var um = new UpdateManager(tempPath, "osx-x64", logger, locator);
            var info = um.CheckForUpdates();
            Assert.NotNull(info);
            Assert.True(new SemanticVersion(1, 1, 0) == info.TargetFullRelease.Version);
            Assert.Equal(0, info.DeltasToTarget.Count());
        }

        [Fact]
        public void CheckForUpdatesFromRemote()
        {
            using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
            using var _1 = Utility.GetTempDirectory(out var tempPath);
            var releases = """
3a2eadd15dd984e4559f2b4d790ec8badaeb6a39  MyCoolApp-1.1.0.nupkg  1040561
94689fede03fed7ab59c24337673a27837f0c3ec  MyCoolApp-1.0.0.nupkg  1004502
""";
            var downloader = new FakeDownloader() { MockedResponseBytes = Encoding.UTF8.GetBytes(releases) };
            var locator = new TestSquirrelLocator("MyCoolApp", "1.0.0", tempPath, logger);
            var um = new UpdateManager(new SimpleWebSource("http://any.com", "hello", downloader, logger), logger, locator);
            var info = um.CheckForUpdates();
            Assert.NotNull(info);
            Assert.True(new SemanticVersion(1, 1, 0) == info.TargetFullRelease.Version);
            Assert.Equal(0, info.DeltasToTarget.Count());
            Assert.Contains("/RELEASES-hello?", downloader.LastUrl);
        }

        [Fact]
        public void CheckForUpdatesFromLocalWithDelta()
        {
            using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
            using var _1 = Utility.GetTempDirectory(out var tempPath);
            File.WriteAllText(Path.Combine(tempPath, "RELEASES"), """
3a2eadd15dd984e4559f2b4d790ec8badaeb6a39  MyCoolApp-1.1.0.nupkg  1040561
3a2eadd15dd984e4559f2b4d790ec8badaeb6a39  MyCoolApp-1.2.0.nupkg  1040561
14db31d2647c6d2284882a2e101924a9c409ee67  MyCoolApp-1.2.0-delta.nupkg  80396
14db31d2647c6d2284882a2e101924a9c409ee67  MyCoolApp-1.1.0-delta.nupkg  80396
94689fede03fed7ab59c24337673a27837f0c3ec  MyCoolApp-1.0.0.nupkg  1004502
""");
            var locator = new TestSquirrelLocator("MyCoolApp", "1.0.0", tempPath, logger);
            var um = new UpdateManager(tempPath, null, logger, locator);
            var info = um.CheckForUpdates();
            Assert.NotNull(info);
            Assert.True(new SemanticVersion(1, 2, 0) == info.TargetFullRelease.Version);
            Assert.Equal(2, info.DeltasToTarget.Count());
        }

        [Fact(Skip = "Consumes API Quota")]
        public void CheckForUpdatesGithub()
        {
            // https://github.com/caesay/SquirrelCustomLauncherTestApp
            using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
            using var _1 = Utility.GetTempDirectory(out var tempPath);
            var releases = """
3a2eadd15dd984e4559f2b4d790ec8badaeb6a39  MyCoolApp-1.1.0.nupkg  1040561
94689fede03fed7ab59c24337673a27837f0c3ec  MyCoolApp-1.0.0.nupkg  1004502
""";
            var locator = new TestSquirrelLocator("MyCoolApp", "1.0.0", tempPath, logger);
            var source = new GithubSource("https://github.com/caesay/SquirrelCustomLauncherTestApp", null, false);
            var um = new UpdateManager(source, logger, locator);
            var info = um.CheckForUpdates();
            Assert.NotNull(info);
            Assert.True(new SemanticVersion(1, 0, 1) == info.TargetFullRelease.Version);
            Assert.Equal(0, info.DeltasToTarget.Count());
        }

        [Fact(Skip = "Consumes API Quota")]
        public void CheckForUpdatesGithubWithNonExistingChannel()
        {
            // https://github.com/caesay/SquirrelCustomLauncherTestApp
            using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
            using var _1 = Utility.GetTempDirectory(out var tempPath);
            var locator = new TestSquirrelLocator("MyCoolApp", "1.0.0", tempPath, logger);
            var source = new GithubSource("https://github.com/caesay/SquirrelCustomLauncherTestApp", null, false, "hello");
            var um = new UpdateManager(source, logger, locator);
            Assert.Throws<ArgumentException>(() => um.CheckForUpdates());
        }

        [Fact]
        public void NoUpdatesIfCurrentEqualsRemoteVersion()
        {
            using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
            using var _1 = Utility.GetTempDirectory(out var tempPath);
            File.WriteAllText(Path.Combine(tempPath, "RELEASES"), """
3a2eadd15dd984e4559f2b4d790ec8badaeb6a39  MyCoolApp-1.1.0.nupkg  1040561
94689fede03fed7ab59c24337673a27837f0c3ec  MyCoolApp-1.0.0.nupkg  1004502
""");
            var locator = new TestSquirrelLocator("MyCoolApp", "1.1.0", tempPath, logger);
            var um = new UpdateManager(tempPath, null, logger, locator);
            var info = um.CheckForUpdates();
            Assert.Null(info);
        }

        [Fact]
        public void NoUpdatesIfCurrentGreaterThanRemoteVersion()
        {
            using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
            using var _1 = Utility.GetTempDirectory(out var tempPath);
            File.WriteAllText(Path.Combine(tempPath, "RELEASES"), """
3a2eadd15dd984e4559f2b4d790ec8badaeb6a39  MyCoolApp-1.1.0.nupkg  1040561
94689fede03fed7ab59c24337673a27837f0c3ec  MyCoolApp-1.0.0.nupkg  1004502
""");
            var locator = new TestSquirrelLocator("MyCoolApp", "1.2.0", tempPath, logger);
            var um = new UpdateManager(tempPath, null, logger, locator);
            var info = um.CheckForUpdates();
            Assert.Null(info);
        }
    }
}
