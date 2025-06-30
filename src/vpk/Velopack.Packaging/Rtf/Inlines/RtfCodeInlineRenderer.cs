#nullable enable

using Markdig.Syntax.Inlines;

namespace Velopack.Packaging.Rtf.Inlines;

/// <summary>
/// An RTF renderer for a <see cref="CodeInline"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{CodeInline}" />
public class RtfCodeInlineRenderer : RtfObjectRenderer<CodeInline>
{
    protected override void Write(RtfRenderer renderer, CodeInline obj)
    {
        renderer.Write("{\\fmodern ");
        renderer.WriteEscape(obj.Content);
        renderer.Write("}");
    }
}
