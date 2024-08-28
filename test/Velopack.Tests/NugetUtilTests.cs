using NuGet.Versioning;
using Velopack.NuGet;

namespace Velopack.Tests;

public class NugetUtilTests
{
    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.3-alpha13")]
    [InlineData("1.2.3-alpha135")]
    [InlineData("0.0.1")]
    [InlineData("0.0.1-beta")]
    [InlineData("0.0.1-beta01")]
    [InlineData("1.299656.3-alpha")]
    public void SemanticVersionParsesValidVersion(string ver)
    {
        NugetUtil.ThrowIfVersionNotSemverCompliant(ver);
        Assert.True(SemanticVersion.TryParse(ver, out var _));
    }

    [Theory]
    [InlineData("")]
    [InlineData("1")]
    [InlineData("0")]
    [InlineData("1.2.3.4")]
    [InlineData("1.2.3.4-alpha")]
    [InlineData("0.0.0.0")]
    [InlineData("0.0.0")]
    [InlineData("0.0")]
    [InlineData("0.0.0-alpha")]
    public void SemanticVersionThrowsInvalidVersion(string ver)
    {
        Assert.ThrowsAny<Exception>(() => NugetUtil.ThrowIfVersionNotSemverCompliant(ver));
    }
}
