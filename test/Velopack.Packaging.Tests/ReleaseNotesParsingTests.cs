using System.Security;
using System.Xml.Linq;
using NuGet.Packaging;
using Velopack.Core;
using Velopack.Packaging.Abstractions;
using Velopack.Packaging.Windows.Commands;
using Velopack.Util;
using Velopack.Vpk;
using Velopack.Vpk.Logging;

namespace Velopack.Packaging.Tests;

/// <summary>
/// Test-only implementation of PackageBuilder that exposes GenerateNuspecContent for testing.
/// </summary>
internal class TestPackageBuilder : PackageBuilder<IPackOptions>
{
    public TestPackageBuilder(ILogger logger)
        : base(RuntimeOs.Windows, logger, new BasicConsole(logger, new VelopackDefaults(false)))
    {
    }

    public string GenerateNuspecContentPublic() => GenerateNuspecContent();

    protected override string[] GetMainExeSearchPaths(string packDirectory, string mainExeName)
        => Array.Empty<string>();

    protected override Task<string> PreprocessPackDir(Action<int> progress, string packDir)
        => Task.FromResult(packDir);

    protected override Task CreatePortablePackage(Action<int> progress, string packDir, string outputPath)
        => Task.CompletedTask;
}

/// <summary>
/// Tests to validate that release notes are properly escaped and embedded in NuGet packages
/// regardless of their textual content. This addresses the fix for XML parsing issues where
/// special characters in release notes could break the package metadata.
/// 
/// These tests directly validate the actual PackageBuilder.GenerateNuspecContent() method
/// to ensure release notes are properly escaped regardless of textual content.
/// </summary>
public class ReleaseNotesParsingTests
{
    private readonly ITestOutputHelper _output;

    public ReleaseNotesParsingTests(ITestOutputHelper output)
    {
        _output = output;
    }

    /// <summary>
    /// Helper method to test release notes XML escaping using the actual PackageBuilder logic.
    /// </summary>
    private void ValidateReleaseNotesInNuspec(string releaseNotesContent)
    {
        using var logger = _output.BuildLoggerFor<ReleaseNotesParsingTests>();
        using var _1 = TempUtil.GetTempDirectory(out var tmpNotesDir);

        // Write release notes to a temp file (as PackageBuilder expects)
        var notesPath = Path.Combine(tmpNotesDir, "RELEASE_NOTES.md");
        File.WriteAllText(notesPath, releaseNotesContent);

        // Create a minimal PackageBuilder instance to test nuspec generation
        var builder = new TestPackageBuilder(logger);

        // Set up minimal required options
        var options = new WindowsPackOptions {
            ReleaseDir = new DirectoryInfo(tmpNotesDir),
            PackId = "TestApp",
            PackVersion = "1.0.0",
            EntryExecutableName = "test.exe",
            TargetRuntime = RID.Parse("win-x64"),
            ReleaseNotes = notesPath,
        };

        // Use reflection or expose method to call Run with options to set them
        typeof(PackageBuilder<IPackOptions>)
            .GetProperty("Options", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(builder, options);

        // Generate nuspec content using the actual PackageBuilder method
        var nuspecXml = builder.GenerateNuspecContentPublic();

        // Parse the XML to ensure it's valid
        XDocument xml;
        try {
            xml = XDocument.Parse(nuspecXml);
        } catch (Exception ex) {
            throw new Exception($"Failed to parse nuspec XML. This indicates the release notes broke the XML structure: {ex.Message}", ex);
        }

        // Validate that release notes are present and contain the expected content
        var metadata = xml.Root?.ElementsNoNamespace("metadata").FirstOrDefault();
        Assert.NotNull(metadata);

        var releaseNotesElement = metadata.ElementsNoNamespace("releaseNotes").FirstOrDefault();

        // Empty release notes are not added by PackageBuilder (check in addMetadata)
        if (string.IsNullOrEmpty(releaseNotesContent)) {
            Assert.Null(releaseNotesElement);
            return;
        }

        Assert.NotNull(releaseNotesElement);

        var extractedNotes = releaseNotesElement.Value;

        // Normalize line endings for comparison
        var normalizedExpected = releaseNotesContent.ReplaceLineEndings("\n").Trim();
        var normalizedActual = extractedNotes.ReplaceLineEndings("\n").Trim();

        Assert.Equal(normalizedExpected, normalizedActual);
    }

    [Fact]
    public void WithEmptyContent()
    {
        // Empty release notes should be handled gracefully
        // PackageBuilder skips empty metadata, ValidateReleaseNotesInNuspec will verify element is omitted
        ValidateReleaseNotesInNuspec("");
    }

    [Fact]
    public void DebugCDataSequence()
    {
        // Test specifically for ]]> preservation
        var notes = "Before]]>After";
        ValidateReleaseNotesInNuspec(notes);

        // If this test passes, ]]> is preserved exactly
        _output.WriteLine($"Test passed: ']]>' sequence is preserved correctly");
    }

    [Fact]
    public void WithVariousProblematicContent()
    {
        // Comprehensive test combining all edge cases that could break XML parsing:
        // - XML special characters: & < > " '
        // - Control characters: tabs, carriage returns
        // - Unicode: emojis, international characters, symbols
        // - CDATA-like content: <![CDATA[...]]> and ]]>
        // - XML comments and processing instructions
        var notes = @"# Release Notes & Updates 发布说明

## What's New?
- Feature A&B: Support for <configuration> files with ""quoted"" values
- Fixed: App crashes when path contains '<', '>', or '&' characters
- Updated: XML parser to handle <tag attr=""value&more"" />
- Unicode support: café, naïve, 日本語
- Emojis work too: 🎉 🚀 ✨
- Symbols: © ® ™ € £ ¥

## Code Examples
```xml
<root>
  <item value=""test & demo"" />
  <special>&lt;escaped&gt;</special>
</root>
```

## XML Edge Cases
This release handles XML correctly:
<![CDATA[
This looks like CDATA but should be escaped.
]]>

And also: ]]> when it appears standalone.

XML comment: <!-- comment -->
Processing instruction: <?xml version=""1.0""?>

## Control Characters
Line 1	Tabbed content
Line 2
Carriage return
Line 3

## Breaking Changes
- Old syntax: key=""value"" is deprecated
- New syntax: Use key='value' or escape with &amp;

For questions, contact support@example.com & check docs.";
        ValidateReleaseNotesInNuspec(notes);
    }

    [Fact]
    public void WithVeryLongContent()
    {
        // Generate a long release note with lots of special characters
        var lines = new List<string>();
        for (int i = 0; i < 100; i++) {
            lines.Add($"## Section {i}");
            lines.Add($"- Fix for issue #{i}: handling 'quotes' and \"double quotes\" & ampersands");
            lines.Add($"- Update <component{i}> with value={i}");
            lines.Add("");
        }
        var notes = string.Join(Environment.NewLine, lines);

        ValidateReleaseNotesInNuspec(notes);
    }

    [Fact]
    public void WithPreEscapedContent()
    {
        // Test that pre-escaped HTML entities are preserved correctly
        var notes = @"## Copyright Notice
Copyright &copy; 2026 My Company &trade;

## Special Characters
- Registered: &reg;
- Ampersand: &amp;
- Less than: &lt;
- Greater than: &gt;
- Quote: &quot;

These should remain as HTML entities, not be double-escaped.";

        ValidateReleaseNotesInNuspec(notes);
    }

    [Fact]
    public void WithCDataTerminator()
    {
        // Test that ]]> is properly handled (it terminates CDATA sections)
        var notes = @"## CDATA Edge Case

This content contains ]]> which would normally terminate a CDATA section.

Multiple cases: ]]> and then ]]> again.

Also test: ]] without > and > without ]].";

        ValidateReleaseNotesInNuspec(notes);
    }
}
