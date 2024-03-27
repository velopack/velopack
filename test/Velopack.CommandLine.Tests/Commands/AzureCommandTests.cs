using System.CommandLine;
using Velopack.Vpk.Commands.Deployment;

namespace Velopack.CommandLine.Tests.Commands;

public abstract class AzureCommandTests<T> : BaseCommandTests<T>
    where T : AzureBaseCommand, new()
{
    [Fact]
    public void Command_WithRequiredEndpointOptions_ParsesValue()
    {
        AzureBaseCommand command = new T();

        string cli = $"--account \"account-name\" --key \"shhhh\" --endpoint \"https://endpoint\" --container \"mycontainer\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Empty(parseResult.Errors);
        Assert.Equal("account-name", command.Account);
        Assert.Equal("shhhh", command.Key);
        Assert.Equal("https://endpoint/", command.Endpoint);
        Assert.Equal("mycontainer", command.ContainerName);
    }
}

public class AzureDownloadCommandTests : AzureCommandTests<AzureDownloadCommand>
{ }

public class AzureUploadCommandTests : AzureCommandTests<AzureUploadCommand>
{
    public override bool ShouldBeNonEmptyReleaseDir => true;

    //[Fact]
    //public void KeepMaxReleases_WithNumber_ParsesValue()
    //{
    //    var command = new S3UploadCommand();

    //    string cli = GetRequiredDefaultOptions() + "--keepMaxReleases 42";
    //    ParseResult parseResult = command.ParseAndApply(cli);

    //    Assert.Equal(42, command.KeepMaxReleases);
    //}
}
