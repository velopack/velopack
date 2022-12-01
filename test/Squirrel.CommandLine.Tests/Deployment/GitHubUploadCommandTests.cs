using System.CommandLine;
using System.CommandLine.Parsing;
using Squirrel.CommandLine.Deployment;
using Xunit;

namespace Squirrel.CommandLine.Tests.Deployment
{
    public class GitHubUploadCommandTests : GitHubBaseCommandTests<GitHubUploadCommand>
    {
        [Fact]
        public void Publish_BareOption_SetsFlag()
        {
            var command = new GitHubUploadCommand();

            string cli = GetRequiredDefaultOptions() + "--publish";
            ParseResult parseResult = command.Parse(cli);

            Assert.True(parseResult.GetValueForOption(command.Publish));
        }

        [Fact]
        public void ReleaseName_WithName_ParsesValue()
        {
            var command = new GitHubUploadCommand();

            string cli = GetRequiredDefaultOptions() + $"--releaseName \"my release\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("my release", parseResult.GetValueForOption(command.ReleaseName));
        }
    }
}
