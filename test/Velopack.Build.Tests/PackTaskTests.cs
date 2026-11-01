namespace Velopack.Build.Tests;

public class PackTaskTests
{
    [Fact]
    public void BuildJsonOptions_IncludesRequiredAndCommonPackOptions()
    {
        var task = new TestPackTask {
            PackId = "MyApp",
            PackVersion = "1.2.3",
            PackDirectory = "C:\\MyApp",
            ReleaseDir = "C:\\Releases",
            PackAuthors = "Velopack",
            PackTitle = "My Application",
            EntryExecutableName = "MyApp.exe",
            Icon = "app.ico",
            ReleaseNotes = "release-notes.md",
            Channel = "beta",
            Exclude = @".*\.pdb",
            TargetRuntime = "win-x64",
            NoDefaultExclude = true,
            NoPortable = true,
            NoInst = true,
        };

        var options = task.GetJsonOptions();

        Assert.Equal("MyApp", options["PackId"]);
        Assert.Equal("1.2.3", options["PackVersion"]);
        Assert.Equal("C:\\MyApp", options["PackDirectory"]);
        Assert.Equal("C:\\Releases", options["ReleaseDir"]);
        Assert.Equal("Velopack", options["PackAuthors"]);
        Assert.Equal("My Application", options["PackTitle"]);
        Assert.Equal("MyApp.exe", options["EntryExecutableName"]);
        Assert.Equal("app.ico", options["Icon"]);
        Assert.Equal("release-notes.md", options["ReleaseNotes"]);
        Assert.Equal("beta", options["Channel"]);
        Assert.Equal(@".*\.pdb", options["Exclude"]);
        Assert.Equal("win-x64", options["TargetRuntime"]);
        Assert.Equal(true, options["NoDefaultExclude"]);
        Assert.Equal(true, options["NoPortable"]);
        Assert.Equal(true, options["NoInst"]);
    }

    [Fact]
    public void BuildJsonOptions_OmitsNullOrWhitespaceAndFalseOptionalValues()
    {
        var task = new TestPackTask {
            PackId = "MyApp",
            PackVersion = "1.0.0",
            PackDirectory = "C:\\MyApp",
            ReleaseDir = "C:\\Releases",
            PackAuthors = "  ",
        };

        var options = task.GetJsonOptions();

        Assert.DoesNotContain("PackAuthors", options.Keys);
        Assert.DoesNotContain("NoDefaultExclude", options.Keys);
        Assert.DoesNotContain("NoPortable", options.Keys);
        Assert.DoesNotContain("NoInst", options.Keys);
    }

    [Fact]
    public void BuildJsonOptions_IncludesPropertiesSupportedByCurrentPlatform()
    {
        var task = new TestPackTask {
            PackId = "MyApp",
            PackVersion = "1.0.0",
            PackDirectory = "C:\\MyApp",
            ReleaseDir = "C:\\Releases",
            Runtimes = "net8-x64-desktop",
            SplashImage = "splash.bmp",
            SplashProgressColor = "#FF0000",
            SkipVelopackAppCheck = true,
            SignParameters = "/a /fd SHA256",
            SignExclude = ".*\\.map",
            SignParallel = 5,
            SignTemplate = "sign {{file}}",
            AzureTrustedSignFile = "azure.json",
            Shortcuts = "Desktop,StartMenu",
            InstWelcome = "welcome.txt",
            InstReadme = "readme.md",
            InstLicense = "license.rtf",
            InstConclusion = "conclusion.txt",
            MsiTopBanner = "banner.bmp",
            MsiDialogBackground = "logo.bmp",
            BuildMsi = true,
            MsiVersionOverride = "1.0.0.0",
            Categories = "Development",
            Compression = "zstd",
            BundleId = "com.example.app",
            InfoPlistPath = "Info.plist",
            SignAppIdentity = "Developer ID Application",
            SignInstallIdentity = "Developer ID Installer",
            SignEntitlements = "entitlements.plist",
            SignDisableDeep = true,
            NotaryProfile = "notary-profile",
            Keychain = "login.keychain",
        };

        var options = task.GetJsonOptions();

        if (OperatingSystem.IsWindows()) {
            Assert.Equal("net8-x64-desktop", options["Runtimes"]);
            Assert.Equal("splash.bmp", options["SplashImage"]);
            Assert.Equal("#FF0000", options["SplashProgressColor"]);
            Assert.Equal(true, options["SkipVelopackAppCheck"]);
            Assert.Equal("/a /fd SHA256", options["SignParameters"]);
            Assert.Equal(".*\\.map", options["SignExclude"]);
            Assert.Equal(5, options["SignParallel"]);
            Assert.Equal("sign {{file}}", options["SignTemplate"]);
            Assert.Equal("azure.json", options["AzureTrustedSignFile"]);
            Assert.Equal("Desktop,StartMenu", options["Shortcuts"]);
            Assert.Equal("banner.bmp", options["MsiTopBanner"]);
            Assert.Equal("logo.bmp", options["MsiDialogBackground"]);
            Assert.Equal("1.0.0.0", options["MsiVersionOverride"]);
            Assert.Equal(true, options["BuildMsi"]);
            Assert.DoesNotContain("BundleId", options.Keys);
            Assert.DoesNotContain("Categories", options.Keys);
            Assert.DoesNotContain("SignDisableDeep", options.Keys);
        } else if (OperatingSystem.IsMacOS()) {
            Assert.Equal("com.example.app", options["BundleId"]);
            Assert.Equal("Info.plist", options["InfoPlistPath"]);
            Assert.Equal("Developer ID Application", options["SignAppIdentity"]);
            Assert.Equal("Developer ID Installer", options["SignInstallIdentity"]);
            Assert.Equal("entitlements.plist", options["SignEntitlements"]);
            Assert.Equal(true, options["SignDisableDeep"]);
            Assert.Equal("notary-profile", options["NotaryProfile"]);
            Assert.Equal("login.keychain", options["Keychain"]);
            Assert.DoesNotContain("Runtimes", options.Keys);
            Assert.DoesNotContain("Categories", options.Keys);
        } else if (OperatingSystem.IsLinux()) {
            Assert.Equal("Development", options["Categories"]);
            Assert.Equal("zstd", options["Compression"]);
            Assert.DoesNotContain("BundleId", options.Keys);
            Assert.DoesNotContain("Runtimes", options.Keys);
            Assert.DoesNotContain("SignAppIdentity", options.Keys);
        }
    }

    private sealed class TestPackTask : Build.PackTask
    {
        public Dictionary<string, object?> GetJsonOptions() => BuildJsonOptions();
    }
}
