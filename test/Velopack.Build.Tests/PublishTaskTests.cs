using Microsoft.Build.Framework;

namespace Velopack.Build.Tests;

/// <summary>
/// Tests for PublishTask MSBuild task
/// </summary>
public class PublishTaskTests
{
    [Fact]
    public void PublishTask_ShouldHaveRequiredProperties()
    {
        var task = new PublishTask();
        
        // Verify required properties exist
        Assert.NotNull(task.GetType().GetProperty(nameof(PublishTask.ReleaseDirectory)));
        
        // Verify required attribute
        var releaseDirectoryProp = task.GetType().GetProperty(nameof(PublishTask.ReleaseDirectory))!;
        Assert.NotNull(releaseDirectoryProp.GetCustomAttributes(typeof(RequiredAttribute), false).FirstOrDefault());
    }

    [Fact]
    public void PublishTask_DefaultValues_ShouldBeSet()
    {
        var task = new PublishTask();
        
        Assert.Equal("Auto", task.VelopackToolMode);
        Assert.False(task.VelopackToolPrerelease);
        Assert.False(task.VelopackSkipToolInstall);
        Assert.False(task.WaitForLive);
    }

    [Fact]
    public void PublishTask_BuildPublishArguments_ShouldIncludeRequiredArguments()
    {
        var task = new PublishTask
        {
            BuildEngine = new MockBuildEngine(),
            ReleaseDirectory = "C:\\Releases"
        };
        
        // Use reflection to call private BuildPublishArguments method
        var method = typeof(PublishTask).GetMethod("BuildPublishArguments", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var args = (string[])method!.Invoke(task, null)!;
        
        Assert.Contains("flow", args);
        Assert.Contains("publish", args);
        Assert.Contains("--outputDir", args);
        Assert.Contains("C:\\Releases", args);
    }

    [Fact]
    public void PublishTask_BuildPublishArguments_ShouldIncludeChannel()
    {
        var task = new PublishTask
        {
            BuildEngine = new MockBuildEngine(),
            ReleaseDirectory = "C:\\Releases",
            Channel = "stable"
        };
        
        var method = typeof(PublishTask).GetMethod("BuildPublishArguments", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var args = (string[])method!.Invoke(task, null)!;
        
        Assert.Contains("--channel", args);
        Assert.Contains("stable", args);
    }

    [Fact]
    public void PublishTask_BuildPublishArguments_ShouldIncludeWaitForLive()
    {
        var task = new PublishTask
        {
            BuildEngine = new MockBuildEngine(),
            ReleaseDirectory = "C:\\Releases",
            WaitForLive = true
        };
        
        var method = typeof(PublishTask).GetMethod("BuildPublishArguments", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var args = (string[])method!.Invoke(task, null)!;
        
        Assert.Contains("--waitForLive", args);
    }

    [Fact]
    public void PublishTask_BuildPublishArguments_ShouldNotIncludeWaitForLiveWhenFalse()
    {
        var task = new PublishTask
        {
            BuildEngine = new MockBuildEngine(),
            ReleaseDirectory = "C:\\Releases",
            WaitForLive = false
        };
        
        var method = typeof(PublishTask).GetMethod("BuildPublishArguments", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        
        var args = (string[])method!.Invoke(task, null)!;
        
        Assert.DoesNotContain("--waitForLive", args);
    }

    [Fact]
    public void PublishTask_ToolModeParser_ShouldWork()
    {
        var method = typeof(PublishTask).GetMethod("ParseToolMode", 
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
        
        Assert.NotNull(method);
        
        var result = method!.Invoke(null, new object[] { "local" });
        Assert.Equal(VpkToolConfiguration.ToolMode.Local, result);
        
        result = method.Invoke(null, new object[] { "global" });
        Assert.Equal(VpkToolConfiguration.ToolMode.Global, result);
        
        result = method.Invoke(null, new object[] { "auto" });
        Assert.Equal(VpkToolConfiguration.ToolMode.Auto, result);
    }

    [Theory]
    [InlineData("", "")]
    [InlineData("https://api.velopack.io", "")]
    [InlineData("", "key123")]
    [InlineData("https://api.velopack.io", "key123")]
    public void PublishTask_Properties_ShouldAcceptVariousValues(string serviceUrl, string apiKey)
    {
        var task = new PublishTask
        {
            BuildEngine = new MockBuildEngine(),
            ReleaseDirectory = "C:\\Releases",
            ServiceUrl = serviceUrl,
            ApiKey = apiKey
        };
        
        Assert.Equal(serviceUrl, task.ServiceUrl);
        Assert.Equal(apiKey, task.ApiKey);
    }
}
