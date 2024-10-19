using System.Text.RegularExpressions;

public static class StructFinder
{
    public static string[] FindStructs(string code)
    {
        List<string> structs = new();
        code = code.ReplaceLineEndings("\n");
        foreach (var match in Regex.EnumerateMatches(code, @"(^|[^\S\r\n]+)(pub\s*)?struct")) {
            var linesBefore = code.Substring(0, match.Index).Split(new char[] { '\n' }, StringSplitOptions.None);

            var beginLine = linesBefore
                .Select((Text, Index) => new { Text, Index })
                .Last(p => String.IsNullOrWhiteSpace(p.Text))
                .Index + 1;

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