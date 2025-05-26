using System.Text;
using System.Xml;
using Markdig;
using MarkdigExtensions.RtfRenderer;

namespace Velopack.Packaging.Windows.Msi;

public static class MsiUtil
{
    public static string SanitizeDirectoryString(string name)
        => string.Join("_", name.Split(Path.GetInvalidPathChars()));

    public static string FormatXmlMessage(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
            return "";

        StringBuilder sb = new();
        XmlWriterSettings settings = new() {
            ConformanceLevel = ConformanceLevel.Fragment,
            NewLineHandling = NewLineHandling.None,
        };
        using XmlWriter writer = XmlWriter.Create(sb, settings);
        writer.WriteString(message);
        writer.Flush();
        var rv = sb.ToString();
        rv = rv.Replace("\r", "&#10;").Replace("\n", "&#13;");
        return rv;
    }

    public static string GetFileContent(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return "";
        string fileContents = File.ReadAllText(filePath, Encoding.UTF8);
        return fileContents;
    }

    public static string RenderMarkdownAsPlainText(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return "";
        return Markdown.ToPlainText(markdown);
    }

    public static string RenderMarkdownAsRtf(string markdown)
    {
        var builder = new StringBuilder();
        using var writer = new StringWriter(builder);
        var renderer = new RtfRenderer(writer);
        renderer.StartDocument();
        _ = Markdown.Convert(markdown, renderer);
        renderer.CloseDocument();
        return builder.ToString();
    }
}