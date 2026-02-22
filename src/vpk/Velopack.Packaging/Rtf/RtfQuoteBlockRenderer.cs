#nullable enable

using Markdig.Syntax;

namespace Velopack.Packaging.Rtf;

/// <summary>
/// An RTF renderer for a <see cref="QuoteBlock"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{QuoteBlock}" />
public class RtfQuoteBlockRenderer : RtfObjectRenderer<QuoteBlock>
{
    protected override void Write(RtfRenderer renderer, QuoteBlock obj)
    {
        renderer.EnsureLine();
        renderer.Write("{\\pard\\li720 "); // Indent for quote
        var savedImplicitParagraph = renderer.ImplicitParagraph;
        renderer.ImplicitParagraph = false;
        renderer.WriteChildren(obj);
        renderer.ImplicitParagraph = savedImplicitParagraph;
        renderer.Write(" \\par}\n");
        renderer.EnsureLine();
    }
}
