
using System.CommandLine;
using Velopack.Deployment;
using Velopack.Vpk;
using Velopack.Vpk.Commands.Deployment;

namespace Velopack.CommandLine.Tests.Commands;

public class HttpDownloadCommandTests : BaseCommandTests<HttpDownloadCommand>
{
    [Fact]
    public void Url_WithUrl_ParsesValue()
    {
        var command = new HttpDownloadCommand();

        ParseResult parseResult = command.ParseAndApply($"--url \"http://clowd.squirrel.com\"");

        Assert.Empty(parseResult.Errors);
        Assert.Equal("http://clowd.squirrel.com", command.Url);
    }

    [Fact]
    public void Url_WithNonHttpValue_FailsValidation()
    {
        var command = new HttpDownloadCommand();
        command.ParseAndApply($"--url \"file://clowd.squirrel.com\"");
        var options = OptionMapper.Map<HttpDownloadOptions>(command);

        var result = new HttpDownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("url must contain an absolute http / https Uri"));
    }

    [Fact]
    public void Url_WithRelativeUrl_FailsValidation()
    {
        var command = new HttpDownloadCommand();
        command.ParseAndApply($"--url \"clowd.squirrel.com\"");
        var options = OptionMapper.Map<HttpDownloadOptions>(command);

        var result = new HttpDownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("url must contain an absolute http / https Uri"));
    }

    protected override string GetRequiredDefaultOptions()
    {
        return $"--url \"https://clowd.squirrel.com\" ";
    }
}
