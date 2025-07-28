using System.CommandLine;
using Velopack.Vpk.Commands.Deployment;

namespace Velopack.CommandLine.Tests.Commands;

public abstract class AzureCommandTests<T> : BaseCommandTests<T>
    where T : AzureBaseCommand, new()
{
    protected override string GetRequiredDefaultOptions()
    {
        return "--account \"test-account\" --key \"test-key\" --container \"test-container\" ";
    }
    
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
        Assert.Equal("mycontainer", command.Container);
    }
}

public class AzureDownloadCommandTests : AzureCommandTests<AzureDownloadCommand>
{
    [Fact]
    public void Folder_WithPath_ParsesValue()
    {
        var command = new AzureDownloadCommand();

        string cli = GetRequiredDefaultOptions() + " --folder \"releases/v1\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal("releases/v1", command.Folder);
    }
}

public class AzureUploadCommandTests : AzureCommandTests<AzureUploadCommand>
{
    public override bool ShouldBeNonEmptyReleaseDir => true;
    
    protected override string GetRequiredDefaultOptions()
    {
        return base.GetRequiredDefaultOptions() + "--releaseDir \"./releases\" ";
    }

    [Fact]
    public void Folder_WithPath_ParsesValue()
    {
        var command = new AzureUploadCommand();

        string cli = GetRequiredDefaultOptions() + " --folder \"releases/v1\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal("releases/v1", command.Folder);
    }

}
