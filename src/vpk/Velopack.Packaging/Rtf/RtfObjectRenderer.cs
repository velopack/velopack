#nullable enable

using Markdig.Renderers;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;

namespace Velopack.Packaging.Rtf;

/// <summary>
/// A base class for RTF rendering <see cref="Block"/> and <see cref="Inline"/> Markdown objects.
/// </summary>
/// <typeparam name="TObject">The type of the object.</typeparam>
/// <seealso cref="IMarkdownObjectRenderer" />
public abstract class RtfObjectRenderer<TObject> : MarkdownObjectRenderer<RtfRenderer, TObject> where TObject : MarkdownObject
{
}
