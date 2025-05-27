#nullable enable

using Markdig.Helpers;
using Markdig.Renderers;
using Markdig.Syntax;

namespace Velopack.Packaging.Rtf;

/// <summary>
/// Default RTF renderer for a Markdown <see cref="MarkdownDocument"/> object.
/// </summary>
/// <seealso cref="TextRendererBase{RtfRenderer}" />
public class RtfRenderer : TextRendererBase<RtfRenderer>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="RtfRenderer"/> class.
    /// </summary>
    /// <param name="writer">The writer.</param>
    public RtfRenderer(TextWriter writer) : base(writer)
    {
        // Default block renderers
        ObjectRenderers.Add(new RtfCodeBlockRenderer());
        ObjectRenderers.Add(new RtfListRenderer());
        ObjectRenderers.Add(new RtfHeadingRenderer());
        ObjectRenderers.Add(new RtfBlockRenderer());
        ObjectRenderers.Add(new RtfParagraphRenderer());
        ObjectRenderers.Add(new RtfQuoteBlockRenderer());
        ObjectRenderers.Add(new RtfThematicBreakRenderer());

        // Inline renderers
        ObjectRenderers.Add(new Inlines.RtfAutolinkInlineRenderer());
        ObjectRenderers.Add(new Inlines.RtfCodeInlineRenderer());
        ObjectRenderers.Add(new Inlines.RtfDelimiterInlineRenderer());
        ObjectRenderers.Add(new Inlines.RtfEmphasisInlineRenderer());
        ObjectRenderers.Add(new Inlines.RtfLineBreakInlineRenderer());
        ObjectRenderers.Add(new Inlines.RtfLinkInlineRenderer());
        ObjectRenderers.Add(new Inlines.RtfLiteralInlineRenderer());
    }

    public bool ImplicitParagraph { get; set; }

    /// <summary>
    /// Writes the lines of a <see cref="LeafBlock"/>
    /// </summary>
    /// <param name="leafBlock">The leaf block.</param>
    /// <param name="writeEndOfLines">if set to <c>true</c> write end of lines.</param>
    /// <param name="escape">if set to <c>true</c> escape the content for RTF</param>
    /// <param name="softEscape">Only escape minimal RTF chars</param>
    /// <returns>This instance</returns>
    public RtfRenderer WriteLeafRawLines(LeafBlock leafBlock, bool writeEndOfLines, bool escape, bool softEscape = false)
    {
        if (leafBlock is null) throw new ArgumentNullException(nameof(leafBlock));
        var slices = leafBlock.Lines.Lines;
        if (slices is not null) {
            for (int i = 0; i < slices.Length; i++) {
                ref StringSlice slice = ref slices[i].Slice;
                if (slice.Text is null) {
                    break;
                }

                if (!writeEndOfLines && i > 0) {
                    WriteLine();
                }

                ReadOnlySpan<char> span = slice.AsSpan();
                if (escape) {
                    WriteEscape(span, softEscape);
                } else {
                    Write(span);
                }

                if (writeEndOfLines) {
                    WriteLine();
                }
            }
        }

        return this;
    }

    /// <summary>
    /// Writes the content escaped for RTF, converting Unicode to RTF Unicode escapes.
    /// </summary>
    /// <param name="content">The string content.</param>
    /// <param name="softEscape">If true, only escape minimal RTF chars.</param>
    public void WriteEscape(string? content, bool softEscape = false)
    {
        if (content == null) return;
        for (int i = 0; i < content.Length; i++) {
            char c = content[i];
            if (char.IsHighSurrogate(c) && i + 1 < content.Length && char.IsLowSurrogate(content[i + 1])) {
                int codepoint = char.ConvertToUtf32(c, content[i + 1]);
                Write($"\\u{codepoint}?"); // RTF spec: use '?' as fallback char
                i++; // skip low surrogate
            } else if (c == '\\') Write("\\\\");
            else if (c == '{') Write("\\{");
            else if (c == '}') Write("\\}");
            else if (c < 0x20 || (c >= 0x7F && c <= 0x9F)) { } // skip control characters
            else if (c <= 0x7F) // ASCII
                Write(c);
            else // BMP Unicode
                Write($"\\u{(int) c}{c}");
        }
    }

    /// <summary>
    /// Writes the content escaped for RTF, converting Unicode to RTF Unicode escapes.
    /// </summary>
    /// <param name="span">The character span.</param>
    /// <param name="softEscape">If true, only escape minimal RTF chars.</param>
    public void WriteEscape(ReadOnlySpan<char> span, bool softEscape = false)
    {
        for (int i = 0; i < span.Length; i++) {
            char c = span[i];
            if (char.IsHighSurrogate(c) && i + 1 < span.Length && char.IsLowSurrogate(span[i + 1])) {
                int codepoint = char.ConvertToUtf32(c, span[i + 1]);
                Write($"\\u{codepoint}?"); // RTF spec: use '?' as fallback char
                i++; // skip low surrogate
            } else if (c == '\\') Write("\\\\");
            else if (c == '{') Write("\\{");
            else if (c == '}') Write("\\}");
            else if (c < 0x20 || (c >= 0x7F && c <= 0x9F)) { } // skip control characters
            else if (c <= 0x7F) // ASCII
                Write(c);
            else // BMP Unicode
                Write($"\\u{(int) c}{c}");
        }
    }

    public void WriteEscape(ref Markdig.Helpers.StringSlice slice, bool softEscape = false)
    {
        WriteEscape(slice.AsSpan(), softEscape);
    }

    public void WriteEscape(Markdig.Helpers.StringSlice slice, bool softEscape = false)
    {
        WriteEscape(slice.AsSpan(), softEscape);
    }

    /// <summary>
    /// Writes the RTF document start block.
    /// </summary>
    public void WriteRtfStart()
    {
        WriteLine(@"{\rtf1\ansi\ansicpg1252\deff0\nouicompat\deflang1031{\fonttbl{\f0\fnil\fcharset0 Calibri;}}");
        WriteLine(@"{\colortbl ;\red0\green0\blue0;}");
        WriteLine(@"\viewkind4\uc1\pard\sa200\sl276\slmult1\f0\fs19\lang7");
    }

    /// <summary>
    /// Writes the RTF document end block.
    /// </summary>
    public void WriteRtfEnd()
    {
        WriteLine("}");
    }
}