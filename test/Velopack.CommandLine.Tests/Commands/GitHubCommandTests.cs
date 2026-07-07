using System.CommandLine;
using Velopack.Deployment;
using Velopack.Vpk;
using Velopack.Vpk.Commands.Deployment;

namespace Velopack.CommandLine.Tests.Commands;

public abstract class GitHubCommandTests<T> : BaseCommandTests<T>
    where T : GitHubBaseCommand, new()
{
    [Fact]
    public void RepoUrl_WithUrl_ParsesValue()
    {
        GitHubBaseCommand command = new T();

        ParseResult parseResult = command.ParseAndApply($"--repoUrl \"http://clowd.squirrel.com\"");

        Assert.Empty(parseResult.Errors);
        Assert.Equal("http://clowd.squirrel.com", command.RepoUrl);
    }

    [Fact]
    public void RepoUrl_WithNonHttpValue_FailsValidation()
    {
        GitHubBaseCommand command = new T();
        command.ParseAndApply($"--repoUrl \"file://clowd.squirrel.com\"");
        var options = OptionMapper.Map<GitHubDownloadOptions>(command);

        var result = new GitHubDownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("repoUrl must contain an absolute http / https Uri"));
    }

    [Fact]
    public void RepoUrl_WithRelativeUrl_FailsValidation()
    {
        GitHubBaseCommand command = new T();
        command.ParseAndApply($"--repoUrl \"clowd.squirrel.com\"");
        var options = OptionMapper.Map<GitHubDownloadOptions>(command);

        var result = new GitHubDownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("repoUrl must contain an absolute http / https Uri"));
    }

    [Fact]
    public void RepoUrl_Missing_FailsValidation()
    {
        GitHubBaseCommand command = new T();
        ParseResult parseResult = command.ParseAndApply("");

        Assert.Empty(parseResult.Errors);

        var options = OptionMapper.Map<GitHubDownloadOptions>(command);
        var result = new GitHubDownloadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.PropertyName == "RepoUrl");
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

        Assert.True(command.Prerelease);
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

    [Fact]
    public void Tag_WithTag_ParsesValue()
    {
        var command = new GitHubUploadCommand();

        string cli = GetRequiredDefaultOptions() + $"--tag \"v1.2.3\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal("v1.2.3", command.TagName);
    }

    [Fact]
    public void TargetCommitish_WithTargetCommitish_ParsesValue()
    {
        var command = new GitHubUploadCommand();

        string cli = GetRequiredDefaultOptions() + $"--targetCommitish \"main\"";
        ParseResult parseResult = command.ParseAndApply(cli);

        Assert.Equal("main", command.TargetCommitish);
    }

    [Fact]
    public void ReleaseDir_Empty_FailsValidation()
    {
        var emptyDir = CreateTempDirectory();
        var command = new GitHubUploadCommand();
        command.ParseAndApply(GetRequiredDefaultOptions() + $"--outputDir \"{emptyDir.FullName}\"");
        var options = OptionMapper.Map<GitHubUploadOptions>(command);

        var result = new GitHubUploadOptionsValidator().Validate(options);

        Assert.Contains(result.Errors, e => e.ErrorMessage.Contains("releaseDir must be a non-empty directory"));
    }
}
