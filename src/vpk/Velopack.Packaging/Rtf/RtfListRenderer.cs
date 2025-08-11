#nullable enable

using Markdig.Syntax;

namespace Velopack.Packaging.Rtf;

/// <summary>
/// An RTF renderer for a <see cref="ListBlock"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{ListBlock}" />
public class RtfListRenderer : RtfObjectRenderer<ListBlock>
{
    protected override void Write(RtfRenderer renderer, ListBlock listBlock)
    {
        renderer.EnsureLine();
        if (listBlock.IsOrdered)
        {
            renderer.Write("{\\pard ");
        }
        else
        {
            renderer.Write("{\\pard ");
        }
        foreach (var item in listBlock)
        {
            var listItem = (ListItemBlock)item;
            var previousImplicit = renderer.ImplicitParagraph;
            renderer.ImplicitParagraph = !listBlock.IsLoose;
            renderer.EnsureLine();
            renderer.Write("\\bullet "); // RTF bullet character
            renderer.WriteChildren(listItem);
            renderer.Write(" \\par");
            renderer.ImplicitParagraph = previousImplicit;
        }
        renderer.Write("}\n");
    }
}
