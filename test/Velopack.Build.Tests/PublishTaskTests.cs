namespace Velopack.Build.Tests;

public class PublishTaskTests
{
    [Theory]
    [MemberData(nameof(GetRequiredArgumentsData))]
    public void BuildArguments_IncludesRequiredArguments(
        string releaseDirectory, string[] expectedArgs)
    {
        var task = CreatePublishTask(releaseDirectory);
        var args = task.BuildPublishArguments();

        foreach (var expectedArg in expectedArgs)
        {
            Assert.Contains(expectedArg, args);
        }
    }

    public static TheoryData<string, string[]> GetRequiredArgumentsData()
    {
        var data = new TheoryData<string, string[]> {
            {
                "C:\\Release",
                new[] { "publish", "--legacyConsole", "--yes", "--outputDir", "C:\\Release" }
            },
            {
                "/home/user/releases",
                new[] { "publish", "--legacyConsole", "--yes", "--outputDir", "/home/user/releases" }
            }
        };
        return data;
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildArguments_ExcludesOptionalArgument_WhenNullOrWhitespace(string? value)
    {
        var task = CreateMinimalPublishTask();
        task.Channel = value;

        var args = task.BuildPublishArguments();

        Assert.DoesNotContain("--channel", args);
    }

    [Fact]
    public void BuildArguments_IncludesChannel_WhenProvided()
    {
        var task = CreateMinimalPublishTask();
        task.Channel = "stable";

        var args = task.BuildPublishArguments();

        Assert.Contains("--channel", args);
        Assert.Contains("stable", args);
    }

    [Fact]
    public void BuildArguments_IncludesTimeout_WhenProvided()
    {
        var task = CreateMinimalPublishTask();
        task.Timeout = "300";

        var args = task.BuildPublishArguments();

        Assert.Contains("--timeout", args);
        Assert.Contains("300", args);
    }

    [Fact]
    public void BuildArguments_IncludesWaitForLive_WhenTrue()
    {
        var task = CreateMinimalPublishTask();
        task.WaitForLive = true;

        var args = task.BuildPublishArguments();

        Assert.Contains("--waitForLive", args);
    }

    [Fact]
    public void BuildArguments_ExcludesWaitForLive_WhenFalse()
    {
        var task = CreateMinimalPublishTask();
        task.WaitForLive = false;

        var args = task.BuildPublishArguments();

        Assert.DoesNotContain("--waitForLive", args);
    }

    [Fact]
    public void BuildArguments_IncludesAllArguments_InComplexScenario()
    {
        var task = new PublishTask
        {
            ReleaseDirectory = "C:\\Releases",
            Channel = "beta",
            Timeout = "600",
            WaitForLive = true,
            ServiceUrl = "https://api.velopack.io",
            ApiKey = "test-api-key-12345"
        };

        var args = task.BuildPublishArguments();

        var expectedArgs = new[]
        {
            "publish", "--legacyConsole", "--yes",
            "--outputDir", "C:\\Releases",
            "--channel", "beta",
            "--timeout", "600",
            "--waitForLive"
        };

        foreach (var expectedArg in expectedArgs)
        {
            Assert.Contains(expectedArg, args);
        }
    }

    [Fact]
    public void BuildArguments_StartsWithPublishCommand()
    {
        var task = CreateMinimalPublishTask();
        var args = task.BuildPublishArguments();

        Assert.Equal("publish", args[0]);
    }

    [Fact]
    public void BuildArguments_IncludesLegacyConsoleAndYes()
    {
        var task = CreateMinimalPublishTask();
        var args = task.BuildPublishArguments();

        Assert.Contains("--legacyConsole", args);
        Assert.Contains("--yes", args);
    }

    [Fact]
    public void BuildEnvironmentVariables_IncludesServiceUrl_WhenProvided()
    {
        var task = CreateMinimalPublishTask();
        task.ServiceUrl = "https://api.velopack.io";

        var envVars = task.BuildPublishEnvironmentVariables();

        Assert.True(envVars.ContainsKey("VPK_FLOW_SERVICE_URL"));
        Assert.Equal("https://api.velopack.io", envVars["VPK_FLOW_SERVICE_URL"]);
    }

    [Fact]
    public void BuildEnvironmentVariables_IncludesApiKey_WhenProvided()
    {
        var task = CreateMinimalPublishTask();
        task.ApiKey = "test-api-key-12345";

        var envVars = task.BuildPublishEnvironmentVariables();

        Assert.True(envVars.ContainsKey("VPK_FLOW_API_KEY"));
        Assert.Equal("test-api-key-12345", envVars["VPK_FLOW_API_KEY"]);
    }

    [Fact]
    public void BuildEnvironmentVariables_ExcludesServiceUrl_WhenNotProvided()
    {
        var task = CreateMinimalPublishTask();
        task.ServiceUrl = null;

        var envVars = task.BuildPublishEnvironmentVariables();

        Assert.False(envVars.ContainsKey("VPK_FLOW_SERVICE_URL"));
    }

    [Fact]
    public void BuildEnvironmentVariables_ExcludesApiKey_WhenNotProvided()
    {
        var task = CreateMinimalPublishTask();
        task.ApiKey = null;

        var envVars = task.BuildPublishEnvironmentVariables();

        Assert.False(envVars.ContainsKey("VPK_FLOW_API_KEY"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildEnvironmentVariables_ExcludesServiceUrl_WhenNullOrWhitespace(string? value)
    {
        var task = CreateMinimalPublishTask();
        task.ServiceUrl = value;

        var envVars = task.BuildPublishEnvironmentVariables();

        Assert.False(envVars.ContainsKey("VPK_FLOW_SERVICE_URL"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void BuildEnvironmentVariables_ExcludesApiKey_WhenNullOrWhitespace(string? value)
    {
        var task = CreateMinimalPublishTask();
        task.ApiKey = value;

        var envVars = task.BuildPublishEnvironmentVariables();

        Assert.False(envVars.ContainsKey("VPK_FLOW_API_KEY"));
    }

    [Fact]
    public void BuildEnvironmentVariables_IncludesBothVariables_WhenBothProvided()
    {
        var task = CreateMinimalPublishTask();
        task.ServiceUrl = "https://api.velopack.io";
        task.ApiKey = "test-api-key-12345";

        var envVars = task.BuildPublishEnvironmentVariables();

        Assert.True(envVars.ContainsKey("VPK_FLOW_SERVICE_URL"));
        Assert.Equal("https://api.velopack.io", envVars["VPK_FLOW_SERVICE_URL"]);
        Assert.True(envVars.ContainsKey("VPK_FLOW_API_KEY"));
        Assert.Equal("test-api-key-12345", envVars["VPK_FLOW_API_KEY"]);
    }

    [Theory]
    [MemberData(nameof(GetMultipleChannelData))]
    public void BuildArguments_HandlesVariousChannels(string channel)
    {
        var task = CreateMinimalPublishTask();
        task.Channel = channel;

        var args = task.BuildPublishArguments();

        Assert.Contains("--channel", args);
        Assert.Contains(channel, args);
    }

    public static TheoryData<string> GetMultipleChannelData()
    {
        var data = new TheoryData<string>
        {
            "stable",
            "beta",
            "alpha",
            "preview",
            "custom-channel"
        };
        return data;
    }

    [Theory]
    [InlineData("60")]
    [InlineData("300")]
    [InlineData("600")]
    [InlineData("1200")]
    public void BuildArguments_HandlesVariousTimeouts(string timeout)
    {
        var task = CreateMinimalPublishTask();
        task.Timeout = timeout;

        var args = task.BuildPublishArguments();

        Assert.Contains("--timeout", args);
        Assert.Contains(timeout, args);
    }

    private static PublishTask CreateMinimalPublishTask()
    {
        return new PublishTask
        {
            ReleaseDirectory = "C:\\Release"
        };
    }

    private static PublishTask CreatePublishTask(string releaseDirectory)
    {
        return new PublishTask
        {
            ReleaseDirectory = releaseDirectory
        };
    }

    public class PublishTask : Build.PublishTask
    {
        public string[] BuildPublishArguments()
        {
            return BuildArguments();
        }

        public Dictionary<string, string> BuildPublishEnvironmentVariables()
        {
            return BuildEnvironmentVariables();
        }
    }
}
