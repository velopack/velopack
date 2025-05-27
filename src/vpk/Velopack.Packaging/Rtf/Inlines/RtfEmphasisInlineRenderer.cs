#nullable enable

using Markdig.Syntax.Inlines;

namespace Velopack.Packaging.Rtf.Inlines;

/// <summary>
/// An RTF renderer for an <see cref="EmphasisInline"/>.
/// </summary>
/// <seealso cref="RtfObjectRenderer{EmphasisInline}" />
public class RtfEmphasisInlineRenderer : RtfObjectRenderer<EmphasisInline>
{
    public delegate string? GetTagDelegate(EmphasisInline obj);
    public GetTagDelegate? GetTag { get; set; }

    protected override void Write(RtfRenderer renderer, EmphasisInline obj)
    {
        // Use bold or italic for emphasis
        var tag = GetTag?.Invoke(obj) ?? GetDefaultTag(obj);
        if (tag == "b")
            renderer.Write("{\\b ");
        else if (tag == "i")
            renderer.Write("{\\i ");
        renderer.WriteChildren(obj);
        if (tag == "b" || tag == "i")
            renderer.Write("}");
    }
    public static string? GetDefaultTag(EmphasisInline obj)
    {
        return obj.DelimiterChar == '*' || obj.DelimiterChar == '_' ? (obj.DelimiterCount == 2 ? "b" : "i") : null;
    }
}
