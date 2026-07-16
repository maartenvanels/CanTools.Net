using System.Text.RegularExpressions;

namespace CanTools.Formats.Sym;

internal enum SymTokenKind
{
    Comment,
    HexNumber,
    Number,
    String,
    UnitAttribute,
    Prefix,        // /f: /o: /min: /max: /spn: /d: /ln: /e: /p:
    Flag,          // -m -h -b -s -t -v -p
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    Comma,
    Equals,
    Section,       // {ENUMS} {SIGNALS} {SEND} {RECEIVE} {SENDRECEIVE}
    Word,
    EndOfFile,
}

internal readonly record struct SymToken(SymTokenKind Kind, string Text, int Offset, bool IsKeyword);

internal sealed partial class SymTokenizer
{
    private static readonly HashSet<string> Keywords =
    [
        "FormatVersion", "Title", "UniqueVariables", "FloatDecimalPlaces", "BRS",
        "Enum", "Sig", "ID", "Len", "Mux", "CycleTime", "Timeout", "MinInterval",
        "Color", "Var", "Type",
    ];

    [GeneratedRegex(
        "(?<SKIP>[ \\r\\n\\t]+)"
        + "|(?<COMMENT>//.*?\\n)"
        + "|(?<HEX>-?[0-9]+\\.?[0-9A-F]*([eE][+-]?[0-9]+)?h)"
        + "|(?<NUMBER>-?[0-9]+(\\.[0-9]+)?([eE][+-]?[0-9]+)?)"
        + "|(?<STRING>\"(\\\\\"|[^\"])*?\")"
        + "|(?<U>/u:(\"(\\\\\"|[^\"])*?\"|\\S+))"
        + "|(?<PREFIX>/f:|/o:|/min:|/max:|/spn:|/d:|/ln:|/e:|/p:)"
        + "|(?<FLAG>-[mhbstvp])"
        + "|(?<LPAREN>\\()"
        + "|(?<RPAREN>\\))"
        + "|(?<LBRACKET>\\[)"
        + "|(?<RBRACKET>\\])"
        + "|(?<COMMA>,)"
        + "|(?<EQUALS>=)"
        + "|(?<SECTION>\\{ENUMS\\}|\\{SIGNALS\\}|\\{SEND\\}|\\{RECEIVE\\}|\\{SENDRECEIVE\\})"
        + "|(?<WORD>[^\\s=\\(\\]\\-]+)"
        + "|(?<MISMATCH>.)",
        RegexOptions.Singleline)]
    private static partial Regex TokenRegex();

    public static List<SymToken> Tokenize(string text)
    {
        var tokens = new List<SymToken>();

        foreach (Match match in TokenRegex().Matches(text))
        {
            if (match.Groups["SKIP"].Success)
            {
                continue;
            }

            if (match.Groups["MISMATCH"].Success)
            {
                throw ParseException.AtOffset(text, match.Index);
            }

            if (match.Groups["STRING"].Success)
            {
                var value = match.Value[1..^1].Replace("\\\"", "\"");
                tokens.Add(new SymToken(SymTokenKind.String, value, match.Index, false));
                continue;
            }

            var (kind, isKeyword) = match switch
            {
                _ when match.Groups["COMMENT"].Success => (SymTokenKind.Comment, false),
                _ when match.Groups["HEX"].Success => (SymTokenKind.HexNumber, false),
                _ when match.Groups["NUMBER"].Success => (SymTokenKind.Number, false),
                _ when match.Groups["U"].Success => (SymTokenKind.UnitAttribute, false),
                _ when match.Groups["PREFIX"].Success => (SymTokenKind.Prefix, false),
                _ when match.Groups["FLAG"].Success => (SymTokenKind.Flag, false),
                _ when match.Groups["LPAREN"].Success => (SymTokenKind.LeftParen, false),
                _ when match.Groups["RPAREN"].Success => (SymTokenKind.RightParen, false),
                _ when match.Groups["LBRACKET"].Success => (SymTokenKind.LeftBracket, false),
                _ when match.Groups["RBRACKET"].Success => (SymTokenKind.RightBracket, false),
                _ when match.Groups["COMMA"].Success => (SymTokenKind.Comma, false),
                _ when match.Groups["EQUALS"].Success => (SymTokenKind.Equals, false),
                _ when match.Groups["SECTION"].Success => (SymTokenKind.Section, false),
                _ => (SymTokenKind.Word, Keywords.Contains(match.Value)),
            };

            tokens.Add(new SymToken(kind, match.Value, match.Index, isKeyword));
        }

        tokens.Add(new SymToken(SymTokenKind.EndOfFile, "", text.Length, false));

        return tokens;
    }
}

