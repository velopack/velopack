#nullable enable

using Markdig.Syntax.Inlines;

namespace Velopack.Packaging.Rtf.Inlines;

/// <summary>
/// An RTF renderer for an <see cref="AutolinkInline"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{AutolinkInline}" />
public class RtfAutolinkInlineRenderer : RtfObjectRenderer<AutolinkInline>
{
    public string? Rel { get; set; }

    protected override void Write(RtfRenderer renderer, AutolinkInline obj)
    {
        // Render as underlined, blue text (simulating a link in RTF)
        renderer.Write("{\\ul\\cf1 ");
        renderer.WriteEscape(obj.Url);
        renderer.Write("}");
    }
}
