using Velopack.Packaging.Rtf;

namespace Velopack.Packaging.Tests;

public class RtfRendererTests
{
    private static string Escape(string input)
    {
        using var sw = new StringWriter();
        var renderer = new RtfRenderer(sw);
        renderer.WriteEscape(input);
        return sw.ToString();
    }

    private static string EscapeSpan(string input)
    {
        using var sw = new StringWriter();
        var renderer = new RtfRenderer(sw);
        renderer.WriteEscape(input.AsSpan());
        return sw.ToString();
    }

    private static string RenderMarkdown(string markdown)
    {
        using var sw = new StringWriter();
        var renderer = new RtfRenderer(sw);
        renderer.WriteRtfStart();
        _ = Markdig.Markdown.Convert(markdown, renderer);
        renderer.WriteRtfEnd();
        return sw.ToString();
    }

    [Fact]
    public void AsciiIsWrittenVerbatim()
    {
        Assert.Equal("Hello World 123", Escape("Hello World 123"));
        Assert.Equal("Hello World 123", EscapeSpan("Hello World 123"));
    }

    [Fact]
    public void RtfControlCharsAreEscaped()
    {
        Assert.Equal("\\\\ \\{ \\}", Escape("\\ { }"));
        Assert.Equal("\\\\ \\{ \\}", EscapeSpan("\\ { }"));
    }

    [Theory]
    [InlineData('最', 26368)]
    [InlineData('终', 32456)]
    [InlineData('é', 233)]
    public void BmpUnicodeUsesSingleQuestionMarkFallback(char c, int expectedCodepoint)
    {
        // RTF spec: each \uN escape must be followed by exactly one ANSI substitution char
        // (the document declares \uc1). The fallback must be a literal '?', NOT the raw
        // non-ASCII character, otherwise the char renders twice / leaves stray bytes.
        // Regression test for the Chinese license garbling (one '?' after every CJK char).
        var expected = $"\\u{expectedCodepoint}?";
        Assert.Equal(expected, Escape(c.ToString()));
        Assert.Equal(expected, EscapeSpan(c.ToString()));
    }

    [Fact]
    public void ChineseStringDoesNotLeakLiteralCharacters()
    {
        const string chinese = "最终用户许可协议";
        var stringResult = Escape(chinese);
        var spanResult = EscapeSpan(chinese);

        Assert.Equal(stringResult, spanResult);

        // each CJK codepoint must appear as a \uN? escape, and the raw char must NOT be emitted
        foreach (var c in chinese) {
            Assert.Contains($"\\u{(int) c}?", stringResult);
            Assert.DoesNotContain(c.ToString(), stringResult);
        }
    }

    [Fact]
    public void SurrogatePairUsesSingleQuestionMarkFallback()
    {
        // 😀 = U+1F600 (decimal 128512), encoded as a surrogate pair in UTF-16
        const string emoji = "😀";
        var expected = "\\u128512?";
        Assert.Equal(expected, Escape(emoji));
        Assert.Equal(expected, EscapeSpan(emoji));
    }

    [Fact]
    public void MarkdownLicenseRendersChineseAsEscapesNotLiterals()
    {
        const string markdown = "# 最终用户许可协议 (EULA)\n\n感谢您选择本软件。";
        var rtf = RenderMarkdown(markdown);

        // the rendered RTF must not contain any raw CJK characters
        foreach (var c in markdown) {
            if (c > 0x7F) {
                Assert.Contains($"\\u{(int) c}?", rtf);
                Assert.DoesNotContain(c.ToString(), rtf);
            }
        }
    }
}
