using System.Text.RegularExpressions;

public static class StructFinder
{
    public static string[] FindStructs(string code)
    {
        List<string> structs = new();
        code = code.ReplaceLineEndings("\n");
        foreach (var match in Regex.EnumerateMatches(code, @"(^|[^\S\r\n]+)(pub\s*)?struct")) {
            var linesBefore = code.Substring(0, match.Index).Split(new char[] { '\n' }, StringSplitOptions.None);

            // Walk backwards from the struct line, including only doc comments, attributes, and blank lines
            // Stop when we hit a line that is something else (e.g. a macro invocation)
            var structLineIndex = linesBefore.Length - 1;
            var beginLine = structLineIndex;
            for (int j = structLineIndex - 1; j >= 0; j--) {
                var line = linesBefore[j].TrimStart();
                if (line.StartsWith("///") || line.StartsWith("#[") || String.IsNullOrWhiteSpace(linesBefore[j])) {
                    beginLine = j;
                } else {
                    break;
                }
            }
            // Skip leading blank lines
            while (beginLine < structLineIndex && String.IsNullOrWhiteSpace(linesBefore[beginLine])) {
                beginLine++;
            }

            var startIndex = linesBefore
                .Take(beginLine)
                .Aggregate(0, (i, s) => i + s.Length + 1);

            string textAfterStart = code.Substring(startIndex);

            int firstOpenBrace = textAfterStart.IndexOf('{');
            int numOpenBraces = 1;

            // Find the end of the struct by counting open and close braces until numOpenBraces == 0
            for (int i = firstOpenBrace + 1; i < textAfterStart.Length; i++) {
                if (textAfterStart[i] == '{') {
                    numOpenBraces++;
                } else if (textAfterStart[i] == '}') {
                    numOpenBraces--;
                }

                if (numOpenBraces == 0) {
                    var end = startIndex + i + 1;
                    string text = code.Substring(startIndex, end - startIndex);
                    structs.Add(text);
                    break;
                }
            }
        }
        
        return structs.ToArray();
    }
}