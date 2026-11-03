using Velopack.Sources;

namespace Velopack.Tests;

public class GithubSourceTests
{
    [Fact]
    public void Slug_ResolvesToSameRepoUriAsPlainUrl()
    {
        var expected = new GithubSource("https://github.com/myuser/myrepo", null, false);
        var actual = new GithubSource("myuser/myrepo", null, false);

        Assert.Equal(expected.RepoUri, actual.RepoUri);
    }
}
