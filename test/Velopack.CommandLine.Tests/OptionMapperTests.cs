using System.CommandLine;
using Velopack.Deployment;
using Velopack.Vpk;
using Velopack.Vpk.Commands.Deployment;

namespace Velopack.CommandLine.Tests;

public class OptionMapperTests
{
    [Fact]
    public void MapRid()
    {
        Assert.True(OptionMapper.Map<RID>("win-x64") == RID.Parse("win-x64"));
    }

    [Fact]
    public void MapCommand()
    {
        AzureUploadCommand command = new();
        string cli = $"--account \"account-name\" --key \"shhhh\" --endpoint \"https://endpoint\" --container \"mycontainer\" --timeout 45";
        ParseResult parseResult = command.ParseAndApply(cli);
        var options = OptionMapper.Map<AzureUploadOptions>(command);

        Assert.Empty(parseResult.Errors);
        Assert.Equal("account-name", options.Account);
        Assert.Equal("shhhh", options.Key);
        Assert.Equal("https://endpoint", options.Endpoint);
        Assert.Equal("mycontainer", options.Container);
        Assert.Equal(45, options.Timeout);
    }

    [Theory]
    [InlineData(typeof(GitHubDownloadCommand), typeof(GitHubDownloadOptions))]
    [InlineData(typeof(GiteaDownloadCommand), typeof(GiteaDownloadOptions))]
    [InlineData(typeof(S3DownloadCommand), typeof(S3DownloadOptions))]
    [InlineData(typeof(AzureDownloadCommand), typeof(AzureDownloadOptions))]
    [InlineData(typeof(LocalDownloadCommand), typeof(LocalDownloadOptions))]
    [InlineData(typeof(HttpDownloadCommand), typeof(HttpDownloadOptions))]
    public void InfoJson_EveryDownloadCommand_ParsesAndMaps(Type commandType, Type optionsType)
    {
        var command = (Velopack.Vpk.Commands.BaseCommand) Activator.CreateInstance(commandType)!;
        ParseResult parseResult = command.ParseAndApply("--info-json");

        Assert.Empty(parseResult.Errors);
        var options = (RepositoryOptions) typeof(OptionMapper).GetMethod(nameof(OptionMapper.Map))!
            .MakeGenericMethod(optionsType)
            .Invoke(null, [command])!;
        Assert.True(options.InfoJson);
    }
}
