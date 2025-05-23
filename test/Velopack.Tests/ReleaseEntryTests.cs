﻿#pragma warning disable CS0618 // Type or member is obsolete
using System.Text;
using NuGet.Versioning;
using Velopack.Core;
using OldReleaseEntry = Velopack.Tests.OldSquirrel.ReleaseEntry;
using OldSemanticVersion = Velopack.Tests.OldSquirrel.SemanticVersion;

namespace Velopack.Tests;

public class ReleaseEntryTests
{
    [Theory]
    [InlineData(@"MyCoolApp-1.0-full.nupkg", "MyCoolApp", "1.0", "")]
    [InlineData(@"MyCoolApp-1.0.0-full.nupkg", "MyCoolApp", "1.0.0", "")]
    [InlineData(@"MyCoolApp-1.0.0-delta.nupkg", "MyCoolApp", "1.0.0", "")]
    [InlineData(@"MyCoolApp-1.0.0-win-x64-full.nupkg", "MyCoolApp", "1.0.0", "win-x64")]
    [InlineData(@"MyCoolApp-123.456.789-win-x64-full.nupkg", "MyCoolApp", "123.456.789", "win-x64")]
    [InlineData(@"MyCoolApp-123.456.789-hello-win-x64-full.nupkg", "MyCoolApp", "123.456.789", "hello-win-x64")]
    public void NewEntryCanRoundTripToOldSquirrel(string fileName, string id, string version, string metadata)
    {
        var size = 80396;
        var sha = "14db31d2647c6d2284882a2e101924a9c409ee67";
        var re = new ReleaseEntry(sha, fileName, size, null, null, null);
        StringBuilder file = new StringBuilder();
        file.AppendLine(re.EntryAsString);

        var parsed = OldReleaseEntry.ParseReleaseFile(file.ToString());
        Assert.True(parsed.Count() == 1);

        var oldEntry = parsed.First();

        Assert.Equal(fileName, oldEntry.Filename);
        Assert.Equal(id, oldEntry.PackageName);
        Assert.Equal(size, oldEntry.Filesize);
        Assert.Equal(sha, oldEntry.SHA1);
        Assert.Null(oldEntry.BaseUrl);
        Assert.Null(oldEntry.Query);
        Assert.True(oldEntry.Version.Version == OldSemanticVersion.Parse(version).Version);
        Assert.Equal(oldEntry.Version.SpecialVersion, metadata);

    }

    [Theory]
    [InlineData(@"94689fede03fed7ab59c24337673a27837f0c3ec MyCoolApp-1.0.nupkg 1004502", "MyCoolApp-1.0.nupkg", 1004502, null, null)]
    [InlineData(@"3a2eadd15dd984e4559f2b4d790ec8badaeb6a39   MyCoolApp-1.1.nupkg   1040561", "MyCoolApp-1.1.nupkg", 1040561, null, null)]
    [InlineData(@"14db31d2647c6d2284882a2e101924a9c409ee67  MyCoolApp-1.1.nupkg.delta  80396", "MyCoolApp-1.1.nupkg.delta", 80396, null, null)]
    [InlineData(@"0000000000000000000000000000000000000000  http://test.org/Folder/MyCoolApp-1.2.nupkg  2569", "MyCoolApp-1.2.nupkg", 2569, "http://test.org/Folder/", null)]
    [InlineData(@"0000000000000000000000000000000000000000  http://test.org/Folder/MyCoolApp-1.2.nupkg?query=param  2569", "MyCoolApp-1.2.nupkg", 2569, "http://test.org/Folder/", "?query=param")]
    [InlineData(@"0000000000000000000000000000000000000000  https://www.test.org/Folder/MyCoolApp-1.2-delta.nupkg  1231953", "MyCoolApp-1.2-delta.nupkg", 1231953, "https://www.test.org/Folder/", null)]
    [InlineData(@"0000000000000000000000000000000000000000  https://www.test.org/Folder/MyCoolApp-1.2-delta.nupkg?query=param  1231953", "MyCoolApp-1.2-delta.nupkg", 1231953, "https://www.test.org/Folder/", "?query=param")]
    public void ParseValidReleaseEntryLines(string releaseEntry, string fileName, long fileSize, string baseUrl, string query)
    {
        var fixture = ReleaseEntry.ParseReleaseEntry(releaseEntry);

        Assert.Equal(fileName, fixture.OriginalFilename);
        Assert.Equal(fileSize, fixture.Filesize);
        Assert.Equal(baseUrl, fixture.BaseUrl);
        Assert.Equal(query, fixture.Query);

        var old = OldReleaseEntry.ParseReleaseEntry(releaseEntry);
        Assert.Equal(fileName, old.Filename);
        Assert.Equal(fileSize, old.Filesize);
        Assert.Equal(baseUrl, old.BaseUrl);
        Assert.Equal(query, old.Query);
    }

    [Theory]
    [InlineData(@"94689fede03fed7ab59c24337673a27837f0c3ec My.Cool.App-1.0-full.nupkg 1004502", "My.Cool.App")]
    [InlineData(@"94689fede03fed7ab59c24337673a27837f0c3ec   My.Cool.App-1.1.nupkg 1004502", "My.Cool.App")]
    [InlineData(@"94689fede03fed7ab59c24337673a27837f0c3ec  http://test.org/Folder/My.Cool.App-1.2.nupkg?query=param     1231953", "My.Cool.App")]
    public void ParseValidReleaseEntryLinesWithDots(string releaseEntry, string packageName)
    {
        var fixture = ReleaseEntry.ParseReleaseEntry(releaseEntry);
        Assert.Equal(packageName, fixture.PackageId);
    }

    [Theory]
    [InlineData(@"94689fede03fed7ab59c24337673a27837f0c3ec My-Cool-App-1.0-full.nupkg 1004502", "My-Cool-App")]
    [InlineData(@"94689fede03fed7ab59c24337673a27837f0c3ec   My.Cool-App-1.1.nupkg 1004502", "My.Cool-App")]
    [InlineData(@"94689fede03fed7ab59c24337673a27837f0c3ec  http://test.org/Folder/My.Cool-App-1.2.nupkg?query=param     1231953", "My.Cool-App")]
    public void ParseValidReleaseEntryLinesWithDashes(string releaseEntry, string packageName)
    {
        var fixture = ReleaseEntry.ParseReleaseEntry(releaseEntry);
        Assert.Equal(packageName, fixture.PackageId);
    }

    [Theory]
    [InlineData(@"0000000000000000000000000000000000000000  file:/C/Folder/MyCoolApp-0.0.nupkg  0")]
    [InlineData(@"0000000000000000000000000000000000000000  C:\Folder\MyCoolApp-0.0.nupkg  0")]
    [InlineData(@"0000000000000000000000000000000000000000  ..\OtherFolder\MyCoolApp-0.0.nupkg  0")]
    [InlineData(@"0000000000000000000000000000000000000000  ../OtherFolder/MyCoolApp-0.0.nupkg  0")]
    [InlineData(@"0000000000000000000000000000000000000000  \\Somewhere\NetworkShare\MyCoolApp-0.0.nupkg.delta  0")]
    public void ParseThrowsWhenInvalidReleaseEntryLines(string releaseEntry)
    {
        Assert.Throws<Exception>(() => ReleaseEntry.ParseReleaseEntry(releaseEntry));
    }

    [Theory]
    [InlineData(@"0000000000000000000000000000000000000000 file.nupkg 0")]
    [InlineData(@"0000000000000000000000000000000000000000 http://path/file.nupkg 0")]
    public void EntryAsStringMatchesParsedInput(string releaseEntry)
    {
        var fixture = ReleaseEntry.ParseReleaseEntry(releaseEntry);
        Assert.Equal(releaseEntry, fixture.EntryAsString);
    }

    [Theory]
    [InlineData("Squirrel.Core.1.0.0.0.nupkg", 4457, "75255cfd229a1ed1447abe1104f5635e69975d30")]
    [InlineData("Squirrel.Core.1.1.0.0.nupkg", 15830, "9baf1dbacb09940086c8c62d9a9dbe69fe1f7593")]
    public void GenerateFromFileTest(string name, long size, string sha1)
    {
        var path = PathHelper.GetFixture(name);

        using (var f = File.OpenRead(path)) {
            var fixture = ReleaseEntry.GenerateFromFile(f, "dontcare");
            Assert.Equal(size, fixture.Filesize);
            Assert.Equal(sha1, fixture.SHA1.ToLowerInvariant());
        }
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2.nupkg                  123", 1, 2, 0, 0, "", false)]
    [InlineData("1000000000000000000000000000000000000000  MyCoolApp-1.2-full.nupkg             123", 1, 2, 0, 0, "", false)]
    [InlineData("2000000000000000000000000000000000000000  MyCoolApp-1.2-delta.nupkg            123", 1, 2, 0, 0, "", true)]
    [InlineData("3000000000000000000000000000000000000000  MyCoolApp-1.2-beta1.nupkg            123", 1, 2, 0, 0, "beta1", false)]
    [InlineData("4000000000000000000000000000000000000000  MyCoolApp-1.2-beta1-full.nupkg       123", 1, 2, 0, 0, "beta1", false)]
    [InlineData("5000000000000000000000000000000000000000  MyCoolApp-1.2-beta1-delta.nupkg      123", 1, 2, 0, 0, "beta1", true)]
    [InlineData("6000000000000000000000000000000000000000  MyCoolApp-1.2.3.nupkg                123", 1, 2, 3, 0, "", false)]
    [InlineData("7000000000000000000000000000000000000000  MyCoolApp-1.2.3-full.nupkg           123", 1, 2, 3, 0, "", false)]
    [InlineData("8000000000000000000000000000000000000000  MyCoolApp-1.2.3-delta.nupkg          123", 1, 2, 3, 0, "", true)]
    [InlineData("9000000000000000000000000000000000000000  MyCoolApp-1.2.3-beta1.nupkg          123", 1, 2, 3, 0, "beta1", false)]
    [InlineData("0100000000000000000000000000000000000000  MyCoolApp-1.2.3-beta1-full.nupkg     123", 1, 2, 3, 0, "beta1", false)]
    [InlineData("0200000000000000000000000000000000000000  MyCoolApp-1.2.3-beta1-delta.nupkg    123", 1, 2, 3, 0, "beta1", true)]
    [InlineData("0300000000000000000000000000000000000000  MyCoolApp-1.2.3.4.nupkg              123", 1, 2, 3, 4, "", false)]
    [InlineData("0400000000000000000000000000000000000000  MyCoolApp-1.2.3.4-full.nupkg         123", 1, 2, 3, 4, "", false)]
    [InlineData("0500000000000000000000000000000000000000  MyCoolApp-1.2.3.4-delta.nupkg        123", 1, 2, 3, 4, "", true)]
    [InlineData("0600000000000000000000000000000000000000  MyCoolApp-1.2.3.4-beta1.nupkg        123", 1, 2, 3, 4, "beta1", false)]
    [InlineData("0700000000000000000000000000000000000000  MyCoolApp-1.2.3.4-beta1-full.nupkg   123", 1, 2, 3, 4, "beta1", false)]
    [InlineData("0800000000000000000000000000000000000000  MyCoolApp-1.2.3.4-beta1-delta.nupkg  123", 1, 2, 3, 4, "beta1", true)]

    public void ParseVersionTest(string releaseEntry, int major, int minor, int patch, int revision, string prerelease, bool isDelta)
    {
        var fixture = ReleaseEntry.ParseReleaseEntry(releaseEntry);

        Assert.Equal(new NuGetVersion(major, minor, patch, revision, prerelease, null), fixture.Version);
        Assert.Equal(isDelta, fixture.IsDelta);

        var old = OldReleaseEntry.ParseReleaseEntry(releaseEntry);
        Assert.Equal(new NuGetVersion(major, minor, patch, revision, prerelease, null), new NuGetVersion(old.Version.ToString()));
        Assert.Equal(isDelta, old.IsDelta);
    } 

    [Theory]
    [InlineData("0000000000000000000000000000000000000000  MyCool-App-1.2.nupkg                  123", "MyCool-App")]
    [InlineData("0000000000000000000000000000000000000000  MyCool_App-1.2-full.nupkg             123", "MyCool_App")]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2-delta.nupkg            123", "MyCoolApp")]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2-beta1.nupkg            123", "MyCoolApp")]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2-beta1-full.nupkg       123", "MyCoolApp")]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2-beta1-delta.nupkg      123", "MyCoolApp")]
    [InlineData("0000000000000000000000000000000000000000  MyCool-App-1.2.3.nupkg                123", "MyCool-App")]
    [InlineData("0000000000000000000000000000000000000000  MyCool_App-1.2.3-full.nupkg           123", "MyCool_App")]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2.3-delta.nupkg          123", "MyCoolApp")]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2.3-beta1.nupkg          123", "MyCoolApp")]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2.3-beta1-full.nupkg     123", "MyCoolApp")]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2.3-beta1-delta.nupkg    123", "MyCoolApp")]
    [InlineData("0000000000000000000000000000000000000000  MyCool-App-1.2.3.4.nupkg              123", "MyCool-App")]
    [InlineData("0000000000000000000000000000000000000000  MyCool_App-1.2.3.4-full.nupkg         123", "MyCool_App")]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2.3.4-delta.nupkg        123", "MyCoolApp")]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2.3.4-beta1.nupkg        123", "MyCoolApp")]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2.3.4-beta1-full.nupkg   123", "MyCoolApp")]
    [InlineData("0000000000000000000000000000000000000000  MyCool-App-1.2.3.4-beta1-delta.nupkg  123", "MyCool-App")]
    public void CheckPackageName(string releaseEntry, string expected)
    {
        var fixture = ReleaseEntry.ParseReleaseEntry(releaseEntry);
        Assert.Equal(expected, fixture.PackageId);

        var old = OldReleaseEntry.ParseReleaseEntry(releaseEntry);
        Assert.Equal(expected, old.PackageName);
    }

    [Theory]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2.nupkg                  123 # 10%", 1, 2, 0, 0, "", "", false, 0.1f)]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2-full.nupkg             123 # 90%", 1, 2, 0, 0, "", "", false, 0.9f)]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2-delta.nupkg            123", 1, 2, 0, 0, "", "", true, null)]
    [InlineData("0000000000000000000000000000000000000000  MyCoolApp-1.2-delta.nupkg            123 # 5%", 1, 2, 0, 0, "", "", true, 0.05f)]
    public void ParseStagingPercentageTest(string releaseEntry, int major, int minor, int patch, int revision, string prerelease, string rid, bool isDelta, float? stagingPercentage)
    {
        var fixture = ReleaseEntry.ParseReleaseEntry(releaseEntry);

        Assert.Equal(new NuGetVersion(major, minor, patch, revision, prerelease, null), fixture.Version);
        Assert.Equal(isDelta, fixture.IsDelta);

        if (stagingPercentage.HasValue) {
            Assert.True(Math.Abs(fixture.StagingPercentage.Value - stagingPercentage.Value) < 0.001);
        } else {
            Assert.Null(fixture.StagingPercentage);
        }

        var old = OldReleaseEntry.ParseReleaseEntry(releaseEntry);
        var legacyPre = !String.IsNullOrEmpty(prerelease) && !String.IsNullOrEmpty(rid) ? $"{prerelease}-{rid}" : String.IsNullOrEmpty(prerelease) ? rid : prerelease;
        Assert.Equal(new NuGetVersion(major, minor, patch, revision, legacyPre, null), new NuGetVersion(old.Version.ToString()));
        Assert.Equal(isDelta, old.IsDelta);

        if (stagingPercentage.HasValue) {
            Assert.True(Math.Abs(old.StagingPercentage.Value - stagingPercentage.Value) < 0.001);
        } else {
            Assert.Null(old.StagingPercentage);
        }
    }

    [Fact]
    public void CanParseGeneratedReleaseEntryAsString()
    {
        var path = PathHelper.GetFixture("Squirrel.Core.1.1.0.0.nupkg");
        var entryAsString = ReleaseEntry.GenerateFromFile(path).EntryAsString;
        ReleaseEntry.ParseReleaseEntry(entryAsString);
    }

    //[Fact]
    //public void GetLatestReleaseWithNullCollectionReturnsNull()
    //{
    //    Assert.Null(ReleasePackageBuilder.GetPreviousRelease(
    //        null, null, null, null));
    //}

    //[Fact]
    //public void GetLatestReleaseWithEmptyCollectionReturnsNull()
    //{
    //    Assert.Null(ReleasePackageBuilder.GetPreviousRelease(
    //        Enumerable.Empty<ReleaseEntry>(), null, null, null));
    //}

    //[Fact]
    //public void WhenCurrentReleaseMatchesLastReleaseReturnNull()
    //{
    //    var package = new ReleasePackageBuilder("Espera-1.7.6-beta.nupkg");

    //    var releaseEntries = new[] {
    //        ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.6-beta.nupkg"))
    //    };
    //    Assert.Null(ReleasePackageBuilder.GetPreviousRelease(
    //        releaseEntries, package, @"C:\temp\somefolder", null));
    //}

    //[Fact]
    //public void WhenMultipleReleaseMatchesReturnEarlierResult()
    //{
    //    var expected = SemanticVersion.Parse("1.7.5-beta");
    //    var package = new ReleasePackageBuilder("Espera-1.7.6-beta.nupkg");

    //    var releaseEntries = new[] {
    //        ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.6-beta.nupkg")),
    //        ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.5-beta.nupkg"))
    //    };

    //    var actual = ReleasePackageBuilder.GetPreviousRelease(
    //        releaseEntries,
    //        package,
    //        @"C:\temp\", null);

    //    Assert.Equal(expected, actual.Version);
    //}

    //[Fact]
    //public void WhenMultipleReleasesFoundReturnPreviousVersion()
    //{
    //    var expected = SemanticVersion.Parse("1.7.6-beta");
    //    var input = new ReleasePackageBuilder("Espera-1.7.7-beta.nupkg");

    //    var releaseEntries = new[] {
    //        ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.6-beta.nupkg")),
    //        ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.5-beta.nupkg"))
    //    };

    //    var actual = ReleasePackageBuilder.GetPreviousRelease(
    //        releaseEntries,
    //        input,
    //        @"C:\temp\", null);

    //    Assert.Equal(expected, actual.Version);
    //}

    //[Fact]
    //public void WhenMultipleReleasesFoundInOtherOrderReturnPreviousVersion()
    //{
    //    var expected = SemanticVersion.Parse("1.7.6-beta");
    //    var input = new ReleasePackageBuilder("Espera-1.7.7-beta.nupkg");

    //    var releaseEntries = new[] {
    //        ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.5-beta.nupkg")),
    //        ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.7.6-beta.nupkg"))
    //    };

    //    var actual = ReleasePackageBuilder.GetPreviousRelease(
    //        releaseEntries,
    //        input,
    //        @"C:\temp\", null);

    //    Assert.Equal(expected, actual.Version);
    //}

    [Fact]
    public void WhenReleasesAreOutOfOrderSortByVersion()
    {
        var path = Path.GetTempFileName();
        var firstVersion = SemanticVersion.Parse("1.0.0");
        var secondVersion = SemanticVersion.Parse("1.1.0");
        var thirdVersion = SemanticVersion.Parse("1.2.0");

        var releaseEntries = new[] {
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.2.0-delta.nupkg")),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.1.0-delta.nupkg")),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.1.0-full.nupkg")),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.2.0-full.nupkg")),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.0.0-full.nupkg"))
        };

        ReleaseEntry.WriteReleaseFile(releaseEntries, path);

        var releases = ReleaseEntry.ParseReleaseFile(File.ReadAllText(path)).ToArray();

        Assert.Equal(firstVersion, releases[0].Version);
        Assert.Equal(secondVersion, releases[1].Version);
        Assert.Equal(true, releases[1].IsDelta);
        Assert.Equal(secondVersion, releases[2].Version);
        Assert.Equal(false, releases[2].IsDelta);
        Assert.Equal(thirdVersion, releases[3].Version);
        Assert.Equal(true, releases[3].IsDelta);
        Assert.Equal(thirdVersion, releases[4].Version);
        Assert.Equal(false, releases[4].IsDelta);
    }

    [Fact]
    public void WhenPreReleasesAreOutOfOrderSortByNumericSuffixSemVer2()
    {
        var path = Path.GetTempFileName();
        var firstVersion = SemanticVersion.Parse("1.1.9-beta.105");
        var secondVersion = SemanticVersion.Parse("1.2.0-beta.9");
        var thirdVersion = SemanticVersion.Parse("1.2.0-beta.10");
        var fourthVersion = SemanticVersion.Parse("1.2.0-beta.100");

        var releaseEntries = new[] {
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.2.0-beta.1-full.nupkg")),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.2.0-beta.9-full.nupkg")),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.2.0-beta.100-full.nupkg")),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.1.9-beta.105-full.nupkg")),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.2.0-beta.10-full.nupkg"))
        };

        ReleaseEntry.WriteReleaseFile(releaseEntries, path);

        var releases = ReleaseEntry.ParseReleaseFile(File.ReadAllText(path)).ToArray();

        Assert.Equal(firstVersion, releases[0].Version);
        Assert.Equal(secondVersion, releases[2].Version);
        Assert.Equal(thirdVersion, releases[3].Version);
        Assert.Equal(fourthVersion, releases[4].Version);
    }

    [Fact]
    public void StagingUsersGetBetaSoftware()
    {
        // NB: We're kind of using a hack here, in that we know that the 
        // last 4 bytes are used as the percentage, and the percentage 
        // effectively measures, "How close are you to zero". Guid.Empty
        // is v close to zero, because it is zero.
        var path = Path.GetTempFileName();
        var ourGuid = Guid.Empty;

        var releaseEntries = new[] {
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.2.0-full.nupkg", 0.1f)),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.1.0-full.nupkg")),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.0.0-full.nupkg"))
        };

        ReleaseEntry.WriteReleaseFile(releaseEntries, path);

        var releases = ReleaseEntry.ParseReleaseFileAndApplyStaging(File.ReadAllText(path), ourGuid).ToArray();
        Assert.Equal(3, releases.Length);
    }

    [Fact]
    public void BorkedUsersGetProductionSoftware()
    {
        var path = Path.GetTempFileName();
        var ourGuid = default(Guid?);

        var releaseEntries = new[] {
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.2.0-full.nupkg", 0.1f)),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.1.0-full.nupkg")),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.0.0-full.nupkg"))
        };

        ReleaseEntry.WriteReleaseFile(releaseEntries, path);

        var releases = ReleaseEntry.ParseReleaseFileAndApplyStaging(File.ReadAllText(path), ourGuid).ToArray();
        Assert.Equal(2, releases.Length);
    }

    [Theory]
    [InlineData("{22b29e6f-bd2e-43d2-85ca-ffffffffffff}")]
    [InlineData("{22b29e6f-bd2e-43d2-85ca-888888888888}")]
    [InlineData("{22b29e6f-bd2e-43d2-85ca-444444444444}")]
    public void UnluckyUsersGetProductionSoftware(string inputGuid)
    {
        var path = Path.GetTempFileName();
        var ourGuid = Guid.ParseExact(inputGuid, "B");

        var releaseEntries = new[] {
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.2.0-full.nupkg", 0.1f)),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.1.0-full.nupkg")),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.0.0-full.nupkg"))
        };

        ReleaseEntry.WriteReleaseFile(releaseEntries, path);

        var releases = ReleaseEntry.ParseReleaseFileAndApplyStaging(File.ReadAllText(path), ourGuid).ToArray();
        Assert.Equal(2, releases.Length);
    }

    [Theory]
    [InlineData("{22b29e6f-bd2e-43d2-85ca-333333333333}")]
    [InlineData("{22b29e6f-bd2e-43d2-85ca-111111111111}")]
    [InlineData("{22b29e6f-bd2e-43d2-85ca-000000000000}")]
    public void LuckyUsersGetBetaSoftware(string inputGuid)
    {
        var path = Path.GetTempFileName();
        var ourGuid = Guid.ParseExact(inputGuid, "B");

        var releaseEntries = new[] {
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.2.0-full.nupkg", 0.25f)),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.1.0-full.nupkg")),
            ReleaseEntry.ParseReleaseEntry(MockReleaseEntry("Espera-1.0.0-full.nupkg"))
        };

        ReleaseEntry.WriteReleaseFile(releaseEntries, path);

        var releases = ReleaseEntry.ParseReleaseFileAndApplyStaging(File.ReadAllText(path), ourGuid).ToArray();
        Assert.Equal(3, releases.Length);
    }

    [Fact]
    public void ParseReleaseFileShouldReturnNothingForBlankFiles()
    {
        Assert.True(ReleaseEntry.ParseReleaseFile("").Count() == 0);
        Assert.True(ReleaseEntry.ParseReleaseFile(null).Count() == 0);
    }

    //        [Fact]
    //        public void FindCurrentVersionWithExactRidMatch()
    //        {
    //            string _ridReleaseEntries = """
    //0000000000000000000000000000000000000000  MyApp-1.3-win-x86.nupkg  123
    //0000000000000000000000000000000000000000  MyApp-1.4.nupkg  123
    //0000000000000000000000000000000000000000  MyApp-1.4-win-x64.nupkg  123
    //0000000000000000000000000000000000000000  MyApp-1.4-win-x86.nupkg  123
    //0000000000000000000000000000000000000000  MyApp-1.4-osx-x86.nupkg  123
    //""";

    //            var entries = ReleaseEntry.ParseReleaseFile(_ridReleaseEntries);

    //            var e = Utility.FindLatestFullVersion(entries, RID.Parse("win-x86"));
    //            Assert.Equal("MyApp-1.4-win-x86.nupkg", e.OriginalFilename);
    //        }

    //        [Fact]
    //        public void FindCurrentVersionWithExactRidMatchNotLatest()
    //        {
    //            string _ridReleaseEntries = """
    //0000000000000000000000000000000000000000  MyApp-1.3-win-x86.nupkg  123
    //0000000000000000000000000000000000000000  MyApp-1.4.nupkg  123
    //0000000000000000000000000000000000000000  MyApp-1.4-win-x64.nupkg  123
    //0000000000000000000000000000000000000000  MyApp-1.4-win.nupkg  123
    //0000000000000000000000000000000000000000  MyApp-1.4-osx-x86.nupkg  123
    //""";

    //            var entries = ReleaseEntry.ParseReleaseFile(_ridReleaseEntries);

    //            var e = Utility.FindLatestFullVersion(entries, RID.Parse("win-x86"));
    //            Assert.Equal("MyApp-1.3-win.nupkg", e.OriginalFilename);
    //        }

    //        [Fact]
    //        public void FindCurrentVersionWithExactRidMatchOnlyArchitecture()
    //        {
    //            string _ridReleaseEntries = """
    //0000000000000000000000000000000000000000  MyApp-1.3-win-x86.nupkg  123
    //0000000000000000000000000000000000000000  MyApp-1.4.nupkg  123
    //0000000000000000000000000000000000000000  MyApp-1.4-win-x64.nupkg  123
    //0000000000000000000000000000000000000000  MyApp-1.4-win.nupkg  123
    //0000000000000000000000000000000000000000  MyApp-1.4-osx-x86.nupkg  123
    //""";

    //            var entries = ReleaseEntry.ParseReleaseFile(_ridReleaseEntries);

    //            var e = Utility.FindLatestFullVersion(entries, RID.Parse("win-x86"));
    //            Assert.Equal("MyApp-1.3-win.nupkg", e.OriginalFilename);
    //        }

    static string MockReleaseEntry(string name, float? percentage = null)
    {
        if (percentage.HasValue) {
            var ret = String.Format("94689fede03fed7ab59c24337673a27837f0c3ec  {0}  1004502 # {1:F0}%", name, percentage * 100.0f);
            return ret;
        } else {
            return String.Format("94689fede03fed7ab59c24337673a27837f0c3ec  {0}  1004502", name);
        }
    }
}
