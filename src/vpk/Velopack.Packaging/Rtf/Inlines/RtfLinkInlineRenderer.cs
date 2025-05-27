#nullable enable

using Markdig.Syntax.Inlines;

namespace Velopack.Packaging.Rtf.Inlines;

/// <summary>
/// An RTF renderer for a <see cref="LinkInline"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{LinkInline}" />
public class RtfLinkInlineRenderer : RtfObjectRenderer<LinkInline>
{
    public string? Rel { get; set; }

    protected override void Write(RtfRenderer renderer, LinkInline link)
    {
        if (link.IsImage)
        {
            // Skip images entirely in RTF output
            return;
        }
        else
        {
            // Simulate link: underline, blue
            renderer.Write("{\\ul\\cf1 ");
            renderer.WriteChildren(link);
            renderer.Write("}");
        }
    }
}
