#nullable enable

using Markdig.Syntax;

namespace Velopack.Packaging.Rtf;

/// <summary>
/// An RTF renderer for a <see cref="HeadingBlock"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{HeadingBlock}" />
public class RtfHeadingRenderer : RtfObjectRenderer<HeadingBlock>
{
    protected override void Write(RtfRenderer renderer, HeadingBlock obj)
    {
        // Map heading levels to RTF font sizes (example: h1 = 32pt, h2 = 28pt, ...)
        int[] fontSizes = [36, 30, 24, 20, 16, 14];
        int level = obj.Level - 1;
        int fontSize = (level >= 0 && level < fontSizes.Length) ? fontSizes[level] : 12;

        renderer.Write($"\\line{{\\pard\\b\\fs{fontSize} "); // RTF font size is half-points
        renderer.WriteLeafInline(obj);
        renderer.Write(" \\par}\n");
    }
}