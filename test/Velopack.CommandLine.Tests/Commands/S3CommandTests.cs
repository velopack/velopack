using System.CommandLine;
using Velopack.Deployment;
using Velopack.Vpk;
using Velopack.Vpk.Commands.Deployment;

namespace Velopack.CommandLine.Tests.Commands;

public abstract class S3CommandTests<T> : BaseCommandTests<T>
    where T : S3BaseCommand, new()
{
    protected S3DownloadOptions ParseAndMap(string cli)
    {
        S3BaseCommand command = new T();
        command.ParseAndApply(cli);
        return OptionMapper.Map<S3DownloadOptions>(command);
    }

    [Fact]
    public void Command_WithRequiredEndpointOptions_ParsesValue()
    {
        S3BaseCommand command = new T();

        string cli = $"--keyId \"some key\" --secret \"shhhh\" --endpoint \"http://endpoint\" --bucket \"a-bucket\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Empty(parseResult.Errors);
        Assert.Equal("some key", command.KeyId);
        Assert.Equal("shhhh", command.Secret);
        Assert.Equal("http://endpoint", command.Endpoint);
        Assert.Equal("a-bucket", command.Bucket);
    }

    [Fact]
    public void Command_WithRequiredRegionOptions_ParsesValue()
    {
        S3BaseCommand command = new T();

        string cli = $"--keyId \"some key\" --secret \"shhhh\" --region \"us-west-1\" --bucket \"a-bucket\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Empty(parseResult.Errors);
        Assert.Equal("some key", command.KeyId);
        Assert.Equal("shhhh", command.Secret);
        Assert.Equal("us-west-1", command.Region);
        Assert.Equal("a-bucket", command.Bucket);
    }

    [Fact]
    public void Command_WithInvalidRegion_FailsValidation()
    {
        var options = ParseAndMap($"--keyId \"some key\" --secret \"shhhh\" --bucket \"a-bucket\" --region \"not-a-region\"");

        var result = new S3DownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("lookup failed, is this a valid AWS region?"));
    }

    [Fact]
    public void Command_WithoutRegionAndEndpoint_FailsValidation()
    {
        var options = ParseAndMap($"--keyId \"some key\" --secret \"shhhh\" --bucket \"a-bucket\"");

        var result = new S3DownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("At least one of 'region' and 'endpoint' options are required."));
    }

    [Fact]
    public void Command_WithBothRegionAndEndpoint_FailsValidation()
    {
        var options = ParseAndMap($"--keyId \"some key\" --secret \"shhhh\" --region \"us-west-1\" --endpoint \"http://endpoint\" --bucket \"a-bucket\"");

        var result = new S3DownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("Cannot use 'region' and 'endpoint' options together"));
    }

    [Fact]
    public void Command_WithDisableChecksumValidation_ParsesValue()
    {
        var options = ParseAndMap(GetRequiredDefaultOptions() + "--disableChecksumValidation");

        Assert.True(options.DisableChecksumValidation);
    }

    [Fact]
    public void Command_WithoutDisableChecksumValidation_DefaultsToFalse()
    {
        var options = ParseAndMap(GetRequiredDefaultOptions());

        Assert.False(options.DisableChecksumValidation);
    }

    [Fact]
    public void Command_WithoutBucket_FailsValidation()
    {
        var options = ParseAndMap($"--keyId \"some key\" --secret \"shhhh\" --region \"us-west-1\"");

        var result = new S3DownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.PropertyName == "Bucket");
    }

    protected override string GetRequiredDefaultOptions()
    {
        return $"--keyId \"some key\" --secret \"shhhh\" --endpoint \"http://endpoint\" --bucket \"a-bucket\" ";
    }
}

public class S3DownloadCommandTests : S3CommandTests<S3DownloadCommand>
{ }

public class S3UploadCommandTests : S3CommandTests<S3UploadCommand>
{
    public override bool ShouldBeNonEmptyReleaseDir => true;
}
