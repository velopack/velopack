#nullable enable

using Markdig.Syntax;

namespace Velopack.Packaging.Rtf;

/// <summary>
/// An RTF renderer for a <see cref="HtmlBlock"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{HtmlBlock}" />
public class RtfBlockRenderer : RtfObjectRenderer<HtmlBlock>
{
    protected override void Write(RtfRenderer renderer, HtmlBlock obj)
    {
        // Placeholder: Write the block as RTF raw text (should be adapted for real RTF output)
        renderer.Write("{\\rtf1 ");
        renderer.WriteLeafRawLines(obj, true, false);
        renderer.Write("}\n");
    }
}
