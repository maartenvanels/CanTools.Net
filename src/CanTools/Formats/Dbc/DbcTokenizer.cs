using System.Text.RegularExpressions;

namespace CanTools.Formats.Dbc;

internal enum DbcTokenKind
{
    Number,
    Word,
    String,
    LeftParen,
    RightParen,
    LeftBracket,
    RightBracket,
    Comma,
    Pipe,
    At,
    Sign,
    Semicolon,
    Colon,
    EndOfFile,
}

internal readonly record struct DbcToken(DbcTokenKind Kind, string Text, int Offset, bool IsKeyword)
{
    public bool Is(DbcTokenKind kind) => Kind == kind;

    public bool IsKeywordNamed(string keyword) => IsKeyword && Text == keyword;
}

/// <summary>
/// Splits DBC text into tokens. Mirrors the upstream tokenizer exactly: alternatives
/// are tried in the same order (a number wins over a word), strings may span lines,
/// and section keywords are ordinary words promoted to keywords afterwards.
/// </summary>
internal static partial class DbcTokenizer
{
    private static readonly HashSet<string> Keywords =
    [
        "BA_", "BA_DEF_", "BA_DEF_DEF_", "BA_DEF_DEF_REL_", "BA_DEF_REL_",
        "BA_DEF_SGTYPE_", "BA_REL_", "BA_SGTYPE_", "BO_", "BO_TX_BU_", "BS_",
        "BU_", "BU_BO_REL_", "BU_EV_REL_", "BU_SG_REL_", "CAT_", "CAT_DEF_",
        "CM_", "ENVVAR_DATA_", "EV_", "EV_DATA_", "FILTER", "NS_", "NS_DESC_",
        "SG_", "SG_MUL_VAL_", "SGTYPE_", "SGTYPE_VAL_", "SIG_GROUP_",
        "SIG_TYPE_REF_", "SIG_VALTYPE_", "SIGTYPE_VALTYPE_", "VAL_",
        "VAL_TABLE_", "VERSION",
    ];

    [GeneratedRegex(
        """
        (?<SKIP>[ \r\n\t]+|//.*?\n)
        |(?<NUMBER>[-+]?[0-9]+\.?[0-9]*([eE][+-]?[0-9]+)?)
        |(?<WORD>[A-Za-z0-9_]+)
        |(?<STRING>"(\\"|[^"])*?")
        |(?<LPAREN>\()
        |(?<RPAREN>\))
        |(?<LBRACKET>\[)
        |(?<RBRACKET>\])
        |(?<COMMA>,)
        |(?<PIPE>\|)
        |(?<AT>@)
        |(?<SIGN>[+-])
        |(?<SCOLON>;)
        |(?<COLON>:)
        |(?<MISMATCH>.)
        """,
        RegexOptions.Singleline | RegexOptions.IgnorePatternWhitespace)]
    private static partial Regex TokenRegex();

    public static List<DbcToken> Tokenize(string text)
    {
        var tokens = new List<DbcToken>();

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
                tokens.Add(new DbcToken(DbcTokenKind.String, value, match.Index, IsKeyword: false));
                continue;
            }

            var (kind, isKeyword) = match switch
            {
                _ when match.Groups["NUMBER"].Success => (DbcTokenKind.Number, false),
                _ when match.Groups["WORD"].Success => (DbcTokenKind.Word, Keywords.Contains(match.Value)),
                _ when match.Groups["LPAREN"].Success => (DbcTokenKind.LeftParen, false),
                _ when match.Groups["RPAREN"].Success => (DbcTokenKind.RightParen, false),
                _ when match.Groups["LBRACKET"].Success => (DbcTokenKind.LeftBracket, false),
                _ when match.Groups["RBRACKET"].Success => (DbcTokenKind.RightBracket, false),
                _ when match.Groups["COMMA"].Success => (DbcTokenKind.Comma, false),
                _ when match.Groups["PIPE"].Success => (DbcTokenKind.Pipe, false),
                _ when match.Groups["AT"].Success => (DbcTokenKind.At, false),
                _ when match.Groups["SIGN"].Success => (DbcTokenKind.Sign, false),
                _ when match.Groups["SCOLON"].Success => (DbcTokenKind.Semicolon, false),
                _ => (DbcTokenKind.Colon, false),
            };

            tokens.Add(new DbcToken(kind, match.Value, match.Index, isKeyword));
        }

        tokens.Add(new DbcToken(DbcTokenKind.EndOfFile, "", text.Length, IsKeyword: false));

        return tokens;
    }
}
