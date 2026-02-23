#nullable enable

using Markdig.Syntax.Inlines;

namespace Velopack.Packaging.Rtf.Inlines;

/// <summary>
/// An RTF renderer for a <see cref="LineBreakInline"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{LineBreakInline}" />
public class RtfLineBreakInlineRenderer : RtfObjectRenderer<LineBreakInline>
{
    public bool RenderAsHardlineBreak { get; set; }

    protected override void Write(RtfRenderer renderer, LineBreakInline obj)
    {
        if (renderer.IsLastInContainer) return;
        renderer.Write("\\line "); // RTF line break
        renderer.EnsureLine();
    }
}
