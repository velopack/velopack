
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

    [Fact]
    public void Header_WithMultipleValues_ParsesAndMaps()
    {
        var command = new HttpDownloadCommand();

        ParseResult parseResult = command.ParseAndApply(
            $"--url \"https://clowd.squirrel.com\" --header \"Authorization: Bearer test\" --header \"X-API-Key: Bleh\"");
        var options = OptionMapper.Map<HttpDownloadOptions>(command);

        Assert.Empty(parseResult.Errors);
        Assert.Equal(new[] { "Authorization: Bearer test", "X-API-Key: Bleh" }, options.Headers);

        var result = new HttpDownloadOptionsValidator().Validate(options);
        Assert.True(result.IsValid);
    }

    [Fact]
    public void Header_Missing_PassesValidation()
    {
        var command = new HttpDownloadCommand();
        command.ParseAndApply($"--url \"https://clowd.squirrel.com\"");
        var options = OptionMapper.Map<HttpDownloadOptions>(command);

        var result = new HttpDownloadOptionsValidator().Validate(options);

        Assert.True(result.IsValid);
    }

    [Theory]
    [InlineData("NoColonHere")]
    [InlineData(": value-without-name")]
    public void Header_WithInvalidFormat_FailsValidation(string header)
    {
        var command = new HttpDownloadCommand();
        command.ParseAndApply($"--url \"https://clowd.squirrel.com\" --header \"{header}\"");
        var options = OptionMapper.Map<HttpDownloadOptions>(command);

        var result = new HttpDownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("must be in the format 'Name: Value'"));
    }

    [Fact]
    public void AllowEmptyChannel_WithFlag_ParsesAndMaps()
    {
        var command = new HttpDownloadCommand();

        ParseResult parseResult = command.ParseAndApply($"--url \"https://clowd.squirrel.com\" --allowEmptyChannel");
        var options = OptionMapper.Map<HttpDownloadOptions>(command);

        Assert.Empty(parseResult.Errors);
        Assert.True(command.AllowEmptyChannel);
        Assert.True(options.AllowEmptyChannel);
    }

    [Fact]
    public void AllowEmptyChannel_Missing_DefaultsToFalse()
    {
        var command = new HttpDownloadCommand();

        ParseResult parseResult = command.ParseAndApply($"--url \"https://clowd.squirrel.com\"");
        var options = OptionMapper.Map<HttpDownloadOptions>(command);

        Assert.Empty(parseResult.Errors);
        Assert.False(command.AllowEmptyChannel);
        Assert.False(options.AllowEmptyChannel);
    }

    protected override string GetRequiredDefaultOptions()
    {
        return $"--url \"https://clowd.squirrel.com\" ";
    }
}
