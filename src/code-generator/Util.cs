public static class Util
{
    public static void ReplaceTextInFile(string path, string placeholderName, string text)
    {
        var body = File.ReadAllText(path);
        ReplaceTextBetween(ref body, placeholderName, text);
        File.WriteAllText(path, body.ReplaceLineEndings("\n"));
    }

    public static void ReplaceTextBetween(ref string body, string placeholderName, string text)
    {
        var start = $"// !! AUTO-GENERATED-START {placeholderName}";
        var end = $"// !! AUTO-GENERATED-END {placeholderName}";
        var startIndex = body.IndexOf(start);
        var endIndex = body.IndexOf(end);
        if (startIndex == -1 || endIndex == -1) {
            throw new InvalidOperationException($"Could not find placeholder {placeholderName}");
        }

        // normalize the start index to the beginning of the next line and end index to the end of the previous line
        startIndex = body.IndexOf('\n', startIndex) + 1;
        endIndex = body.LastIndexOf('\n', endIndex);

        body = body.Remove(startIndex, endIndex - startIndex);
        body = body.Insert(startIndex, text.TrimEnd());
    }

    public static string PrefixEveryLine(this string text, string prefix)
    {
        if (string.IsNullOrEmpty(prefix)) { return text; }
        var lines = text.ReplaceLineEndings("\n").Split(['\n']).Select(l => prefix + l);
        return String.Join("\n", lines);
    }

    public static string ToRustComment(this string text)
    {
        return text.PrefixEveryLine("/// ");
    }

    public static string ToCppComment(this string text)
    {
        if (text.Contains("\n")) {
            return "/**\n" + text.PrefixEveryLine(" * ") + "\n */";
        } else {
            return $"/** {text} */";
        }
    }
}