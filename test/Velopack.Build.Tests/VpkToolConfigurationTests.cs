namespace Velopack.Build.Tests;

/// <summary>
/// Tests for VpkToolConfiguration
/// </summary>
public class VpkToolConfigurationTests
{
    [Fact]
    public void DefaultConfiguration_ShouldHaveAutoMode()
    {
        var config = new VpkToolConfiguration();
        
        Assert.Equal(VpkToolConfiguration.ToolMode.Auto, config.Mode);
    }

    [Fact]
    public void DefaultConfiguration_ShouldNotAllowPrerelease()
    {
        var config = new VpkToolConfiguration();
        
        Assert.False(config.AllowPrerelease);
    }

    [Fact]
    public void DefaultConfiguration_ShouldNotSkipInstall()
    {
        var config = new VpkToolConfiguration();
        
        Assert.False(config.SkipInstall);
    }

    [Fact]
    public void DefaultConfiguration_ShouldUseCurrentDirectory()
    {
        var config = new VpkToolConfiguration();
        
        Assert.Equal(Environment.CurrentDirectory, config.WorkingDirectory);
    }

    [Fact]
    public void Configuration_ShouldAllowSettingAllProperties()
    {
        var config = new VpkToolConfiguration
        {
            Mode = VpkToolConfiguration.ToolMode.Local,
            Version = "1.2.3",
            AllowPrerelease = true,
            Source = "https://custom-source",
            SkipInstall = true,
            WorkingDirectory = "C:\\Test"
        };
        
        Assert.Equal(VpkToolConfiguration.ToolMode.Local, config.Mode);
        Assert.Equal("1.2.3", config.Version);
        Assert.True(config.AllowPrerelease);
        Assert.Equal("https://custom-source", config.Source);
        Assert.True(config.SkipInstall);
        Assert.Equal("C:\\Test", config.WorkingDirectory);
    }

    [Theory]
    [InlineData(VpkToolConfiguration.ToolMode.Auto)]
    [InlineData(VpkToolConfiguration.ToolMode.Local)]
    [InlineData(VpkToolConfiguration.ToolMode.Global)]
    public void ToolMode_AllValues_ShouldBeValid(VpkToolConfiguration.ToolMode mode)
    {
        var config = new VpkToolConfiguration { Mode = mode };
        
        Assert.Equal(mode, config.Mode);
    }
}

/// <summary>
/// Tests for ResolvedTool
/// </summary>
public class ResolvedToolTests
{
    [Fact]
    public void ResolvedTool_Local_ShouldHaveCorrectExecutionPrefix()
    {
        var tool = new ResolvedTool(isLocal: true, "1.0.0");
        
        Assert.True(tool.IsLocal);
        Assert.Equal("1.0.0", tool.Version);
        Assert.Equal("tool run vpk", tool.ExecutionPrefix);
    }

    [Fact]
    public void ResolvedTool_Global_ShouldHaveCorrectExecutionPrefix()
    {
        var tool = new ResolvedTool(isLocal: false, "1.0.0");
        
        Assert.False(tool.IsLocal);
        Assert.Equal("1.0.0", tool.Version);
        Assert.Equal("vpk", tool.ExecutionPrefix);
    }

    [Theory]
    [InlineData("1.0.0")]
    [InlineData("2.5.3-beta.1")]
    [InlineData("0.0.1-preview")]
    public void ResolvedTool_WithDifferentVersions_ShouldStoreVersion(string version)
    {
        var tool = new ResolvedTool(isLocal: true, version);
        
        Assert.Equal(version, tool.Version);
    }
}
