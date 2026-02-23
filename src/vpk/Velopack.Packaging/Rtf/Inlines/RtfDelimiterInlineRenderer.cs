#nullable enable

using Markdig.Syntax.Inlines;

namespace Velopack.Packaging.Rtf.Inlines;

/// <summary>
/// An RTF renderer for a <see cref="DelimiterInline"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{DelimiterInline}" />
public class RtfDelimiterInlineRenderer : RtfObjectRenderer<DelimiterInline>
{
    protected override void Write(RtfRenderer renderer, DelimiterInline obj)
    {
        renderer.WriteEscape(obj.ToLiteral());
        renderer.WriteChildren(obj);
    }
}
