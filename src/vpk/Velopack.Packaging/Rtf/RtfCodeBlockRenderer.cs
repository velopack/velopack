#nullable enable

using Markdig.Syntax;

namespace Velopack.Packaging.Rtf;

/// <summary>
/// An RTF renderer for a <see cref="CodeBlock"/> and <see cref="FencedCodeBlock"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{CodeBlock}" />
public class RtfCodeBlockRenderer : RtfObjectRenderer<CodeBlock>
{
    public bool OutputAttributesOnPre { get; set; }

    protected override void Write(RtfRenderer renderer, CodeBlock obj)
    {
        renderer.EnsureLine();
        if (obj is FencedCodeBlock { Info: string info })
        {
            // For RTF, just render code in a monospaced block with a border
            renderer.Write("{\\pard\\fmodern\\brdrb\\brdrs ");
            renderer.WriteLeafRawLines(obj, true, true);
            renderer.Write(" \\par}\n");
        }
        else
        {
            renderer.Write("{\\pard\\fmodern ");
            renderer.WriteLeafRawLines(obj, true, true);
            renderer.Write(" \\par}\n");
        }
    }
}
