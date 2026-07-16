namespace CanTools.Formats.Sym;

/// <summary>Parses tokenized SYM text into raw statements.</summary>
internal sealed class SymParser
{
    private readonly string _text;
    private readonly List<SymToken> _tokens;
    private int _position;

    public SymParser(string text)
    {
        _text = text;
        _tokens = SymTokenizer.Tokenize(text);
    }

    private SymToken Peek(int lookahead = 0) =>
        _tokens[Math.Min(_position + lookahead, _tokens.Count - 1)];

    private SymToken Next() => _tokens[Math.Min(_position++, _tokens.Count - 1)];

    private ParseException Error(SymToken token) => ParseException.AtOffset(_text, token.Offset);

    private string Expect(SymTokenKind kind)
    {
        var token = Peek();

        if (token.Kind != kind)
        {
            throw Error(token);
        }

        _position++;

        return token.Text;
    }

    private void ExpectKeyword(string keyword)
    {
        var token = Peek();

        if (!token.IsKeyword || token.Text != keyword)
        {
            throw Error(token);
        }

        _position++;
    }

    private bool PeekKeyword(string keyword) => Peek().IsKeyword && Peek().Text == keyword;

    // A name may be a quoted string, a plain word, or even a keyword-shaped word.
    private string ExpectName()
    {
        var token = Peek();

        if (token.Kind is not (SymTokenKind.String or SymTokenKind.Word or SymTokenKind.Number
            or SymTokenKind.HexNumber))
        {
            throw Error(token);
        }

        _position++;

        return token.Text;
    }

    private string ExpectNumber()
    {
        var token = Peek();

        if (token.Kind != SymTokenKind.Number)
        {
            throw Error(token);
        }

        _position++;

        return token.Text;
    }

    private string? TakeComment() =>
        Peek().Kind == SymTokenKind.Comment ? CommentText(Next().Text) : null;

    // "// apa\r\n" → "apa": drop the three-character prefix and the line ending.
    private static string CommentText(string raw) => raw[3..].TrimEnd('\r', '\n');

    public SymFileContents Parse()
    {
        var contents = new SymFileContents();

        while (Peek().Kind == SymTokenKind.Comment)
        {
            _position++;
        }

        ExpectKeyword("FormatVersion");
        Expect(SymTokenKind.Equals);
        contents.Version = ExpectNumber();
        Expect(SymTokenKind.Comment);

        while (PeekKeyword("Title") || PeekKeyword("UniqueVariables")
               || PeekKeyword("FloatDecimalPlaces") || PeekKeyword("BRS"))
        {
            _position++;
            Expect(SymTokenKind.Equals);
            _position++;
        }

        while (Peek().Kind != SymTokenKind.EndOfFile)
        {
            var section = Expect(SymTokenKind.Section);

            switch (section)
            {
                case "{ENUMS}":
                    ParseEnums(contents);
                    break;
                case "{SIGNALS}":
                    while (Peek().Kind == SymTokenKind.Comment || PeekKeyword("Sig"))
                    {
                        if (Peek().Kind == SymTokenKind.Comment)
                        {
                            _position++;
                            continue;
                        }

                        ExpectKeyword("Sig");
                        Expect(SymTokenKind.Equals);
                        contents.SignalTemplates.Add(ParseSignalDefinition(isVariable: false));
                    }
                    break;
                default:
                    var symbols = section switch
                    {
                        "{SEND}" => contents.Send,
                        "{RECEIVE}" => contents.Receive,
                        _ => contents.SendReceive,
                    };
                    while (Peek().Kind is SymTokenKind.Comment or SymTokenKind.LeftBracket)
                    {
                        if (Peek().Kind == SymTokenKind.Comment)
                        {
                            _position++;
                            continue;
                        }

                        symbols.Add(ParseSymbol());
                    }
                    break;
            }
        }

        return contents;
    }

    private void ParseEnums(SymFileContents contents)
    {
        while (Peek().Kind == SymTokenKind.Comment || PeekKeyword("Enum"))
        {
            if (Peek().Kind == SymTokenKind.Comment)
            {
                _position++;
                continue;
            }

            _position++;
            Expect(SymTokenKind.Equals);
            var name = ExpectName();
            Expect(SymTokenKind.LeftParen);

            var values = new Dictionary<long, NamedSignalValue>();

            if (Peek().Kind != SymTokenKind.RightParen)
            {
                while (true)
                {
                    var value = long.Parse(ExpectNumber());
                    Expect(SymTokenKind.Equals);
                    values[value] = new NamedSignalValue(value, Expect(SymTokenKind.String));

                    if (Peek().Kind != SymTokenKind.Comma)
                    {
                        break;
                    }

                    _position++;
                    TakeComment();
                }
            }

            Expect(SymTokenKind.RightParen);
            TakeComment();

            contents.Enums[name] = values;
        }
    }

    private SymSignalDefinition ParseSignalDefinition(bool isVariable)
    {
        // Sig=<name> <type> [len] [-h|-b] [-m] attrs [comment]
        // Var=<name> <type> <start>,<len> flags attrs [comment] — the caller reads
        // start/length; this method starts at the type for both shapes.
        var name = ExpectName();
        var typeName = ExpectName();
        string? lengthText = null;

        if (!isVariable && Peek().Kind == SymTokenKind.Number)
        {
            lengthText = Next().Text;
        }

        var isBigEndian = false;

        while (Peek().Kind == SymTokenKind.Flag)
        {
            var flag = Next().Text;

            if (flag == "-m")
            {
                isBigEndian = true;
            }
            else if (isVariable
                     ? flag is not ("-v" or "-s" or "-h")
                     : flag is not ("-h" or "-b"))
            {
                throw Error(_tokens[_position - 1]);
            }
        }

        var attributes = ParseAttributes();
        var comment = TakeComment();

        return new SymSignalDefinition(name, typeName, lengthText, isBigEndian, attributes, comment);
    }

    private List<(string Prefix, string Value)> ParseAttributes()
    {
        var attributes = new List<(string, string)>();

        while (Peek().Kind is SymTokenKind.UnitAttribute or SymTokenKind.Prefix)
        {
            var token = Next();

            if (token.Kind == SymTokenKind.UnitAttribute)
            {
                var value = token.Text.StartsWith("/u:\"", StringComparison.Ordinal)
                    ? token.Text[4..^1].Replace("\\\"", "\"")
                    : token.Text[3..];
                attributes.Add(("/u:", value));
                continue;
            }

            var argument = Peek();
            if (argument.Kind is not (SymTokenKind.Number or SymTokenKind.HexNumber
                or SymTokenKind.Word or SymTokenKind.String))
            {
                throw Error(argument);
            }

            _position++;
            attributes.Add((token.Text, argument.Text));
        }

        return attributes;
    }

    private SymSymbol ParseSymbol()
    {
        Expect(SymTokenKind.LeftBracket);
        var symbol = new SymSymbol(ExpectName());
        Expect(SymTokenKind.RightBracket);

        while (true)
        {
            var token = Peek();

            if (token.Kind == SymTokenKind.Comment)
            {
                _position++;
                continue;
            }

            if (!token.IsKeyword)
            {
                return symbol;
            }

            switch (token.Text)
            {
                case "ID":
                    _position++;
                    Expect(SymTokenKind.Equals);
                    symbol.IdText = Expect(SymTokenKind.HexNumber);
                    if (Peek().Kind == SymTokenKind.HexNumber && Peek().Text.StartsWith('-'))
                    {
                        symbol.IdRangeEndText = Next().Text;
                    }
                    symbol.IdComment = TakeComment();
                    break;
                case "Len":
                    _position++;
                    Expect(SymTokenKind.Equals);
                    symbol.LengthText = ExpectNumber();
                    break;
                case "CycleTime":
                    _position++;
                    Expect(SymTokenKind.Equals);
                    symbol.CycleTimeText = ExpectNumber();
                    if (Peek() is { Kind: SymTokenKind.Flag, Text: "-p" })
                    {
                        _position++;
                    }
                    break;
                case "Timeout" or "MinInterval":
                    _position++;
                    Expect(SymTokenKind.Equals);
                    ExpectNumber();
                    break;
                case "Color":
                    _position++;
                    Expect(SymTokenKind.Equals);
                    Expect(SymTokenKind.HexNumber);
                    break;
                case "Type":
                    _position++;
                    Expect(SymTokenKind.Equals);
                    symbol.TypeText = ExpectName();
                    break;
                case "Mux":
                    symbol.MuxLines.Add(ParseMuxLine());
                    break;
                case "Sig":
                    _position++;
                    Expect(SymTokenKind.Equals);
                    var signalName = ExpectName();
                    symbol.SignalReferences.Add((signalName, ExpectNumber()));
                    break;
                case "Var":
                    _position++;
                    Expect(SymTokenKind.Equals);
                    symbol.Variables.Add(ParseVariable());
                    break;
                default:
                    return symbol;
            }
        }
    }

    private SymMuxLine ParseMuxLine()
    {
        ExpectKeyword("Mux");
        Expect(SymTokenKind.Equals);
        var name = ExpectName();
        var start = ExpectNumber();
        Expect(SymTokenKind.Comma);
        var length = ExpectNumber();

        var muxIdToken = Peek();
        if (muxIdToken.Kind is not (SymTokenKind.Number or SymTokenKind.HexNumber))
        {
            throw Error(muxIdToken);
        }
        _position++;

        var isBigEndian = false;
        while (Peek().Kind == SymTokenKind.Flag && Peek().Text is "-t" or "-m")
        {
            isBigEndian |= Next().Text == "-m";
        }

        return new SymMuxLine(name, start, length, muxIdToken.Text, isBigEndian, TakeComment());
    }

    private SymSignalDefinition ParseVariable()
    {
        // Var=<name> <type> <start>,<length> flags attrs [comment]
        var name = ExpectName();
        var typeName = ExpectName();
        var start = ExpectNumber();
        Expect(SymTokenKind.Comma);
        var length = ExpectNumber();

        var isBigEndian = false;
        while (Peek().Kind == SymTokenKind.Flag && Peek().Text is "-v" or "-m" or "-s" or "-h")
        {
            isBigEndian |= Next().Text == "-m";
        }

        var attributes = ParseAttributes();
        var comment = TakeComment();

        return new SymSignalDefinition(
            name, typeName, length, isBigEndian, attributes, comment, StartText: start);
    }
}
