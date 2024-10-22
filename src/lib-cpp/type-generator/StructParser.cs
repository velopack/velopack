using System.Text.RegularExpressions;
using Superpower;
using Superpower.Parsers;
using Superpower.Model;
using Superpower.Tokenizers;

public class RustField
{
    public string DocComment { get; set; }
    public string Name { get; set; }
    public string Type { get; set; }
    public bool Optional { get; set; }
}

public class RustStruct
{
    public string DocComment { get; set; }
    public string Name { get; set; }
    public List<RustField> Fields { get; set; }
}

public enum RustToken
{
    DocComment,
    Attribute,
    KeywordPub,
    KeywordStruct,
    KeywordImpl,
    Identifier,
    OpenBrace,    // {
    CloseBrace,   // }
    Colon,        // :
    Semicolon,    // ;
    Comma,        // ,
    OtherSymbol,
}

public static class StructParser
{
    // Tokenizer
    private static readonly Tokenizer<RustToken> Tokenizer = new TokenizerBuilder<RustToken>()
        .Ignore(Span.WhiteSpace)
        .Ignore(Span.Regex(@"/\*[\s\S]*?\*/")) // Ignore multi-line comments
        .Ignore(Span.Regex(@"(\s|^)\/\/[^\/].*")) // Ignore single-line comments but not doc comments
        .Match(Span.Regex(@"///.*"), RustToken.DocComment)
        .Match(Span.Regex(@"#\[[^\]]*\]"), RustToken.Attribute)
        .Match(Span.EqualTo("pub"), RustToken.KeywordPub)
        .Match(Span.EqualTo("struct"), RustToken.KeywordStruct)
        .Match(Span.EqualTo("impl"), RustToken.KeywordImpl)
        .Match(Span.Regex(@"[a-zA-Z_][a-zA-Z0-9_<>]*"), RustToken.Identifier)
        .Match(Character.EqualTo('{'), RustToken.OpenBrace)
        .Match(Character.EqualTo('}'), RustToken.CloseBrace)
        .Match(Character.EqualTo(':'), RustToken.Colon)
        .Match(Character.EqualTo(';'), RustToken.Semicolon)
        .Match(Character.EqualTo(','), RustToken.Comma)
        .Match(Character.AnyChar, RustToken.OtherSymbol)
        .Ignore(Span.WhiteSpace)
        .Build();

    // Parsers
    private static readonly TokenListParser<RustToken, string> DocComment =
        Token.EqualTo(RustToken.DocComment)
            .Many()
            .Select(docs => string.Join("\n", docs.Select(doc =>
                doc.ToStringValue().Substring(3).Trim())));

    private static readonly TokenListParser<RustToken, Unit> Attribute =
        Token.EqualTo(RustToken.Attribute).Many().Select(_ => Unit.Value);

    private static TokenListParser<RustToken, Unit> SkipNestedBraces()
    {
        return
            from open in Token.EqualTo(RustToken.OpenBrace)
            from content in SkipNestedBracesContent()
            from close in Token.EqualTo(RustToken.CloseBrace)
            select Unit.Value;
    }

    private static TokenListParser<RustToken, Unit> SkipNestedBracesContent()
    {
        return
            (from nested in SkipNestedBraces() select Unit.Value) // handle recursive braces
            .Or(from nonBrace in Token.Matching<RustToken>(kind => kind != RustToken.OpenBrace && kind != RustToken.CloseBrace,"non-brace").AtLeastOnce() select Unit.Value).Many() 
            .Select(_ => Unit.Value);
    }

    private static readonly TokenListParser<RustToken, Unit> ImplBlock =
        from implKeyword in Token.EqualTo(RustToken.KeywordImpl)
        from rest in Token.Matching<RustToken>(kind => kind != RustToken.OpenBrace, "Expected tokens before '{'").AtLeastOnce()
        from content in SkipNestedBraces()
        select Unit.Value;

    private static readonly TokenListParser<RustToken, string> TypeParser =
        from rest in Token.Matching<RustToken>(kind => kind != RustToken.Comma, "Expected tokens before ','").AtLeastOnce()
        from end in Token.EqualTo(RustToken.Comma) 
        select string.Join(" ", rest.Select(t => t.ToStringValue()));

    private static readonly TokenListParser<RustToken, RustField> FieldDefinition =
        from attrs1 in Attribute.Optional()
        from docComments in DocComment.OptionalOrDefault()
        from attrs2 in Attribute.Optional()
        from pub in Token.EqualTo(RustToken.KeywordPub).Optional()
        from fieldName in Token.EqualTo(RustToken.Identifier).Select(t => t.ToStringValue())
        from colon in Token.EqualTo(RustToken.Colon)
        from fieldType in TypeParser
        select new RustField
        {
            DocComment = docComments,
            Name = fieldName,
            Type = fieldType.Trim()
        };

    private static readonly TokenListParser<RustToken, List<RustField>> StructBody =
        from openBrace in Token.EqualTo(RustToken.OpenBrace)
        from fields in FieldDefinition.Many()
        from closeBrace in Token.EqualTo(RustToken.CloseBrace)
        select fields.ToList();

    private static readonly TokenListParser<RustToken, RustStruct> StructDefinition =
        from attrs1 in Attribute.Optional()
        from docComments in DocComment.OptionalOrDefault()
        from attrs2 in Attribute.Optional()
        from pub in Token.EqualTo(RustToken.KeywordPub).Optional()
        from structKeyword in Token.EqualTo(RustToken.KeywordStruct)
        from structName in Token.EqualTo(RustToken.Identifier).Select(t => t.ToStringValue())
        from structBody in StructBody
        select new RustStruct
        {
            DocComment = docComments,
            Name = structName,
            Fields = structBody
        };

    private static readonly TokenListParser<RustToken, RustStruct> TopLevelItem =
        (from impl in ImplBlock
         select (RustStruct)null)
        .Or(
         from structDef in StructDefinition
         select structDef
        );

    public static IEnumerable<RustStruct> ParseStructs(string code)
    {
        var tokens = Tokenizer.Tokenize(code);
        var parser = TopLevelItem.Many();
        var result = parser(tokens);

        if (!result.HasValue)
        {
            throw new Exception(result.ToString());
        }

        var structs = result.Value.Where(s => s != null).ToArray();
        
        foreach(var s in structs)
        {
            foreach(var f in s.Fields)
            {
                var match = Regex.Match(f.Type, @"Option<(.*)>");
                // If the field type is an Option, extract the inner type and set Optional to true
                if (match.Success)
                {
                    f.Type = match.Groups[1].Value;
                    f.Optional = true;
                }
            }
        }
        
        return structs;
    }
}
