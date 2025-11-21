using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;

namespace Velopack.Build.Tests;

/// <summary>
/// Tests for PackTask MSBuild task
/// </summary>
public class PackTaskTests
{
    [Fact]
    public void PackTask_ShouldHaveRequiredProperties()
    {
        var task = new PackTask();
        
        // Verify required properties exist
        Assert.NotNull(task.GetType().GetProperty(nameof(PackTask.TargetFramework)));
        Assert.NotNull(task.GetType().GetProperty(nameof(PackTask.PackVersion)));
        Assert.NotNull(task.GetType().GetProperty(nameof(PackTask.PackId)));
        Assert.NotNull(task.GetType().GetProperty(nameof(PackTask.PackDirectory)));
        Assert.NotNull(task.GetType().GetProperty(nameof(PackTask.ReleaseDir)));
        
        // Verify required attributes
        var targetFrameworkProp = task.GetType().GetProperty(nameof(PackTask.TargetFramework))!;
        Assert.NotNull(targetFrameworkProp.GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
    }

    [Fact]
    public void PackTask_DefaultValues_ShouldBeSet()
    {
        var task = new PackTask();
        
        Assert.Equal("BestSpeed", task.DeltaMode);
        Assert.Equal(10, task.SignParallel);
        Assert.Equal("Auto", task.VelopackToolMode);
        Assert.False(task.VelopackToolPrerelease);
        Assert.False(task.VelopackSkipToolInstall);
    }

    [Fact]
    public void PackTask_ToolModeParser_Auto_ShouldParse()
    {
        // Use reflection to test the private ParseToolMode method
        var method = typeof(PackTask).GetMethod("ParseToolMode", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        Assert.NotNull(method);
        
        var result = method!.Invoke(null, new object[] { "auto" });
        Assert.Equal(VpkToolConfiguration.ToolMode.Auto, result);
        
        result = method.Invoke(null, new object[] { "Auto" });
        Assert.Equal(VpkToolConfiguration.ToolMode.Auto, result);
        
        result = method.Invoke(null, new object[] { "AUTO" });
        Assert.Equal(VpkToolConfiguration.ToolMode.Auto, result);
    }

    [Fact]
    public void PackTask_ToolModeParser_Local_ShouldParse()
    {
        var method = typeof(PackTask).GetMethod("ParseToolMode", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        var result = method!.Invoke(null, new object[] { "local" });
        Assert.Equal(VpkToolConfiguration.ToolMode.Local, result);
    }

    [Fact]
    public void PackTask_ToolModeParser_Global_ShouldParse()
    {
        var method = typeof(PackTask).GetMethod("ParseToolMode", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        var result = method!.Invoke(null, new object[] { "global" });
        Assert.Equal(VpkToolConfiguration.ToolMode.Global, result);
    }

    [Fact]
    public void PackTask_ToolModeParser_Invalid_ShouldDefaultToAuto()
    {
        var method = typeof(PackTask).GetMethod("ParseToolMode", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        var result = method!.Invoke(null, new object[] { "invalid" });
        Assert.Equal(VpkToolConfiguration.ToolMode.Auto, result);
        
        result = method.Invoke(null, new object?[] { null });
        Assert.Equal(VpkToolConfiguration.ToolMode.Auto, result);
    }

    [Fact]
    public void PackTask_BuildPackArguments_ShouldIncludeRequiredArguments()
    {
        var task = new PackTask
        {
            BuildEngine = new MockBuildEngine(),
            PackId = "TestApp",
            PackVersion = "1.0.0",
            PackDirectory = "C:\\Publish",
            ReleaseDir = "C:\\Releases",
            TargetFramework = "net8.0"
        };
        
        // Use reflection to call private BuildPackArguments method
        var method = typeof(PackTask).GetMethod("BuildPackArguments", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var args = (string[])method!.Invoke(task, null)!;
        
        Assert.Contains("pack", args);
        Assert.Contains("--packId", args);
        Assert.Contains("TestApp", args);
        Assert.Contains("--packVersion", args);
        Assert.Contains("1.0.0", args);
        Assert.Contains("--packDir", args);
        Assert.Contains("C:\\Publish", args);
        Assert.Contains("--outputDir", args);
        Assert.Contains("C:\\Releases", args);
    }

    [Fact]
    public void PackTask_BuildPackArguments_ShouldIncludeOptionalArguments()
    {
        var task = new PackTask
        {
            BuildEngine = new MockBuildEngine(),
            PackId = "TestApp",
            PackVersion = "1.0.0",
            PackDirectory = "C:\\Publish",
            ReleaseDir = "C:\\Releases",
            TargetFramework = "net8.0",
            PackTitle = "Test Application",
            PackAuthors = "Test Author",
            Icon = "C:\\icon.ico",
            Channel = "stable"
        };
        
        var method = typeof(PackTask).GetMethod("BuildPackArguments", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var args = (string[])method!.Invoke(task, null)!;
        
        Assert.Contains("--packTitle", args);
        Assert.Contains("--packAuthors", args);
        Assert.Contains("--icon", args);
        Assert.Contains("--channel", args);
    }

    [Fact]
    public void PackTask_BuildPackArguments_ShouldIncludeBooleanFlags()
    {
        var task = new PackTask
        {
            BuildEngine = new MockBuildEngine(),
            PackId = "TestApp",
            PackVersion = "1.0.0",
            PackDirectory = "C:\\Publish",
            ReleaseDir = "C:\\Releases",
            TargetFramework = "net8.0",
            NoPortable = true,
            SkipVelopackAppCheck = true
        };
        
        var method = typeof(PackTask).GetMethod("BuildPackArguments", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var args = (string[])method!.Invoke(task, null)!;
        
        Assert.Contains("--noPortable", args);
        Assert.Contains("--skipVeloAppCheck", args);
    }

    [Fact]
    public void PackTask_BuildPackArguments_ShouldNotIncludeFalseBooleans()
    {
        var task = new PackTask
        {
            BuildEngine = new MockBuildEngine(),
            PackId = "TestApp",
            PackVersion = "1.0.0",
            PackDirectory = "C:\\Publish",
            ReleaseDir = "C:\\Releases",
            TargetFramework = "net8.0",
            NoPortable = false,
            NoInst = false
        };
        
        var method = typeof(PackTask).GetMethod("BuildPackArguments", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var args = (string[])method!.Invoke(task, null)!;
        
        Assert.DoesNotContain("--noPortable", args);
        Assert.DoesNotContain("--noInst", args);
    }

    [Fact]
    public void PackTask_BuildPackArguments_ShouldHandleSigningParameters()
    {
        var task = new PackTask
        {
            BuildEngine = new MockBuildEngine(),
            PackId = "TestApp",
            PackVersion = "1.0.0",
            PackDirectory = "C:\\Publish",
            ReleaseDir = "C:\\Releases",
            TargetFramework = "net8.0",
            SignParameters = "/sha1 abc123",
            SignParallel = 5,
            SignExclude = @"\.dll$"
        };
        
        var method = typeof(PackTask).GetMethod("BuildPackArguments", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var args = (string[])method!.Invoke(task, null)!;
        
        Assert.Contains("--signParams", args);
        Assert.Contains("/sha1 abc123", args);
        Assert.Contains("--signParallel", args);
        Assert.Contains("5", args);
        Assert.Contains("--signExclude", args);
    }
}

/// <summary>
/// Mock build engine for testing MSBuild tasks
/// </summary>
internal class MockBuildEngine : IBuildEngine
{
    public List<string> Messages { get; } = new();
    public List<string> Warnings { get; } = new();
    public List<string> Errors { get; } = new();

    public bool ContinueOnError => false;
    public int LineNumberOfTaskNode => 0;
    public int ColumnNumberOfTaskNode => 0;
    public string ProjectFileOfTaskNode => "TestProject.csproj";

    public bool BuildProjectFile(string projectFileName, string[] targetNames, 
        System.Collections.IDictionary globalProperties, System.Collections.IDictionary targetOutputs)
    {
        return true;
    }

    public void LogCustomEvent(CustomBuildEventArgs e)
    {
        Messages.Add(e.Message ?? string.Empty);
    }

    public void LogErrorEvent(BuildErrorEventArgs e)
    {
        Errors.Add(e.Message ?? string.Empty);
    }

    public void LogMessageEvent(BuildMessageEventArgs e)
    {
        Messages.Add(e.Message ?? string.Empty);
    }

    public void LogWarningEvent(BuildWarningEventArgs e)
    {
        Warnings.Add(e.Message ?? string.Empty);
    }
}
