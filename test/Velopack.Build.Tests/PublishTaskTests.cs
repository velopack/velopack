namespace Velopack.Build.Tests;

public class PublishTaskTests
{
    [Fact]
    public void BuildJsonOptions_IncludesPublishOptions()
    {
        var task = new TestPublishTask {
            ReleaseDirectory = "C:\\Releases",
            ServiceUrl = "https://api.velopack.io",
            Channel = "beta",
            ApiKey = "test-api-key",
            Timeout = "2.5",
            WaitForLive = true,
        };

        var options = task.GetJsonOptions();

        Assert.Equal("C:\\Releases", options["ReleaseDirectory"]);
        Assert.Equal("https://api.velopack.io", options["VelopackBaseUrl"]);
        Assert.Equal("beta", options["Channel"]);
        Assert.Equal("test-api-key", options["ApiKey"]);
        Assert.Equal(2.5d, options["Timeout"]);
        Assert.Equal(true, options["WaitForLive"]);
    }

    [Fact]
    public void BuildJsonOptions_OmitsUnsetOptionalOptions()
    {
        var task = new TestPublishTask {
            ReleaseDirectory = "C:\\Releases",
        };

        var options = task.GetJsonOptions();

        Assert.Equal("C:\\Releases", options["ReleaseDirectory"]);
        Assert.DoesNotContain("VelopackBaseUrl", options.Keys);
        Assert.DoesNotContain("Channel", options.Keys);
        Assert.DoesNotContain("ApiKey", options.Keys);
        Assert.DoesNotContain("Timeout", options.Keys);
        Assert.DoesNotContain("WaitForLive", options.Keys);
    }

    [Fact]
    public void BuildJsonOptions_RejectsInvalidTimeout()
    {
        var task = new TestPublishTask {
            ReleaseDirectory = "C:\\Releases",
            Timeout = "not-a-number",
        };

        var exception = Assert.Throws<FormatException>(() => task.GetJsonOptions());

        Assert.Contains("Timeout must be a number", exception.Message);
    }

    private sealed class TestPublishTask : Build.PublishTask
    {
        public Dictionary<string, object?> GetJsonOptions() => BuildJsonOptions();
    }
}
