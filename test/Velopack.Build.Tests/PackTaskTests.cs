using Velopack.Packaging.Compression;

namespace Velopack.Build.Tests;

public class PackTaskTests
{
    [Theory]
    [MemberData(nameof(GetRequiredArgumentsData))]
    public void BuildArguments_IncludesRequiredArguments(
        string packId, string packVersion, string packDirectory, string releaseDir, string[] expectedArgs)
    {
        var task = CreatePackTask(packId, packVersion, packDirectory, releaseDir);
        var args = task.BuildPackArguments();

        foreach (var expectedArg in expectedArgs)
        {
            Assert.Contains(expectedArg, args);
        }
    }

    public static TheoryData<string, string, string, string, string[]> GetRequiredArgumentsData()
    {
        var data = new TheoryData<string, string, string, string, string[]> {
            {
                "MyApp",
                "1.0.0",
                "C:\\MyApp",
                "C:\\Release",
                new[] { "pack", "--legacyConsole", "--yes", "--packId", "MyApp", "--packVersion", "1.0.0", "--packDir", "C:\\MyApp", "--outputDir", "C:\\Release" }
            },
            {
                "TestApp.Example",
                "2.5.1",
                "/home/user/app",
                "/home/user/releases",
                new[] { "pack", "--legacyConsole", "--yes", "--packId", "TestApp.Example", "--packVersion", "2.5.1", "--packDir", "/home/user/app", "--outputDir", "/home/user/releases" }
            }
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(GetBooleanFlagsData))]
    public void BuildArguments_IncludesBooleanFlags_WhenTrue(string propertyName, string expectedFlag)
    {
        var task = CreateMinimalPackTask();
        var property = typeof(PackTask).GetProperty(propertyName);
        Assert.NotNull(property);
        property.SetValue(task, true);

        var args = task.BuildPackArguments();

        Assert.Contains(expectedFlag, args);
    }

    [Theory]
    [MemberData(nameof(GetBooleanFlagsData))]
    public void BuildArguments_ExcludesBooleanFlags_WhenFalse(string propertyName, string expectedFlag)
    {
        var task = CreateMinimalPackTask();
        var property = typeof(PackTask).GetProperty(propertyName);
        Assert.NotNull(property);
        property.SetValue(task, false);

        var args = task.BuildPackArguments();

        Assert.DoesNotContain(expectedFlag, args);
    }

    public static TheoryData<string, string> GetBooleanFlagsData()
    {
        var data = new TheoryData<string, string> {
            { nameof(PackTask.SkipVelopackAppCheck), "--skipVeloAppCheck" },
            { nameof(PackTask.NoPortable), "--noPortable" },
            { nameof(PackTask.NoInst), "--noInst" },
            { nameof(PackTask.BuildMsi), "--msi" },
            { nameof(PackTask.SignDisableDeep), "--signDisableDeep" }
        };
        return data;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildArguments_ExcludesOptionalArgument_WhenNullOrWhitespace(string? value)
    {
        var task = CreateMinimalPackTask();
        task.PackAuthors = value;

        var args = task.BuildPackArguments();

        Assert.DoesNotContain("--packAuthors", args);
    }

    [Fact]
    public void BuildArguments_IncludesAllArguments_InComplexScenario()
    {
        var task = new PackTask
        {
            PackId = "ComplexApp",
            PackVersion = "3.2.1",
            PackDirectory = "C:\\ComplexApp",
            ReleaseDir = "C:\\Releases",
            TargetFramework = "net8.0",
            EntryExecutableName = "ComplexApp.exe",
            PackAuthors = "Complex Authors Inc.",
            PackTitle = "Complex Application Title",
            Icon = "app.ico",
            ReleaseNotes = "release-notes.md",
            DeltaMode = nameof(DeltaMode.BestSize),
            Channel = "stable",
            Exclude = @".*\.(pdb|xml)",
            Runtimes = "net8-x64-desktop",
            NoPortable = true,
            BuildMsi = true,
            SignParameters = "/a /fd SHA256",
            SignParallel = 5,
            Shortcuts = "Desktop,StartMenu",
            Categories = "Development",
            Compression = "BestSpeed"
        };

        var args = task.BuildPackArguments();

        var expectedArgs = new[]
        {
            "pack", "--legacyConsole", "--yes",
            "--packId", "ComplexApp",
            "--packVersion", "3.2.1",
            "--packDir", "C:\\ComplexApp",
            "--outputDir", "C:\\Releases",
            "--mainExe", "ComplexApp.exe",
            "--packAuthors", "Complex Authors Inc.",
            "--packTitle", "Complex Application Title",
            "--icon", "app.ico",
            "--releaseNotes", "release-notes.md",
            "--delta", "BestSize",
            "--channel", "stable",
            "--exclude", @".*\.(pdb|xml)",
            "--framework", "net8-x64-desktop",
            "--signParams", "/a /fd SHA256",
            "--signParallel", "5",
            "--shortcuts", "Desktop,StartMenu",
            "--categories", "Development",
            "--compression", "BestSpeed",
            "--noPortable",
            "--msi"
        };

        foreach (var expectedArg in expectedArgs)
        {
            Assert.Contains(expectedArg, args);
        }
    }

    [Fact]
    public void BuildArguments_StartsWithPackCommand()
    {
        var task = CreateMinimalPackTask();
        var args = task.BuildPackArguments();

        Assert.Equal("pack", args[0]);
    }

    [Fact]
    public void BuildArguments_IncludesLegacyConsoleAndYes()
    {
        var task = CreateMinimalPackTask();
        var args = task.BuildPackArguments();

        Assert.Contains("--legacyConsole", args);
        Assert.Contains("--yes", args);
    }

    [Theory]
    [MemberData(nameof(GetMacOSSpecificArgumentsData))]
    public void BuildArguments_IncludesMacOSSpecificArguments(PackTask task, string[] expectedArgs)
    {
        var args = task.BuildPackArguments();

        foreach (var expectedArg in expectedArgs)
        {
            Assert.Contains(expectedArg, args);
        }
    }

    public static TheoryData<PackTask, string[]> GetMacOSSpecificArgumentsData()
    {
        var data = new TheoryData<PackTask, string[]> {
            {
                new PackTask {
                    PackId = "MacApp",
                    PackVersion = "1.0.0",
                    PackDirectory = "/Users/app",
                    ReleaseDir = "/Users/releases",
                    TargetFramework = "net8.0",
                    SignAppIdentity = "Developer ID Application: Company (TEAM123)",
                    SignInstallIdentity = "Developer ID Installer: Company (TEAM123)",
                    NotaryProfile = "AC_PASSWORD",
                    BundleId = "com.example.macapp"
                },
                new[] { "--signAppIdentity", "Developer ID Application: Company (TEAM123)", "--signInstallIdentity", "Developer ID Installer: Company (TEAM123)", "--notaryProfile", "AC_PASSWORD", "--bundleId", "com.example.macapp" }
            },
            {
                new PackTask {
                    PackId = "MacApp2",
                    PackVersion = "2.0.0",
                    PackDirectory = "/Users/app2",
                    ReleaseDir = "/Users/releases2",
                    TargetFramework = "net8.0",
                    SignEntitlements = "entitlements.plist",
                    Keychain = "login.keychain",
                    InfoPlistPath = "Info.plist",
                    SignDisableDeep = true
                },
                new[] { "--signEntitlements", "entitlements.plist", "--keychain", "login.keychain", "--infoPlist", "Info.plist", "--signDisableDeep" }
            }
        };
        return data;
    }

    [Theory]
    [MemberData(nameof(GetWindowsSpecificArgumentsData))]
    public void BuildArguments_IncludesWindowsSpecificArguments(PackTask task, string[] expectedArgs)
    {
        var args = task.BuildPackArguments();

        foreach (var expectedArg in expectedArgs)
        {
            Assert.Contains(expectedArg, args);
        }
    }

    public static TheoryData<PackTask, string[]> GetWindowsSpecificArgumentsData()
    {
        var data = new TheoryData<PackTask, string[]> {
            {
                new PackTask {
                    PackId = "WinApp",
                    PackVersion = "1.0.0",
                    PackDirectory = "C:\\WinApp",
                    ReleaseDir = "C:\\Release",
                    TargetFramework = "net8.0",
                    SplashImage = "splash.bmp",
                    InstWelcome = "welcome.rtf",
                    InstReadme = "readme.rtf",
                    InstLicense = "license.rtf",
                    InstConclusion = "conclusion.rtf"
                },
                new[] { "--splashImage", "splash.bmp", "--instWelcome", "welcome.rtf", "--instReadme", "readme.rtf", "--instLicense", "license.rtf", "--instConclusion", "conclusion.rtf" }
            },
            {
                new PackTask {
                    PackId = "WinApp2",
                    PackVersion = "2.0.0",
                    PackDirectory = "C:\\WinApp2",
                    ReleaseDir = "C:\\Release2",
                    TargetFramework = "net8.0",
                    BuildMsi = true,
                    MsiVersionOverride = "2.0.0.0",
                    SignParameters = "/a /fd SHA256 /tr http://timestamp.digicert.com",
                    AzureTrustedSignFile = "azure-config.json"
                },
                new[] { "--msi", "--msiVersionOverride", "2.0.0.0", "--signParams", "/a /fd SHA256 /tr http://timestamp.digicert.com", "--azureTrustedSignFile", "azure-config.json" }
            }
        };
        return data;
    }


    private static PackTask CreateMinimalPackTask()
    {
        return new PackTask
        {
            PackId = "TestApp",
            PackVersion = "1.0.0",
            PackDirectory = "C:\\TestApp",
            ReleaseDir = "C:\\Release",
            TargetFramework = "net8.0"
        };
    }

    private static PackTask CreatePackTask(
        string packId, string packVersion, string packDirectory, string releaseDir)
    {
        return new PackTask
        {
            PackId = packId,
            PackVersion = packVersion,
            PackDirectory = packDirectory,
            ReleaseDir = releaseDir,
            TargetFramework = "net8.0"
        };
    }

    public class PackTask : Build.PackTask
    {
        public string[] BuildPackArguments()
        {
            return BuildArguments();
        }
    }
}
