using System.CommandLine;
using Velopack.Vpk.Commands;

namespace Velopack.CommandLine.Tests.Commands;

public abstract class S3CommandTests<T> : BaseCommandTests<T>
    where T : S3BaseCommand, new()
{
    [Fact]
    public void Command_WithRequiredEndpointOptions_ParsesValue()
    {
        S3BaseCommand command = new T();

        string cli = $"--keyId \"some key\" --secret \"shhhh\" --endpoint \"http://endpoint\" --bucket \"a-bucket\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Empty(parseResult.Errors);
        Assert.Equal("some key", command.KeyId);
        Assert.Equal("shhhh", command.Secret);
        Assert.Equal("http://endpoint/", command.Endpoint);
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
    public void Command_WithoutRegionArgumentValue_ShowsError()
    {
        S3BaseCommand command = new T();

        string cli = $"--keyId \"some key\" --secret \"shhhh\" --bucket \"a-bucket\"  --region \"\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal(1, parseResult.Errors.Count);
        //Assert.Equal(command.Region, parseResult.Errors[0].SymbolResult?.Symbol);
        Assert.StartsWith("A region value is required", parseResult.Errors[0].Message);
    }

    [Fact]
    public void Command_WithoutRegionAndEndpoint_ShowsError()
    {
        S3BaseCommand command = new T();

        string cli = $"--keyId \"some key\" --secret \"shhhh\" --bucket \"a-bucket\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal(1, parseResult.Errors.Count);
        Assert.StartsWith("At least one of the following options are required '--region' and '--endpoint'", parseResult.Errors[0].Message);
    }

    [Fact]
    public void Command_WithBothRegionAndEndpoint_ShowsError()
    {
        S3BaseCommand command = new T();

        string cli = $"--keyId \"some key\" --secret \"shhhh\" --region \"us-west-1\" --endpoint \"http://endpoint\" --bucket \"a-bucket\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal(1, parseResult.Errors.Count);
        Assert.StartsWith("Cannot use '--region' and '--endpoint' options together", parseResult.Errors[0].Message);
    }

    //[Fact]
    //public void PathPrefix_WithPath_ParsesValue()
    //{
    //    S3BaseCommand command = new T();

    //    string cli = GetRequiredDefaultOptions() + $"--pathPrefix \"sub-folder\"";
    //    ParseResult parseResult = command.ParseAndApply(cli);

    //    Assert.Equal("sub-folder", command.PathPrefix);
    //}

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

    //[Fact]
    //public void KeepMaxReleases_WithNumber_ParsesValue()
    //{
    //    var command = new S3UploadCommand();

    //    string cli = GetRequiredDefaultOptions() + "--keepMaxReleases 42";
    //    ParseResult parseResult = command.ParseAndApply(cli);

    //    Assert.Equal(42, command.KeepMaxReleases);
    //}
}
