﻿using System.CommandLine;
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
        Assert.Equal("https://endpoint/", options.Endpoint);
        Assert.Equal("mycontainer", options.Container);
        Assert.Equal(45, options.Timeout);
    }
}
