using System.CommandLine.Parsing;
using Squirrel.CommandLine.Commands;
using Xunit;

namespace Squirrel.CommandLine.Tests.Commands
{
    public abstract class GitHubCommandTests<T> : BaseCommandTests<T>
        where T : GitHubBaseCommand, new()
    {
        [Fact]
        public void RepoUrl_WithUrl_ParsesValue()
        {
            GitHubBaseCommand command = new T();

            ParseResult parseResult = command.ParseAndApply($"--repoUrl \"http://clowd.squirrel.com\"");

            Assert.Empty(parseResult.Errors);
            Assert.Equal("http://clowd.squirrel.com/", command.RepoUrl);
        }

        [Fact]
        public void RepoUrl_WithNonHttpValue_ShowsError()
        {
            GitHubBaseCommand command = new T();

            ParseResult parseResult = command.ParseAndApply($"--repoUrl \"file://clowd.squirrel.com\"");

            Assert.Equal(1, parseResult.Errors.Count);
            //Assert.Equal(command.RepoUrl, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("--repoUrl must contain a Uri with one of the following schems: http, https.", parseResult.Errors[0].Message);
        }

        [Fact]
        public void RepoUrl_WithRelativeUrl_ShowsError()
        {
            GitHubBaseCommand command = new T();

            ParseResult parseResult = command.ParseAndApply($"--repoUrl \"clowd.squirrel.com\"");

            Assert.Equal(1, parseResult.Errors.Count);
            //Assert.Equal(command.RepoUrl, parseResult.Errors[0].SymbolResult?.Symbol);
            Assert.StartsWith("--repoUrl must contain an absolute Uri.", parseResult.Errors[0].Message);
        }

        [Fact]
        public void Token_WithValue_ParsesValue()
        {
            GitHubBaseCommand command = new T();

            string cli = GetRequiredDefaultOptions() + $"--token \"abc\"";
            ParseResult parseResult = command.ParseAndApply(cli);

            Assert.Equal("abc", command.Token);
        }

        protected override string GetRequiredDefaultOptions()
        {
            return $"--repoUrl \"https://clowd.squirrel.com\" ";
        }
    }

    public class GitHubDownloadCommandTests : GitHubCommandTests<GitHubDownloadCommand>
    {
        [Fact]
        public void Pre_BareOption_SetsFlag()
        {
            var command = new GitHubDownloadCommand();

            string cli = GetRequiredDefaultOptions() + "--pre";
            ParseResult parseResult = command.ParseAndApply(cli);

            Assert.True(command.Pre);
        }
    }

    public class GitHubUploadCommandTests : GitHubCommandTests<GitHubUploadCommand>
    {
        public override bool ShouldBeNonEmptyReleaseDir => true;

        [Fact]
        public void Publish_BareOption_SetsFlag()
        {
            var command = new GitHubUploadCommand();

            string cli = GetRequiredDefaultOptions() + "--publish";
            ParseResult parseResult = command.ParseAndApply(cli);

            Assert.True(command.Publish);
        }

        [Fact]
        public void ReleaseName_WithName_ParsesValue()
        {
            var command = new GitHubUploadCommand();

            string cli = GetRequiredDefaultOptions() + $"--releaseName \"my release\"";
            ParseResult parseResult = command.ParseAndApply(cli);

            Assert.Equal("my release", command.ReleaseName);
        }
    }
}
