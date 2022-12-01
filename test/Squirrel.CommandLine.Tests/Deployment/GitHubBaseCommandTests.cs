using System.CommandLine;
using System.CommandLine.Parsing;
using Squirrel.CommandLine.Deployment;
using Xunit;

namespace Squirrel.CommandLine.Tests.Deployment
{
    public abstract class GitHubBaseCommandTests<T> : BaseCommandTests<T>
        where T : GitHubBaseCommand, new()
    {
        [Fact]
        public void RepoUrl_WithUrl_ParsesValue()
        {
            GitHubBaseCommand command = new T();

            ParseResult parseResult = command.Parse($"--repoUrl \"http://clowd.squirrel.com\"");

            Assert.Empty(parseResult.Errors);
            Assert.Equal("http://clowd.squirrel.com/", parseResult.GetValueForOption(command.RepoUrl)?.AbsoluteUri);
        }

        [Fact]
        public void RepoUrl_WithNonHttpValue_ShowsError()
        {
            GitHubBaseCommand command = new T();

            ParseResult parseResult = command.Parse($"--repoUrl \"file://clowd.squirrel.com\"");
            
            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.RepoUrl, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("--repoUrl must contain a Uri with one of the following schems: http, https.", parseResult.Errors[0].Message);
        }

        [Fact]
        public void RepoUrl_WithRelativeUrl_ShowsError()
        {
            GitHubBaseCommand command = new T();

            ParseResult parseResult = command.Parse($"--repoUrl \"clowd.squirrel.com\"");
            
            Assert.Equal(1, parseResult.Errors.Count);
            Assert.Equal(command.RepoUrl, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("--repoUrl must contain an absolute Uri.", parseResult.Errors[0].Message);
        }

        [Fact]
        public void Token_WithValue_ParsesValue()
        {
            GitHubBaseCommand command = new T();

            string cli = GetRequiredDefaultOptions() + $"--token \"abc\"";
            ParseResult parseResult = command.Parse(cli);

            Assert.Equal("abc", parseResult.GetValueForOption(command.Token));
        }

        protected override string GetRequiredDefaultOptions()
        {
            return $"--repoUrl \"https://clowd.squirrel.com\" ";
        }
    }
}
