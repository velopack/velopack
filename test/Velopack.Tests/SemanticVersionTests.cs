#nullable enable
using Velopack.NuGet;

namespace Velopack.Tests;

public class SemanticVersionTests
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
    
    // ── Parsing: valid inputs ──────────────────────────────────────────

    [Theory]
    [InlineData("1.0", 1, 0, 0, 0, "", "")]
    [InlineData("1.2", 1, 2, 0, 0, "", "")]
    [InlineData("1.2.3", 1, 2, 3, 0, "", "")]
    [InlineData("0.0.1", 0, 0, 1, 0, "", "")]
    [InlineData("100.200.300", 100, 200, 300, 0, "", "")]
    [InlineData("1.2.3.4", 1, 2, 3, 4, "", "")]
    [InlineData("1.2.3.0", 1, 2, 3, 0, "", "")]
    [InlineData("10.0.40219", 10, 0, 40219, 0, "", "")]
    public void ParsesVersionParts(string input, int major, int minor, int patch, int revision, string release, string metadata)
    {
        var v = SemanticVersion.Parse(input);
        Assert.Equal(major, v.Major);
        Assert.Equal(minor, v.Minor);
        Assert.Equal(patch, v.Patch);
        Assert.Equal(revision, v.Revision);
        Assert.Equal(release, v.Release);
        Assert.Equal(metadata, v.Metadata);
    }

    [Theory]
    [InlineData("1.2.3-alpha", "alpha")]
    [InlineData("1.2.3-beta.1", "beta.1")]
    [InlineData("1.2.3-rc.1.2", "rc.1.2")]
    [InlineData("1.2.3-alpha-beta", "alpha-beta")]
    [InlineData("1.2.3.4-beta1", "beta1")]
    [InlineData("0.0.1-beta01", "beta01")]
    public void ParsesPrerelease(string input, string expectedRelease)
    {
        var v = SemanticVersion.Parse(input);
        Assert.Equal(expectedRelease, v.Release);
        Assert.True(v.IsPrerelease);
    }

    [Theory]
    [InlineData("1.2.3+build123", "build123")]
    [InlineData("1.2.3-beta+meta", "meta")]
    [InlineData("1.2.3+sha.abc123", "sha.abc123")]
    public void ParsesMetadata(string input, string expectedMeta)
    {
        var v = SemanticVersion.Parse(input);
        Assert.Equal(expectedMeta, v.Metadata);
        Assert.True(v.HasMetadata);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.3+build")]
    public void StableVersionIsNotPrerelease(string input)
    {
        var v = SemanticVersion.Parse(input);
        Assert.False(v.IsPrerelease);
        Assert.Empty(v.Release);
    }

    [Theory]
    [InlineData("1.2.3")]
    [InlineData("1.2.3-beta")]
    public void NoMetadataWhenNotPresent(string input)
    {
        var v = SemanticVersion.Parse(input);
        Assert.False(v.HasMetadata);
        Assert.Empty(v.Metadata);
    }

    [Fact]
    public void ParseTrimsWhitespace()
    {
        var v = SemanticVersion.Parse("  1.2.3-beta+build  ");
        Assert.Equal(1, v.Major);
        Assert.Equal("beta", v.Release);
        Assert.Equal("build", v.Metadata);
    }

    // ── Parsing: invalid inputs ────────────────────────────────────────

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("1")]
    [InlineData("abc")]
    [InlineData("1.2.3.4.5")]
    [InlineData("1.2.3-")]
    [InlineData("-1.2.3")]
    [InlineData("1.-2.3")]
    [InlineData("1.2.-3")]
    [InlineData("a.b.c")]
    public void TryParseReturnsFalseForInvalid(string? input)
    {
        Assert.False(SemanticVersion.TryParse(input, out var result));
        Assert.Null(result);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-version")]
    [InlineData("1")]
    public void ParseThrowsForInvalid(string input)
    {
        Assert.Throws<ArgumentException>(() => SemanticVersion.Parse(input));
    }

    // ── Constructor ────────────────────────────────────────────────────

    [Fact]
    public void ConstructorSetsDefaults()
    {
        var v = new SemanticVersion(1, 2, 3);
        Assert.Equal(0, v.Revision);
        Assert.Equal("", v.Release);
        Assert.Equal("", v.Metadata);
        Assert.False(v.IsPrerelease);
        Assert.False(v.HasMetadata);
    }

    [Fact]
    public void ConstructorWithRevision()
    {
        var v = new SemanticVersion(1, 2, 3, 4, "beta", "build");
        Assert.Equal(1, v.Major);
        Assert.Equal(2, v.Minor);
        Assert.Equal(3, v.Patch);
        Assert.Equal(4, v.Revision);
        Assert.Equal("beta", v.Release);
        Assert.Equal("build", v.Metadata);
    }

    [Fact]
    public void ConstructorNullPrereleaseTreatedAsEmpty()
    {
        var v = new SemanticVersion(1, 0, 0, null!, null!);
        Assert.Equal("", v.Release);
        Assert.Equal("", v.Metadata);
    }

    [Fact]
    public void ConstructorRejectsNegativeValues()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticVersion(-1, 0, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticVersion(0, -1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticVersion(0, 0, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new SemanticVersion(0, 0, 0, -1));
    }

    // ── ReleaseLabels ──────────────────────────────────────────────────

    [Fact]
    public void ReleaseLabelsEmpty()
    {
        var v = new SemanticVersion(1, 0, 0);
        Assert.Empty(v.ReleaseLabels);
    }

    [Fact]
    public void ReleaseLabelsAreSplit()
    {
        var v = SemanticVersion.Parse("1.0.0-beta.1.rc");
        Assert.Equal(new[] { "beta", "1", "rc" }, v.ReleaseLabels);
    }

    // ── ToString / ToNormalizedString / ToFullString ────────────────────

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta", "1.2.3-beta")]
    [InlineData("1.2.3-beta+build", "1.2.3-beta")]
    [InlineData("1.0.0", "1.0.0")]
    public void ToStringMatchesNormalized(string input, string expected)
    {
        Assert.Equal(expected, SemanticVersion.Parse(input).ToString());
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta", "1.2.3-beta")]
    [InlineData("1.2.3-beta+build", "1.2.3-beta")]
    public void ToNormalizedStringExcludesMetadata(string input, string expected)
    {
        Assert.Equal(expected, SemanticVersion.Parse(input).ToNormalizedString());
    }

    [Theory]
    [InlineData("1.2.3", "1.2.3")]
    [InlineData("1.2.3-beta", "1.2.3-beta")]
    [InlineData("1.2.3-beta+build", "1.2.3-beta+build")]
    [InlineData("1.2.3+build", "1.2.3+build")]
    public void ToFullStringIncludesMetadata(string input, string expected)
    {
        Assert.Equal(expected, SemanticVersion.Parse(input).ToFullString());
    }

    [Fact]
    public void RevisionAppearsInStringsWhenNonZero()
    {
        var v = new SemanticVersion(1, 2, 3, 4, "beta", "build");
        Assert.Equal("1.2.3.4-beta", v.ToString());
        Assert.Equal("1.2.3.4-beta", v.ToNormalizedString());
        Assert.Equal("1.2.3.4-beta+build", v.ToFullString());
    }

    [Fact]
    public void RevisionOmittedInStringsWhenZero()
    {
        var v = new SemanticVersion(1, 2, 3, 0, "beta");
        Assert.Equal("1.2.3-beta", v.ToString());
    }

    [Fact]
    public void FourPartVersionRoundTrips()
    {
        var original = "1.2.3.4-beta+build";
        var v = SemanticVersion.Parse(original);
        Assert.Equal(original, v.ToFullString());
    }

    // ── Equality ───────────────────────────────────────────────────────

    [Fact]
    public void EqualVersionsAreEqual()
    {
        var a = SemanticVersion.Parse("1.2.3-beta+build1");
        var b = SemanticVersion.Parse("1.2.3-beta+build2");
        // metadata is ignored for equality
        Assert.Equal(a, b);
        Assert.True(a == b);
        Assert.False(a != b);
        Assert.True(a.Equals(b));
    }

    [Fact]
    public void DifferentVersionsAreNotEqual()
    {
        var a = SemanticVersion.Parse("1.2.3");
        var b = SemanticVersion.Parse("1.2.4");
        Assert.NotEqual(a, b);
        Assert.True(a != b);
        Assert.False(a == b);
    }

    [Fact]
    public void EqualityIsCaseInsensitiveOnPrerelease()
    {
        var a = SemanticVersion.Parse("1.0.0-BETA");
        var b = SemanticVersion.Parse("1.0.0-beta");
        Assert.Equal(a, b);
        Assert.True(a == b);
    }

    [Fact]
    public void RevisionAffectsEquality()
    {
        var a = new SemanticVersion(1, 2, 3, 0);
        var b = new SemanticVersion(1, 2, 3, 1);
        Assert.NotEqual(a, b);
    }

    [Fact]
    public void NullEquality()
    {
        var v = SemanticVersion.Parse("1.0.0");
        Assert.False(v.Equals(null));
        Assert.False(v == null);
        Assert.True(v != null);
        Assert.True((SemanticVersion?) null == null);
        Assert.False((SemanticVersion?) null != null);
    }

    // ── GetHashCode ────────────────────────────────────────────────────

    [Fact]
    public void EqualVersionsHaveSameHashCode()
    {
        var a = SemanticVersion.Parse("1.2.3-beta");
        var b = SemanticVersion.Parse("1.2.3-BETA");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void MetadataDoesNotAffectHashCode()
    {
        var a = SemanticVersion.Parse("1.2.3-beta+aaa");
        var b = SemanticVersion.Parse("1.2.3-beta+zzz");
        Assert.Equal(a.GetHashCode(), b.GetHashCode());
    }

    [Fact]
    public void DifferentVersionsLikelyHaveDifferentHashCodes()
    {
        var a = SemanticVersion.Parse("1.2.3");
        var b = SemanticVersion.Parse("1.2.4");
        // not strictly required but verifies hash varies
        Assert.NotEqual(a.GetHashCode(), b.GetHashCode());
    }

    // ── Comparison / ordering ──────────────────────────────────────────

    [Theory]
    [InlineData("1.0.0", "2.0.0")]
    [InlineData("1.0.0", "1.1.0")]
    [InlineData("1.0.0", "1.0.1")]
    [InlineData("1.0.0.0", "1.0.0.1")]
    [InlineData("1.0.0-alpha", "1.0.0-beta")]
    [InlineData("1.0.0-alpha", "1.0.0")]          // prerelease < stable
    [InlineData("1.0.0-alpha.1", "1.0.0-alpha.2")]
    [InlineData("1.0.0-1", "1.0.0-2")]
    [InlineData("1.0.0-1", "1.0.0-alpha")]        // numeric < alpha
    [InlineData("1.0.0-alpha", "1.0.0-alpha.1")]  // fewer identifiers < more
    public void LessThanIsCorrect(string lower, string higher)
    {
        var a = SemanticVersion.Parse(lower);
        var b = SemanticVersion.Parse(higher);
        Assert.True(a < b, $"Expected {lower} < {higher}");
        Assert.True(b > a, $"Expected {higher} > {lower}");
        Assert.True(a.CompareTo(b) < 0);
        Assert.True(b.CompareTo(a) > 0);
    }

    [Fact]
    public void MetadataIgnoredInComparison()
    {
        var a = SemanticVersion.Parse("1.0.0+zzz");
        var b = SemanticVersion.Parse("1.0.0+aaa");
        Assert.True(a.CompareTo(b) == 0);
        Assert.True(a <= b);
        Assert.True(a >= b);
    }

    [Fact]
    public void CompareToNullReturnsPositive()
    {
        var v = SemanticVersion.Parse("1.0.0");
        Assert.True(v.CompareTo((SemanticVersion?) null) > 0);
        Assert.True(v.CompareTo((object?) null) > 0);
    }

    [Fact]
    public void CompareToNonVersionThrows()
    {
        var v = SemanticVersion.Parse("1.0.0");
        Assert.Throws<ArgumentException>(() => v.CompareTo("not a version"));
    }

    // ── Operators with nulls ───────────────────────────────────────────

    [Fact]
    public void NullOperators()
    {
        SemanticVersion? n = null;
        var v = SemanticVersion.Parse("1.0.0");

        Assert.True(n < v);
        Assert.False(n > v);
        Assert.True(n <= v);
        Assert.False(n >= v);

        Assert.False(v < n);
        Assert.True(v > n);
        Assert.False(v <= n);
        Assert.True(v >= n);

#pragma warning disable CS1718 // Comparison made to same variable — intentional null self-comparison test
        Assert.True(n <= n);
        Assert.True(n >= n);
#pragma warning restore CS1718
    }

    // ── Sorting ────────────────────────────────────────────────────────

    [Fact]
    public void SortingProducesCorrectOrder()
    {
        var versions = new[] {
            SemanticVersion.Parse("2.0.0"),
            SemanticVersion.Parse("1.0.0-alpha"),
            SemanticVersion.Parse("1.0.0"),
            SemanticVersion.Parse("1.0.0-beta"),
            SemanticVersion.Parse("1.0.1"),
            SemanticVersion.Parse("1.0.0-alpha.1"),
        };

        var sorted = versions.OrderBy(v => v).Select(v => v.ToString()).ToArray();

        Assert.Equal(new[] {
            "1.0.0-alpha",
            "1.0.0-alpha.1",
            "1.0.0-beta",
            "1.0.0",
            "1.0.1",
            "2.0.0",
        }, sorted);
    }

    // ── CompareByVersion ───────────────────────────────────────────────

    [Fact]
    public void CompareByVersionIgnoresPrerelease()
    {
        var a = SemanticVersion.Parse("1.2.3-alpha");
        var b = SemanticVersion.Parse("1.2.3-beta");
        Assert.Equal(0, SemanticVersion.CompareByVersion(a, b));
    }

    [Fact]
    public void CompareByVersionIncludesRevision()
    {
        var a = new SemanticVersion(1, 2, 3, 0);
        var b = new SemanticVersion(1, 2, 3, 1);
        Assert.True(SemanticVersion.CompareByVersion(a, b) < 0);
    }

    [Fact]
    public void CompareByVersionComparesNumerically()
    {
        var a = SemanticVersion.Parse("1.2.3");
        var b = SemanticVersion.Parse("1.2.4");
        Assert.True(SemanticVersion.CompareByVersion(a, b) < 0);
    }

    [Fact]
    public void CompareByVersionHandlesNulls()
    {
        var v = SemanticVersion.Parse("1.0.0");
        Assert.True(SemanticVersion.CompareByVersion(null, v) < 0);
        Assert.True(SemanticVersion.CompareByVersion(v, null) > 0);
        Assert.Equal(0, SemanticVersion.CompareByVersion(null, null));
    }

    // ── 4-part version specifics ───────────────────────────────────────

    [Fact]
    public void FourPartVersionComparesCorrectly()
    {
        var a = SemanticVersion.Parse("1.2.3.4");
        var b = SemanticVersion.Parse("1.2.3.5");
        Assert.True(a < b);

        var c = SemanticVersion.Parse("1.2.3");
        Assert.True(c < a); // 1.2.3.0 < 1.2.3.4
    }

    [Fact]
    public void FourPartVersionWithPrerelease()
    {
        var v = SemanticVersion.Parse("1.2.3.4-beta1");
        Assert.Equal(1, v.Major);
        Assert.Equal(2, v.Minor);
        Assert.Equal(3, v.Patch);
        Assert.Equal(4, v.Revision);
        Assert.Equal("beta1", v.Release);
        Assert.True(v.IsPrerelease);
    }

    [Fact]
    public void ThreePartRevisionDefaultsToZero()
    {
        var v = SemanticVersion.Parse("1.2.3");
        Assert.Equal(0, v.Revision);
    }

    [Fact]
    public void TwoPartDefaultsPatchAndRevisionToZero()
    {
        var v = SemanticVersion.Parse("1.2");
        Assert.Equal(0, v.Patch);
        Assert.Equal(0, v.Revision);
    }

    // ── Dictionary / HashSet usage ─────────────────────────────────────

    [Fact]
    public void WorksInDictionary()
    {
        var dict = new Dictionary<SemanticVersion, string>();
        var key = SemanticVersion.Parse("1.2.3-beta");
        dict[key] = "value";
        // lookup with case-different prerelease should find the same entry
        var lookup = SemanticVersion.Parse("1.2.3-BETA");
        Assert.True(dict.ContainsKey(lookup));
        Assert.Equal("value", dict[lookup]);
    }

    [Fact]
    public void WorksInHashSet()
    {
        var set = new HashSet<SemanticVersion> {
            SemanticVersion.Parse("1.2.3"),
            SemanticVersion.Parse("1.2.3+different-meta"),
        };
        // metadata ignored, so these are the same version
        Assert.Single(set);
    }
}
