#nullable enable

using Markdig.Syntax.Inlines;

namespace Velopack.Packaging.Rtf.Inlines;

/// <summary>
/// An RTF renderer for a <see cref="LiteralInline"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{LiteralInline}" />
public class RtfLiteralInlineRenderer : RtfObjectRenderer<LiteralInline>
{
    protected override void Write(RtfRenderer renderer, LiteralInline obj)
    {
        renderer.WriteEscape(ref obj.Content);
    }
}
