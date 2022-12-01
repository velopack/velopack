using System.CommandLine;
using System.CommandLine.Parsing;
using Squirrel.CommandLine.Deployment;
using Xunit;

namespace Squirrel.CommandLine.Tests.Deployment
{
    public abstract class S3BaseCommandTests<T> : BaseCommandTests<T>
        where T : S3BaseCommand, new()
    {
        [Fact]
        public void Command_WithRequiredEndpointOptions_ParsesValue()
        {
            S3BaseCommand command = new T();

            string cli = $"--keyId \"some key\" --secret \"shhhh\" --endpoint \"http://endpoint\" --bucket \"a-bucket\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Empty(parseResult.Errors);
            Assert.Equal("some key", parseResult.GetValueForOption(command.KeyId));
            Assert.Equal("shhhh", parseResult.GetValueForOption(command.Secret));
            Assert.Equal("http://endpoint", parseResult.GetValueForOption(command.Endpoint));
            Assert.Equal("a-bucket", parseResult.GetValueForOption(command.Bucket));
        }

        [Fact]
        public void Command_WithRequiredRegionOptions_ParsesValue()
        {
            S3BaseCommand command = new T();

            string cli = $"--keyId \"some key\" --secret \"shhhh\" --region \"us-west-1\" --bucket \"a-bucket\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Empty(parseResult.Errors);
            Assert.Equal("some key", parseResult.GetValueForOption(command.KeyId));
            Assert.Equal("shhhh", parseResult.GetValueForOption(command.Secret));
            Assert.Equal("us-west-1", parseResult.GetValueForOption(command.Region));
            Assert.Equal("a-bucket", parseResult.GetValueForOption(command.Bucket));
        }

        [Fact]
        public void Command_WithoutRegionArgumentValue_ShowsError()
        {
            S3BaseCommand command = new T();

            string cli = $"--keyId \"some key\" --secret \"shhhh\" --bucket \"a-bucket\"  --region \"\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.Region, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("A region value is required", parseResult.Errors[0].Message);
        }

        [Fact]
        public void Command_WithoutRegionAndEndpoint_ShowsError()
        {
            S3BaseCommand command = new T();

            string cli = $"--keyId \"some key\" --secret \"shhhh\" --bucket \"a-bucket\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("At least one of the following options are required '--region' and '--endpoint'", parseResult.Errors[0].Message);
        }

        [Fact]
        public void Command_WithBothRegionAndEndpoint_ShowsError()
        {
            S3BaseCommand command = new T();

            string cli = $"--keyId \"some key\" --secret \"shhhh\" --region \"us-west-1\" --endpoint \"http://endpoint\" --bucket \"a-bucket\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("Cannot use '--region' and '--endpoint' options together", parseResult.Errors[0].Message);
        }

        [Fact]
        public void PathPrefix_WithPath_ParsesValue()
        {
            S3BaseCommand command = new T();

            string cli = GetRequiredDefaultOptions() + $"--pathPrefix \"sub-folder\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("sub-folder", parseResult.GetValueForOption(command.PathPrefix));
        }

        protected override string GetRequiredDefaultOptions()
        {
            return $"--keyId \"some key\" --secret \"shhhh\" --endpoint \"http://endpoint\" --bucket \"a-bucket\" ";
        }
    }
}
