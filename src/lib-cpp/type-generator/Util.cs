public static class Util
{
    public static void ReplaceTextInFile(string path, string placeholderName, string text)
    {
        var body = File.ReadAllText(path);
        ReplaceTextBetween(ref body, placeholderName, text);
        File.WriteAllText(path, body);
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
}