namespace CanTools.Formats.Dbc;

/// <summary>
/// Parses tokenized DBC text into raw statements. Hand-written recursive descent
/// that accepts the same input as upstream's textparser grammar; anything else
/// raises a <see cref="ParseException"/> pointing at the offending token.
/// </summary>
internal sealed class DbcParser
{
    private readonly string _text;
    private readonly List<DbcToken> _tokens;
    private int _position;

    private DbcParser(string text)
    {
        _text = text;
        _tokens = DbcTokenizer.Tokenize(text);
    }

    public static DbcFileContents Parse(string text) => new DbcParser(text).ParseFile();

    private DbcToken Peek(int lookahead = 0) =>
        _tokens[Math.Min(_position + lookahead, _tokens.Count - 1)];

    private DbcToken Next() => _tokens[Math.Min(_position++, _tokens.Count - 1)];

    private ParseException Error(DbcToken token) => ParseException.AtOffset(_text, token.Offset);

    private string Expect(DbcTokenKind kind)
    {
        var token = Peek();

        if (!token.Is(kind) || (kind == DbcTokenKind.Word && token.IsKeyword))
        {
            throw Error(token);
        }

        _position++;

        return token.Text;
    }

    private void ExpectKeyword(string keyword)
    {
        var token = Peek();

        if (!token.IsKeywordNamed(keyword))
        {
            throw Error(token);
        }

        _position++;
    }

    private bool TryTakeKeyword(string keyword)
    {
        if (Peek().IsKeywordNamed(keyword))
        {
            _position++;

            return true;
        }

        return false;
    }

    private string ExpectNumberOrString()
    {
        var token = Peek();

        if (!token.Is(DbcTokenKind.Number) && !token.Is(DbcTokenKind.String))
        {
            throw Error(token);
        }

        _position++;

        return token.Text;
    }

    private DbcFileContents ParseFile()
    {
        var contents = new DbcFileContents();

        while (!Peek().Is(DbcTokenKind.EndOfFile))
        {
            var token = Peek();

            if (!token.IsKeyword)
            {
                throw Error(token);
            }

            switch (token.Text)
            {
                case "VERSION":
                    _position++;
                    var version = Expect(DbcTokenKind.String);
                    contents.Version ??= version;
                    break;
                case "NS_":
                    _position++;
                    Expect(DbcTokenKind.Colon);
                    // Consume the symbol list until any token followed by ':' (the
                    // start of the next section), like upstream's AnyUntil.
                    while (!Peek().Is(DbcTokenKind.EndOfFile) && !Peek(1).Is(DbcTokenKind.Colon))
                    {
                        _position++;
                    }
                    break;
                case "BS_":
                    _position++;
                    Expect(DbcTokenKind.Colon);
                    break;
                case "BU_":
                    _position++;
                    Expect(DbcTokenKind.Colon);
                    var nodes = new List<string>();
                    while (Peek().Is(DbcTokenKind.Word) && !Peek().IsKeyword)
                    {
                        nodes.Add(Next().Text);
                    }
                    // A later BU_ statement replaces an earlier one, like upstream.
                    contents.Nodes = nodes;
                    break;
                case "BO_":
                    contents.Messages.Add(ParseMessage());
                    break;
                case "CM_":
                    contents.Comments.Add(ParseComment());
                    break;
                case "BA_DEF_":
                    contents.AttributeDefinitions.Add(ParseAttributeDefinition(
                        "BA_DEF_", ["SG_", "BO_", "EV_", "BU_"]));
                    break;
                case "BA_DEF_DEF_":
                    _position++;
                    contents.AttributeDefaults.Add(new DbcAttributeDefaultStatement(
                        Expect(DbcTokenKind.String), ExpectNumberOrString()));
                    Expect(DbcTokenKind.Semicolon);
                    break;
                case "BA_":
                    contents.Attributes.Add(ParseAttribute());
                    break;
                case "BA_DEF_REL_":
                    contents.RelationAttributeDefinitions.Add(ParseAttributeDefinition(
                        "BA_DEF_REL_", ["BU_SG_REL_", "BU_BO_REL_"]));
                    break;
                case "BA_DEF_DEF_REL_":
                    _position++;
                    contents.RelationAttributeDefaults.Add(new DbcAttributeDefaultStatement(
                        Expect(DbcTokenKind.String), ExpectNumberOrString()));
                    Expect(DbcTokenKind.Semicolon);
                    break;
                case "BA_REL_":
                    contents.RelationAttributes.Add(ParseRelationAttribute());
                    break;
                case "VAL_":
                    contents.Choices.Add(ParseChoices());
                    break;
                case "VAL_TABLE_":
                    _position++;
                    contents.ValueTables.Add(new DbcValueTableStatement(
                        Expect(DbcTokenKind.Word), ParseValueTextPairs()));
                    Expect(DbcTokenKind.Semicolon);
                    break;
                case "SIG_VALTYPE_":
                    _position++;
                    var typeFrameId = Expect(DbcTokenKind.Number);
                    var typeSignal = Expect(DbcTokenKind.Word);
                    Expect(DbcTokenKind.Colon);
                    contents.SignalTypes.Add(new DbcSignalTypeStatement(
                        typeFrameId, typeSignal, Expect(DbcTokenKind.Number)));
                    Expect(DbcTokenKind.Semicolon);
                    break;
                case "SG_MUL_VAL_":
                    contents.MultiplexerValues.Add(ParseMultiplexerValues());
                    break;
                case "SIG_GROUP_":
                    contents.SignalGroups.Add(ParseSignalGroup());
                    break;
                case "EV_":
                    contents.EnvironmentVariables.Add(ParseEnvironmentVariable());
                    break;
                case "BO_TX_BU_":
                    _position++;
                    var sendersFrameId = Expect(DbcTokenKind.Number);
                    Expect(DbcTokenKind.Colon);
                    var senders = new List<string> { Expect(DbcTokenKind.Word) };
                    while (Peek().Is(DbcTokenKind.Comma))
                    {
                        _position++;
                        senders.Add(Expect(DbcTokenKind.Word));
                    }
                    contents.MessageSenders.Add(new DbcMessageSendersStatement(sendersFrameId, senders));
                    Expect(DbcTokenKind.Semicolon);
                    break;
                default:
                    // Keywords like ENVVAR_DATA_ or SGTYPE_ have no grammar rule
                    // upstream either; a standalone statement is a syntax error.
                    throw Error(token);
            }
        }

        return contents;
    }

    private DbcMessageStatement ParseMessage()
    {
        ExpectKeyword("BO_");
        var frameId = Expect(DbcTokenKind.Number);
        var name = Expect(DbcTokenKind.Word);
        Expect(DbcTokenKind.Colon);
        var length = Expect(DbcTokenKind.Number);
        var sender = Expect(DbcTokenKind.Word);
        var signals = new List<DbcSignalStatement>();

        while (Peek().IsKeywordNamed("SG_"))
        {
            signals.Add(ParseSignal());
        }

        return new DbcMessageStatement(frameId, name, length, sender, signals);
    }

    private DbcSignalStatement ParseSignal()
    {
        ExpectKeyword("SG_");
        var name = Expect(DbcTokenKind.Word);
        string? muxIndicator = null;

        if (Peek().Is(DbcTokenKind.Word) && !Peek().IsKeyword)
        {
            muxIndicator = Next().Text;
        }

        Expect(DbcTokenKind.Colon);
        var start = Expect(DbcTokenKind.Number);
        Expect(DbcTokenKind.Pipe);
        var length = Expect(DbcTokenKind.Number);
        Expect(DbcTokenKind.At);
        var byteOrder = Expect(DbcTokenKind.Number);
        var sign = Expect(DbcTokenKind.Sign);
        Expect(DbcTokenKind.LeftParen);
        var scale = Expect(DbcTokenKind.Number);
        Expect(DbcTokenKind.Comma);
        var offset = Expect(DbcTokenKind.Number);
        Expect(DbcTokenKind.RightParen);
        Expect(DbcTokenKind.LeftBracket);
        var minimum = Expect(DbcTokenKind.Number);
        Expect(DbcTokenKind.Pipe);
        var maximum = Expect(DbcTokenKind.Number);
        Expect(DbcTokenKind.RightBracket);
        var unit = Expect(DbcTokenKind.String);
        var receivers = new List<string> { Expect(DbcTokenKind.Word) };

        while (Peek().Is(DbcTokenKind.Comma))
        {
            _position++;
            receivers.Add(Expect(DbcTokenKind.Word));
        }

        return new DbcSignalStatement(
            name, muxIndicator, start, length, byteOrder, sign,
            scale, offset, minimum, maximum, unit, receivers);
    }

    private DbcCommentStatement ParseComment()
    {
        ExpectKeyword("CM_");
        DbcCommentStatement comment;

        if (TryTakeKeyword("BU_"))
        {
            comment = new DbcCommentStatement(
                "BU_", null, Expect(DbcTokenKind.Word), Expect(DbcTokenKind.String));
        }
        else if (TryTakeKeyword("BO_"))
        {
            comment = new DbcCommentStatement(
                "BO_", Expect(DbcTokenKind.Number), null, Expect(DbcTokenKind.String));
        }
        else if (TryTakeKeyword("SG_"))
        {
            var frameId = Expect(DbcTokenKind.Number);
            comment = new DbcCommentStatement(
                "SG_", frameId, Expect(DbcTokenKind.Word), Expect(DbcTokenKind.String));
        }
        else if (TryTakeKeyword("EV_"))
        {
            comment = new DbcCommentStatement(
                "EV_", null, Expect(DbcTokenKind.Word), Expect(DbcTokenKind.String));
        }
        else
        {
            comment = new DbcCommentStatement("", null, null, Expect(DbcTokenKind.String));
        }

        Expect(DbcTokenKind.Semicolon);

        return comment;
    }

    private DbcAttributeDefinitionStatement ParseAttributeDefinition(
        string keyword, string[] kinds)
    {
        ExpectKeyword(keyword);
        string? kind = null;

        foreach (var candidate in kinds)
        {
            if (TryTakeKeyword(candidate))
            {
                kind = candidate;
                break;
            }
        }

        var name = Expect(DbcTokenKind.String);
        var typeName = Expect(DbcTokenKind.Word);
        var stringValues = new List<string>();
        var numberValues = new List<string>();

        if (Peek().Is(DbcTokenKind.String))
        {
            stringValues.Add(Next().Text);

            while (Peek().Is(DbcTokenKind.Comma))
            {
                _position++;
                stringValues.Add(Expect(DbcTokenKind.String));
            }
        }
        else
        {
            while (Peek().Is(DbcTokenKind.Number))
            {
                numberValues.Add(Next().Text);
            }
        }

        Expect(DbcTokenKind.Semicolon);

        return new DbcAttributeDefinitionStatement(kind, name, typeName, stringValues, numberValues);
    }

    private DbcAttributeStatement ParseAttribute()
    {
        ExpectKeyword("BA_");
        var name = Expect(DbcTokenKind.String);
        DbcAttributeStatement attribute;

        if (TryTakeKeyword("BU_"))
        {
            attribute = new DbcAttributeStatement(
                name, "BU_", null, Expect(DbcTokenKind.Word), ExpectNumberOrString());
        }
        else if (TryTakeKeyword("BO_"))
        {
            attribute = new DbcAttributeStatement(
                name, "BO_", Expect(DbcTokenKind.Number), null, ExpectNumberOrString());
        }
        else if (TryTakeKeyword("SG_"))
        {
            var frameId = Expect(DbcTokenKind.Number);
            attribute = new DbcAttributeStatement(
                name, "SG_", frameId, Expect(DbcTokenKind.Word), ExpectNumberOrString());
        }
        else if (TryTakeKeyword("EV_"))
        {
            attribute = new DbcAttributeStatement(
                name, "EV_", null, Expect(DbcTokenKind.Word), ExpectNumberOrString());
        }
        else
        {
            attribute = new DbcAttributeStatement(name, "", null, null, ExpectNumberOrString());
        }

        Expect(DbcTokenKind.Semicolon);

        return attribute;
    }

    private DbcRelationAttributeStatement ParseRelationAttribute()
    {
        ExpectKeyword("BA_REL_");
        var name = Expect(DbcTokenKind.String);
        DbcRelationAttributeStatement attribute;

        if (TryTakeKeyword("BU_SG_REL_"))
        {
            var nodeName = Expect(DbcTokenKind.Word);
            ExpectKeyword("SG_");
            var frameId = Expect(DbcTokenKind.Number);
            var signalName = Expect(DbcTokenKind.Word);
            attribute = new DbcRelationAttributeStatement(
                name, "BU_SG_REL_", nodeName, frameId, signalName, ExpectNumberOrString());
        }
        else if (TryTakeKeyword("BU_BO_REL_"))
        {
            var nodeName = Expect(DbcTokenKind.Word);
            var frameId = Expect(DbcTokenKind.Number);
            attribute = new DbcRelationAttributeStatement(
                name, "BU_BO_REL_", nodeName, frameId, null, ExpectNumberOrString());
        }
        else
        {
            throw Error(Peek());
        }

        Expect(DbcTokenKind.Semicolon);

        return attribute;
    }

    private DbcChoicesStatement ParseChoices()
    {
        ExpectKeyword("VAL_");
        string? frameId = null;

        if (Peek().Is(DbcTokenKind.Number))
        {
            frameId = Next().Text;
        }

        var name = Expect(DbcTokenKind.Word);
        var pairs = ParseValueTextPairs();
        Expect(DbcTokenKind.Semicolon);

        return new DbcChoicesStatement(frameId, name, pairs);
    }

    private List<(string Value, string Text)> ParseValueTextPairs()
    {
        var pairs = new List<(string, string)>();

        while (Peek().Is(DbcTokenKind.Number))
        {
            var value = Next().Text;
            pairs.Add((value, Expect(DbcTokenKind.String)));
        }

        return pairs;
    }

    private DbcMultiplexerValuesStatement ParseMultiplexerValues()
    {
        ExpectKeyword("SG_MUL_VAL_");
        var frameId = Expect(DbcTokenKind.Number);
        var signalName = Expect(DbcTokenKind.Word);
        var multiplexerSignal = Expect(DbcTokenKind.Word);
        var ranges = new List<(string, string)>();

        // "3-5" tokenizes as the numbers "3" and "-5"; the builder strips the dash.
        ranges.Add((Expect(DbcTokenKind.Number), Expect(DbcTokenKind.Number)));

        while (Peek().Is(DbcTokenKind.Comma))
        {
            _position++;
            ranges.Add((Expect(DbcTokenKind.Number), Expect(DbcTokenKind.Number)));
        }

        Expect(DbcTokenKind.Semicolon);

        return new DbcMultiplexerValuesStatement(frameId, signalName, multiplexerSignal, ranges);
    }

    private DbcSignalGroupStatement ParseSignalGroup()
    {
        ExpectKeyword("SIG_GROUP_");
        var frameId = Expect(DbcTokenKind.Number);
        var name = Expect(DbcTokenKind.Word);
        var repetitions = Expect(DbcTokenKind.Number);
        Expect(DbcTokenKind.Colon);
        var signalNames = new List<string>();

        while (Peek().Is(DbcTokenKind.Word) && !Peek().IsKeyword)
        {
            signalNames.Add(Next().Text);
        }

        Expect(DbcTokenKind.Semicolon);

        return new DbcSignalGroupStatement(frameId, name, repetitions, signalNames);
    }

    private DbcEnvironmentVariableStatement ParseEnvironmentVariable()
    {
        ExpectKeyword("EV_");
        var name = Expect(DbcTokenKind.Word);
        Expect(DbcTokenKind.Colon);
        var envType = Expect(DbcTokenKind.Number);
        Expect(DbcTokenKind.LeftBracket);
        var minimum = Expect(DbcTokenKind.Number);
        Expect(DbcTokenKind.Pipe);
        var maximum = Expect(DbcTokenKind.Number);
        Expect(DbcTokenKind.RightBracket);
        var unit = Expect(DbcTokenKind.String);
        var initialValue = Expect(DbcTokenKind.Number);
        var envId = Expect(DbcTokenKind.Number);
        var accessType = Expect(DbcTokenKind.Word);
        var accessNode = Expect(DbcTokenKind.Word);
        Expect(DbcTokenKind.Semicolon);

        return new DbcEnvironmentVariableStatement(
            name, envType, minimum, maximum, unit, initialValue, envId, accessType, accessNode);
    }
}
