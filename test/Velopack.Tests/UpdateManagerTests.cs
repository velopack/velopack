using System.Text;
using System.Text.Json;
using NuGet.Versioning;
using Velopack.Locators;
using Velopack.Sources;
using Velopack.Tests.TestHelpers;

namespace Velopack.Tests;

public class UpdateManagerTests
{
    private readonly ITestOutputHelper _output;

    public UpdateManagerTests(ITestOutputHelper output)
    {
        _output = output;
    }

    private FakeDownloader GetMockDownloaderNoDelta()
    {
        var feed = new VelopackAssetFeed() {
            Assets = new VelopackAsset[] {
                new VelopackAsset() {
                    PackageId = "MyCoolApp",
                    Version = new SemanticVersion(1, 1, 0),
                    Type = VelopackAssetType.Full,
                    FileName = $"MyCoolApp-1.1.0.nupkg",
                    SHA1 = "3a2eadd15dd984e4559f2b4d790ec8badaeb6a39",
                    Size = 1040561,
                },
                new VelopackAsset() {
                    PackageId = "MyCoolApp",
                    Version = new SemanticVersion(1, 0, 0),
                    Type = VelopackAssetType.Full,
                    FileName = $"MyCoolApp-1.0.0.nupkg",
                    SHA1 = "94689fede03fed7ab59c24337673a27837f0c3ec",
                    Size = 1004502,
                },
            }
        };
        var json = JsonSerializer.Serialize(feed, SimpleJsonTests.Options);
        return new FakeDownloader() { MockedResponseBytes = Encoding.UTF8.GetBytes(json) };
    }

    private FakeDownloader GetMockDownloaderWith2Delta()
    {
        var feed = new VelopackAssetFeed {
            Assets = new VelopackAsset[] {
                  new VelopackAsset() {
                    PackageId = "MyCoolApp",
                    Version = new SemanticVersion(1, 1, 0),
                    Type = VelopackAssetType.Full,
                    FileName = $"MyCoolApp-1.1.0.nupkg",
                    SHA1 = "3a2eadd15dd984e4559f2b4d790ec8badaeb6a39",
                    Size = 1040561,
                },
                new VelopackAsset() {
                    PackageId = "MyCoolApp",
                    Version = new SemanticVersion(1, 0, 0),
                    Type = VelopackAssetType.Full,
                    FileName = $"MyCoolApp-1.0.0.nupkg",
                    SHA1 = "94689fede03fed7ab59c24337673a27837f0c3ec",
                    Size = 1004502,
                },
                new VelopackAsset() {
                    PackageId = "MyCoolApp",
                    Version = new SemanticVersion(1, 1, 0),
                    Type = VelopackAssetType.Delta,
                    FileName = $"MyCoolApp-1.1.0-delta.nupkg",
                    SHA1 = "14db31d2647c6d2284882a2e101924a9c409ee67",
                    Size = 80396,
                },
                new VelopackAsset() {
                    PackageId = "MyCoolApp",
                    Version = new SemanticVersion(1, 0, 0),
                    Type = VelopackAssetType.Delta,
                    FileName = $"MyCoolApp-1.0.0-delta.nupkg",
                    SHA1 = "14db31d2647c6d2284882a2e101924a9c409ee67",
                    Size = 80396,
                },
                  new VelopackAsset() {
                    PackageId = "MyCoolApp",
                    Version = new SemanticVersion(1, 2, 0),
                    Type = VelopackAssetType.Delta,
                    FileName = $"MyCoolApp-1.2.0-delta.nupkg",
                    SHA1 = "14db31d2647c6d2284882a2e101924a9c409ee67",
                    Size = 80396,
                },
                new VelopackAsset() {
                    PackageId = "MyCoolApp",
                    Version = new SemanticVersion(1, 2, 0),
                    Type = VelopackAssetType.Full,
                    FileName = $"MyCoolApp-1.2.0.nupkg",
                    SHA1 = "3a2eadd15dd984e4559f2b4d790ec8badaeb6a39",
                    Size = 1040561,
                },
            }
        };
        var json = JsonSerializer.Serialize(feed, SimpleJsonTests.Options);
        return new FakeDownloader() { MockedResponseBytes = Encoding.UTF8.GetBytes(json) };
    }

    [Fact]
    public void CheckFromLocal()
    {
        using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
        using var _1 = Utility.GetTempDirectory(out var tempPath);
        var dl = GetMockDownloaderNoDelta();
        var source = new SimpleWebSource("http://any.com", dl);
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger);
        var um = new UpdateManager(source, null, logger, locator);
        var info = um.CheckForUpdates();
        Assert.NotNull(info);
        Assert.True(new SemanticVersion(1, 1, 0) == info.TargetFullRelease.Version);
        Assert.Equal(0, info.DeltasToTarget.Count());
        Assert.False(info.IsDowngrade);
        Assert.StartsWith($"http://any.com/releases.{VelopackRuntimeInfo.SystemOs.GetOsShortName()}.json?", dl.LastUrl);
    }

    [Fact]
    public void CheckFromLocalWithChannel()
    {
        using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
        using var _1 = Utility.GetTempDirectory(out var tempPath);
        var dl = GetMockDownloaderNoDelta();
        var source = new SimpleWebSource("http://any.com", dl);
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger);
        var opt = new UpdateOptions { ExplicitChannel = "experimental" };
        var um = new UpdateManager(source, opt, logger, locator);
        var info = um.CheckForUpdates();
        Assert.NotNull(info);
        Assert.True(new SemanticVersion(1, 1, 0) == info.TargetFullRelease.Version);
        Assert.Equal(0, info.DeltasToTarget.Count());
        Assert.False(info.IsDowngrade);
        Assert.StartsWith("http://any.com/releases.experimental.json?", dl.LastUrl);
    }

    [Fact]
    public void CheckForSameAsInstalledVersion()
    {
        using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
        using var _1 = Utility.GetTempDirectory(out var tempPath);
        var dl = GetMockDownloaderWith2Delta();
        var myVer = new VelopackAsset() {
            PackageId = "MyCoolApp",
            Version = new SemanticVersion(1, 2, 0),
            Type = VelopackAssetType.Full,
            FileName = $"MyCoolApp-1.2.0.nupkg",
            SHA1 = "3a2eadd15dd984e4559f2b4d790ec8badaeb6a39",
            Size = 1040561,
        };
        var source = new SimpleWebSource("http://any.com", dl);
        var locator = new TestVelopackLocator("MyCoolApp", "1.2.0", tempPath, null, null, null, logger: logger, localPackage: myVer, channel: "stable");

        // checking for same version should return null
        var um = new UpdateManager(source, null, logger, locator);
        var info = um.CheckForUpdates();
        Assert.Null(info);
        Assert.StartsWith("http://any.com/releases.stable.json?", dl.LastUrl);

        // checking for same version WITHOUT explicit channel should return null
        var opt = new UpdateOptions { AllowVersionDowngrade = true };
        um = new UpdateManager(source, opt, logger, locator);
        Assert.Null(info);
        Assert.StartsWith("http://any.com/releases.stable.json?", dl.LastUrl);

        // checking for same version with explicit channel & downgrade allowed should return version
        opt = new UpdateOptions { ExplicitChannel = "experimental", AllowVersionDowngrade = true };
        um = new UpdateManager(source, opt, logger, locator);
        info = um.CheckForUpdates();
        Assert.True(info.IsDowngrade);
        Assert.NotNull(info);
        Assert.True(new SemanticVersion(1, 2, 0) == info.TargetFullRelease.Version);
        Assert.StartsWith("http://any.com/releases.experimental.json?", dl.LastUrl);
        Assert.Equal(0, info.DeltasToTarget.Count());
    }

    [Fact]
    public void CheckForLowerThanInstalledVersion()
    {
        using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
        using var _1 = Utility.GetTempDirectory(out var tempPath);
        var dl = GetMockDownloaderWith2Delta();
        var myVer = new VelopackAsset() {
            PackageId = "MyCoolApp",
            Version = new SemanticVersion(2, 0, 0),
            Type = VelopackAssetType.Full,
            FileName = $"MyCoolApp-2.0.0.nupkg",
            SHA1 = "3a2eadd15dd984e4559f2b4d790ec8badaeb6a39",
            Size = 1040561,
        };
        var source = new SimpleWebSource("http://any.com", dl);
        var locator = new TestVelopackLocator("MyCoolApp", "2.0.0", tempPath, null, null, null, logger: logger, localPackage: myVer, channel: "stable");

        // checking for lower version should return null
        var um = new UpdateManager(source, null, logger, locator);
        var info = um.CheckForUpdates();
        Assert.Null(info);
        Assert.StartsWith("http://any.com/releases.stable.json?", dl.LastUrl);

        // checking for lower version with downgrade allowed should return lower version
        var opt = new UpdateOptions { AllowVersionDowngrade = true };
        um = new UpdateManager(source, opt, logger, locator);
        info = um.CheckForUpdates();
        Assert.True(info.IsDowngrade);
        Assert.NotNull(info);
        Assert.True(new SemanticVersion(1, 2, 0) == info.TargetFullRelease.Version);
        Assert.StartsWith("http://any.com/releases.stable.json?", dl.LastUrl);
        Assert.Equal(0, info.DeltasToTarget.Count());
    }

    [Fact]
    public void CheckFromLocalWithDelta()
    {
        using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
        using var _1 = Utility.GetTempDirectory(out var tempPath);
        var dl = GetMockDownloaderWith2Delta();
        var myVer = new VelopackAsset() {
            PackageId = "MyCoolApp",
            Version = new SemanticVersion(1, 0, 0),
            Type = VelopackAssetType.Full,
            FileName = $"MyCoolApp-1.0.0.nupkg",
            SHA1 = "94689fede03fed7ab59c24337673a27837f0c3ec",
            Size = 1004502,
        };
        var source = new SimpleWebSource("http://any.com", dl);

        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, null, null, null, logger: logger, localPackage: myVer);
        var um = new UpdateManager(source, null, logger, locator);
        var info = um.CheckForUpdates();
        Assert.False(info.IsDowngrade);
        Assert.NotNull(info);
        Assert.True(new SemanticVersion(1, 2, 0) == info.TargetFullRelease.Version);
        Assert.Equal(2, info.DeltasToTarget.Count());
    }

    [Fact]
    public void NoDeltaIfNoBasePackage()
    {
        using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
        using var _1 = Utility.GetTempDirectory(out var tempPath);
        var dl = GetMockDownloaderWith2Delta();
        var source = new SimpleWebSource("http://any.com", dl);
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger: logger);
        var um = new UpdateManager(source, null, logger, locator);
        var info = um.CheckForUpdates();
        Assert.NotNull(info);
        Assert.False(info.IsDowngrade);
        Assert.True(new SemanticVersion(1, 2, 0) == info.TargetFullRelease.Version);
        Assert.Equal(0, info.DeltasToTarget.Count());
    }

    [Fact]
    public void CheckFromLocalWithDeltaNoLocalPackage()
    {
        using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
        using var _1 = Utility.GetTempDirectory(out var tempPath);
        var dl = GetMockDownloaderWith2Delta();
        var source = new SimpleWebSource("http://any.com", dl);
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger: logger);
        var um = new UpdateManager(source, null, logger, locator);
        var info = um.CheckForUpdates();
        Assert.NotNull(info);
        Assert.False(info.IsDowngrade);
        Assert.True(new SemanticVersion(1, 2, 0) == info.TargetFullRelease.Version);
        Assert.Equal(0, info.DeltasToTarget.Count());
    }

    [Fact(Skip = "Consumes API Quota")]
    public void CheckGithub()
    {
        // https://github.com/caesay/SquirrelCustomLauncherTestApp
        using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
        using var _1 = Utility.GetTempDirectory(out var tempPath);
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger);
        var source = new GithubSource("https://github.com/caesay/SquirrelCustomLauncherTestApp", null, false);
        var um = new UpdateManager(source, null, logger, locator);
        var info = um.CheckForUpdates();
        Assert.NotNull(info);
        Assert.True(new SemanticVersion(1, 0, 1) == info.TargetFullRelease.Version);
        Assert.Equal(0, info.DeltasToTarget.Count());
    }

    [Fact(Skip = "Consumes API Quota")]
    public void CheckGithubWithNonExistingChannel()
    {
        // https://github.com/caesay/SquirrelCustomLauncherTestApp
        using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
        using var _1 = Utility.GetTempDirectory(out var tempPath);
        var locator = new TestVelopackLocator("MyCoolApp", "1.0.0", tempPath, logger);
        var source = new GithubSource("https://github.com/caesay/SquirrelCustomLauncherTestApp", null, false);
        var opt = new UpdateOptions { ExplicitChannel = "hello" };
        var um = new UpdateManager(source, opt, logger, locator);
        Assert.Throws<ArgumentException>(() => um.CheckForUpdates());
    }

    [Fact]
    public void NoUpdatesIfCurrentEqualsRemoteVersion()
    {
        using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
        using var _1 = Utility.GetTempDirectory(out var tempPath);
        var dl = GetMockDownloaderNoDelta();
        var source = new SimpleWebSource("http://any.com", dl);
        var locator = new TestVelopackLocator("MyCoolApp", "1.1.0", tempPath, logger);
        var um = new UpdateManager(source, null, logger, locator);
        var info = um.CheckForUpdates();
        Assert.Null(info);
    }

    [Fact]
    public void NoUpdatesIfCurrentGreaterThanRemoteVersion()
    {
        using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
        using var _1 = Utility.GetTempDirectory(out var tempPath);
        var dl = GetMockDownloaderNoDelta();
        var source = new SimpleWebSource("http://any.com", dl);
        var locator = new TestVelopackLocator("MyCoolApp", "1.2.0", tempPath, logger);
        var um = new UpdateManager(source, null, logger, locator);
        var info = um.CheckForUpdates();
        Assert.Null(info);
    }

    [Theory]
    [InlineData("Clowd", "3.4.287")]
    [InlineData("slack", "1.1.8")]
    public void DownloadsLatestFullVersion(string id, string version)
    {
        using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
        using var _1 = Utility.GetTempDirectory(out var packagesDir);
        var repo = new FakeFixtureRepository(id, false);
        var source = new SimpleWebSource("http://any.com", repo);
        var locator = new TestVelopackLocator(id, "1.0.0", packagesDir, logger);
        var um = new UpdateManager(source, null, logger, locator);

        var info = um.CheckForUpdates();
        Assert.NotNull(info);
        Assert.True(SemanticVersion.Parse(version) == info.TargetFullRelease.Version);
        Assert.Equal(0, info.DeltasToTarget.Count());

        um.DownloadUpdates(info);

        var target = Path.Combine(packagesDir, $"{id}-{version}-full.nupkg");
        Assert.True(File.Exists(target));
        um.VerifyPackageChecksum(info.TargetFullRelease);
    }

    [SkippableTheory]
    [InlineData("Clowd", "3.4.287", "3.4.292")]
    //[InlineData("slack", "1.1.8", "1.2.2")]
    public async Task DownloadsDeltasAndCreatesFullVersion(string id, string fromVersion, string toVersion)
    {
        Skip.If(VelopackRuntimeInfo.IsLinux);
        using var logger = _output.BuildLoggerFor<UpdateManagerTests>();
        using var _1 = Utility.GetTempDirectory(out var packagesDir);
        var repo = new FakeFixtureRepository(id, true);
        var source = new SimpleWebSource("http://any.com", repo);

        var feed = await source.GetReleaseFeed(logger, VelopackRuntimeInfo.SystemOs.GetOsShortName());
        var basePkg = feed.Assets
            .Where(x => x.Type == VelopackAssetType.Full)
            .Single(x => x.Version == SemanticVersion.Parse(fromVersion));
        var basePkgFixturePath = PathHelper.GetFixture(basePkg.FileName);
        var basePkgPath = Path.Combine(packagesDir, basePkg.FileName);
        File.Copy(basePkgFixturePath, basePkgPath);

        var updateExe = PathHelper.CopyUpdateTo(packagesDir);
        var locator = new TestVelopackLocator(id, fromVersion,
            packagesDir, null, null, updateExe, null, logger);
        var um = new UpdateManager(source, null, logger, locator);

        var info = await um.CheckForUpdatesAsync();
        Assert.NotNull(info);
        Assert.True(SemanticVersion.Parse(toVersion) == info.TargetFullRelease.Version);
        Assert.Equal(3, info.DeltasToTarget.Count());
        Assert.NotNull(info.BaseRelease);

        await um.DownloadUpdatesAsync(info);
        var target = Path.Combine(packagesDir, $"{id}-{toVersion}-full.nupkg");
        Assert.True(File.Exists(target));
    }
}
