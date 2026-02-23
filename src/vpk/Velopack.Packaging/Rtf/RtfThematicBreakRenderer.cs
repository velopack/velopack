#nullable enable

using Markdig.Syntax;

namespace Velopack.Packaging.Rtf;

/// <summary>
/// An RTF renderer for a <see cref="ThematicBreakBlock"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{ThematicBreakBlock}" />
public class RtfThematicBreakRenderer : RtfObjectRenderer<ThematicBreakBlock>
{
    protected override void Write(RtfRenderer renderer, ThematicBreakBlock obj)
    {
        renderer.Write("{\\pard\\qr\\sl0\\slmult1\\line}\n"); // RTF horizontal rule (simulated with a line break)
    }
}
