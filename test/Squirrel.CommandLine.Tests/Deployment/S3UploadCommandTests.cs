using System.CommandLine.Parsing;
using System.CommandLine;
using Squirrel.CommandLine.Deployment;
using Xunit;

namespace Squirrel.CommandLine.Tests.Deployment
{
    public class S3UploadCommandTests : S3BaseCommandTests<S3UploadCommand>
    {
        [Fact]
        public void Overwrite_BareOption_SetsFlag()
        {
            var command = new S3UploadCommand();

            string cli = GetRequiredDefaultOptions() + "--overwrite";
            ParseResult parseResult = command.Parse(cli);

            Assert.True(parseResult.GetValueForOption(command.Overwrite));
        }

        [Fact]
        public void KeepMaxReleases_WithNumber_ParsesValue()
        {
            var command = new S3UploadCommand();

            string cli = GetRequiredDefaultOptions() + "--keepMaxReleases 42";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal(42, parseResult.GetValueForOption(command.KeepMaxReleases));
        }
    }
}
