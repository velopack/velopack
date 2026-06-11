using System.CommandLine;
using Velopack.Deployment;
using Velopack.Vpk;
using Velopack.Vpk.Commands.Deployment;

namespace Velopack.CommandLine.Tests.Commands;

public abstract class AzureCommandTests<T> : BaseCommandTests<T>
    where T : AzureBaseCommand, new()
{
    protected override string GetRequiredDefaultOptions()
    {
        return "--account \"test-account\" --key \"test-key\" --container \"test-container\" ";
    }

    protected AzureDownloadOptions ParseAndMap(string cli)
    {
        AzureBaseCommand command = new T();
        command.ParseAndApply(cli);
        return OptionMapper.Map<AzureDownloadOptions>(command);
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
        Assert.Equal("https://endpoint", command.Endpoint);
        Assert.Equal("mycontainer", command.Container);
    }

    [Fact]
    public void Command_WithoutKeyOrSas_FailsValidation()
    {
        var options = ParseAndMap("--account \"account-name\" --container \"mycontainer\"");

        var result = new AzureDownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("At least one of 'sas' and 'key' options are required."));
    }

    [Fact]
    public void Command_WithBothKeyAndSas_FailsValidation()
    {
        var options = ParseAndMap("--account \"account-name\" --container \"mycontainer\" --key \"k\" --sas \"s\"");

        var result = new AzureDownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Cannot use 'sas' and 'key' options together"));
    }

    [Fact]
    public void Command_WithoutAccount_FailsValidation()
    {
        var options = ParseAndMap("--key \"k\" --container \"mycontainer\"");

        var result = new AzureDownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.PropertyName == "Account");
    }
}

public class AzureDownloadCommandTests : AzureCommandTests<AzureDownloadCommand>
{
    [Fact]
    public void Folder_WithPath_ParsesValue()
    {
        var command = new AzureDownloadCommand();

        string cli = GetRequiredDefaultOptions() + " --prefix \"releases/v1\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal("releases/v1", command.Prefix);
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

        string cli = GetRequiredDefaultOptions() + " --prefix \"releases/v1\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal("releases/v1", command.Prefix);
    }
}
