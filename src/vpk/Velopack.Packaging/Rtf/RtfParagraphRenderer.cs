#nullable enable

using Markdig.Syntax;

namespace Velopack.Packaging.Rtf;

/// <summary>
/// An RTF renderer for a <see cref="ParagraphBlock"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{ParagraphBlock}" />
public class RtfParagraphRenderer : RtfObjectRenderer<ParagraphBlock>
{
    protected override void Write(RtfRenderer renderer, ParagraphBlock obj)
    {
        if (!renderer.ImplicitParagraph)
        {
            renderer.Write("{\\pard "); // RTF paragraph start
        }
        renderer.WriteLeafInline(obj);
        if (!renderer.ImplicitParagraph)
        {
            renderer.Write(" \\par}"); // RTF paragraph end

            // Only write \line if next block is not a heading
            bool nextIsHeading = false;
            if (obj.Parent is ContainerBlock parent)
            {
                int index = parent.IndexOf(obj);
                if (index >= 0 && index + 1 < parent.Count)
                {
                    var next = parent[index + 1];
                    nextIsHeading = next is Markdig.Syntax.HeadingBlock;
                }
            }
            if (!nextIsHeading)
            {
                renderer.Write("\\line ");
            }
            renderer.EnsureLine();
        }
    }
}
