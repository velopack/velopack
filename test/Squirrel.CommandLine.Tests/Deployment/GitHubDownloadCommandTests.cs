using System.CommandLine;
using System.CommandLine.Parsing;
using Squirrel.CommandLine.Deployment;
using Squirrel.CommandLine.Windows;
using Xunit;

namespace Squirrel.CommandLine.Tests.Deployment
{
    public class GitHubDownloadCommandTests : GitHubBaseCommandTests<GitHubDownloadCommand>
    {
        [Fact]
        public void Pre_BareOption_SetsFlag()
        {
            var command = new GitHubDownloadCommand();

            string cli = GetRequiredDefaultOptions() + "--pre";
            ParseResult parseResult = command.Parse(cli);
            
            Assert.True(parseResult.GetValueForOption(command.Pre));
        }
    }
}
