using System.Globalization;
using CanTools.Model;
using Attribute = CanTools.Model.Attribute;

namespace CanTools.Formats.Dbc;

/// <summary>
/// Turns parsed DBC statements into the database model, mirroring the processing
/// order and quirks of upstream's load_string.
/// </summary>
internal sealed class DbcBuilder
{
    // VFrameFormat as defined by Vector: indices not listed here are "reserved".
    private static readonly string[] CanonicalVFrameFormatChoices = BuildVFrameFormatChoices();

    private readonly DbcFileContents _contents;
    private readonly bool _strict;
    private readonly SignalSort? _sortSignals;

    private Dictionary<string, AttributeDefinition> _definitions = null!;
    private Dictionary<string, Dictionary<string, Attribute>> _nodeAttributes = null!;
    private Dictionary<string, Dictionary<string, Attribute>> _envVarAttributes = null!;
    private Dictionary<string, Attribute>? _databaseAttributes;
    private Dictionary<long, FrameAttributes> _frameAttributes = null!;
    private string? _bareComment;
    private Dictionary<string, string> _namedComments = null!;      // node / envvar comments
    private Dictionary<long, string> _messageComments = null!;
    private Dictionary<long, Dictionary<string, string>> _signalComments = null!;

    private sealed class FrameAttributes
    {
        public readonly Dictionary<string, Attribute> Message = [];
        public readonly Dictionary<string, Dictionary<string, Attribute>> Signal = [];
    }

    private DbcBuilder(DbcFileContents contents, bool strict, SignalSort? sortSignals)
    {
        _contents = contents;
        _strict = strict;
        _sortSignals = sortSignals;
    }

    public static (List<Message> Messages, List<Node> Nodes, List<Bus> Buses,
                   string? Version, DbcSpecifics Dbc)
        Build(DbcFileContents contents, bool strict, SignalSort? sortSignals)
    {
        return new DbcBuilder(contents, strict, sortSignals).BuildDatabase();
    }

    private (List<Message>, List<Node>, List<Bus>, string?, DbcSpecifics) BuildDatabase()
    {
        LoadComments();
        _definitions = BuildDefinitions(
            _contents.AttributeDefinitions, _contents.AttributeDefaults, relation: false);
        LoadAttributes();
        var relationDefinitions = BuildDefinitions(
            _contents.RelationAttributeDefinitions, _contents.RelationAttributeDefaults, relation: true);
        var attributesRel = LoadRelationAttributes(relationDefinitions);
        var bus = LoadBus();
        var valueTables = LoadValueTables();
        var choices = LoadChoices();
        var messageSenders = LoadMessageSenders();
        var signalTypes = LoadSignalTypes();
        var (multiplexerValues, signalToMultiplexer) = LoadMultiplexerValues();
        var signalGroups = LoadSignalGroups();

        var messages = LoadMessages(
            bus, choices, messageSenders, signalTypes,
            multiplexerValues, signalToMultiplexer, signalGroups);

        RekeyRelationSignalNames(attributesRel);

        var nodes = LoadNodes();
        var environmentVariables = LoadEnvironmentVariables();

        var dbcSpecifics = new DbcSpecifics(
            _databaseAttributes,
            _definitions,
            environmentVariables,
            valueTables,
            attributesRel,
            relationDefinitions);

        var buses = bus is null ? new List<Bus>() : [bus];

        return (messages, nodes, buses, _contents.Version, dbcSpecifics);
    }

    private void LoadComments()
    {
        _namedComments = [];
        _messageComments = [];
        _signalComments = [];

        foreach (var comment in _contents.Comments)
        {
            switch (comment.Target)
            {
                case "":
                    // CANdb++ concatenates multiple bare comments without separator.
                    _bareComment = (_bareComment ?? "") + comment.Text;
                    break;
                case "BU_":
                case "EV_":
                    _namedComments[comment.Name!] = comment.Text;
                    break;
                case "BO_":
                    _messageComments[ParseLong(comment.FrameId!)] = comment.Text;
                    break;
                case "SG_":
                    var frameId = ParseLong(comment.FrameId!);
                    if (!_signalComments.TryGetValue(frameId, out var perSignal))
                    {
                        _signalComments[frameId] = perSignal = [];
                    }
                    perSignal[comment.Name!] = comment.Text;
                    break;
            }
        }
    }

    private static Dictionary<string, AttributeDefinition> BuildDefinitions(
        List<DbcAttributeDefinitionStatement> statements,
        List<DbcAttributeDefaultStatement> defaultStatements,
        bool relation)
    {
        var defaults = new Dictionary<string, string>();

        foreach (var statement in defaultStatements)
        {
            defaults[statement.Name] = statement.Value;
        }

        var definitions = new Dictionary<string, AttributeDefinition>();

        foreach (var statement in statements)
        {
            SignalValue? defaultValue = null;

            if (defaults.TryGetValue(statement.Name, out var rawDefault))
            {
                defaultValue = ConvertAttributeValue(statement.TypeName, rawDefault);
            }

            double? minimum = null;
            double? maximum = null;
            IReadOnlyList<string> choices = [];

            if (statement.StringValues.Count > 0 && statement.TypeName == "ENUM")
            {
                choices = statement.StringValues;
            }
            else if (statement.NumberValues.Count > 0
                     && statement.TypeName is "INT" or "HEX" or "FLOAT")
            {
                if (statement.TypeName == "FLOAT")
                {
                    minimum = ToFloat(statement.NumberValues[0]);
                    maximum = ToFloat(statement.NumberValues[1]);
                }
                else
                {
                    // Truncate like to_int(), but keep double: INT bounds in the
                    // wild exceed the 64-bit integer range.
                    minimum = Math.Truncate(ToFloat(statement.NumberValues[0]));
                    maximum = Math.Truncate(ToFloat(statement.NumberValues[1]));
                }
            }

            definitions[statement.Name] = new AttributeDefinition(
                statement.Name, defaultValue, statement.Kind, statement.TypeName,
                minimum, maximum, choices);
        }

        return definitions;
    }

    // INT/HEX/ENUM attribute values are integers (ENUM values are indices); FLOAT
    // values are floats; STRING and ENUM *defaults* stay strings.
    private static SignalValue ConvertAttributeValue(string? typeName, string raw) =>
        typeName switch
        {
            "INT" or "HEX" => ToInt(raw),
            "FLOAT" => ToFloat(raw),
            _ => raw,
        };

    private static SignalValue ConvertDefinedValue(AttributeDefinition definition, string raw) =>
        definition.TypeName switch
        {
            "INT" or "HEX" or "ENUM" => ToInt(raw),
            "FLOAT" => ToFloat(raw),
            _ => raw,
        };

    private void LoadAttributes()
    {
        _nodeAttributes = [];
        _envVarAttributes = [];
        _frameAttributes = [];

        foreach (var statement in _contents.Attributes)
        {
            if (!_definitions.TryGetValue(statement.Name, out var definition))
            {
                throw new KeyNotFoundException($"'{statement.Name}'");
            }

            var attribute = new Attribute(
                ConvertDefinedValue(definition, statement.Value), definition);

            switch (statement.Target)
            {
                case "BU_":
                    GetOrAdd(_nodeAttributes, statement.TargetName!)[statement.Name] = attribute;
                    break;
                case "EV_":
                    GetOrAdd(_envVarAttributes, statement.TargetName!)[statement.Name] = attribute;
                    break;
                case "BO_":
                    GetFrame(ParseLong(statement.FrameId!)).Message[statement.Name] = attribute;
                    break;
                case "SG_":
                    var frame = GetFrame(ParseLong(statement.FrameId!));
                    GetOrAdd(frame.Signal, statement.TargetName!)[statement.Name] = attribute;
                    break;
                default:
                    _databaseAttributes ??= [];
                    _databaseAttributes[statement.Name] = attribute;
                    break;
            }
        }
    }

    private Dictionary<long, IReadOnlyList<RelationAttribute>> LoadRelationAttributes(
        Dictionary<string, AttributeDefinition> relationDefinitions)
    {
        var result = new Dictionary<long, IReadOnlyList<RelationAttribute>>();

        foreach (var statement in _contents.RelationAttributes)
        {
            if (!relationDefinitions.TryGetValue(statement.Name, out var definition))
            {
                throw new KeyNotFoundException(
                    $"The relation attribute '{statement.Name}' is used by BA_REL_ "
                    + "but was never defined by BA_DEF_REL_.");
            }

            var attribute = new Attribute(
                ConvertDefinedValue(definition, statement.Value), definition);
            var frameId = ParseLong(statement.FrameId);

            if (!result.TryGetValue(frameId, out var entries))
            {
                result[frameId] = entries = new List<RelationAttribute>();
            }

            ((List<RelationAttribute>)entries).Add(new RelationAttribute(
                statement.NodeName, attribute, statement.SignalName));
        }

        return result;
    }

    private Bus? LoadBus()
    {
        var name = _databaseAttributes?.GetValueOrDefault("DBName")?.Value.Label ?? "";
        int? baudrate = _databaseAttributes?.TryGetValue("Baudrate", out var baudrateAttribute) == true
            ? (int)baudrateAttribute.Value.ToInt64()
            : null;

        if (name == "" && baudrate is null && _bareComment is null)
        {
            return null;
        }

        return new Bus(name, comment: _bareComment is null ? null : new Comments(_bareComment),
                       baudrate: baudrate);
    }

    private Dictionary<string, IReadOnlyDictionary<long, NamedSignalValue>> LoadValueTables()
    {
        var tables = new Dictionary<string, IReadOnlyDictionary<long, NamedSignalValue>>();

        foreach (var statement in _contents.ValueTables)
        {
            tables[statement.Name] = ToNamedValues(statement.Pairs);
        }

        return tables;
    }

    private Dictionary<long, Dictionary<string, Dictionary<long, NamedSignalValue>>> LoadChoices()
    {
        var choices = new Dictionary<long, Dictionary<string, Dictionary<long, NamedSignalValue>>>();

        foreach (var statement in _contents.Choices)
        {
            // The environment-variable form (no frame id) and empty lists are dropped.
            if (statement.FrameId is null || statement.Pairs.Count == 0)
            {
                continue;
            }

            var frameId = ParseLong(statement.FrameId);

            if (!choices.TryGetValue(frameId, out var perSignal))
            {
                choices[frameId] = perSignal = [];
            }

            perSignal[statement.Name] = ToNamedValues(statement.Pairs);
        }

        return choices;
    }

    private static Dictionary<long, NamedSignalValue> ToNamedValues(
        List<(string Value, string Text)> pairs)
    {
        var values = new Dictionary<long, NamedSignalValue>();

        foreach (var (value, text) in pairs)
        {
            var number = ParseLong(value);
            values[number] = new NamedSignalValue(number, text);
        }

        return values;
    }

    private Dictionary<long, List<string>> LoadMessageSenders()
    {
        var senders = new Dictionary<long, List<string>>();

        foreach (var statement in _contents.MessageSenders)
        {
            var frameId = ParseLong(statement.FrameId);

            if (!senders.TryGetValue(frameId, out var list))
            {
                senders[frameId] = list = [];
            }

            list.AddRange(statement.Senders.Select(NodeName));
        }

        return senders;
    }

    private Dictionary<long, Dictionary<string, int>> LoadSignalTypes()
    {
        var types = new Dictionary<long, Dictionary<string, int>>();

        foreach (var statement in _contents.SignalTypes)
        {
            var frameId = ParseLong(statement.FrameId);

            if (!types.TryGetValue(frameId, out var perSignal))
            {
                types[frameId] = perSignal = [];
            }

            perSignal[statement.SignalName] = (int)ParseLong(statement.Type);
        }

        return types;
    }

    private (Dictionary<long, Dictionary<string, Dictionary<string, List<long>>>> Values,
             Dictionary<long, Dictionary<string, string>> SignalToMultiplexer)
        LoadMultiplexerValues()
    {
        var values = new Dictionary<long, Dictionary<string, Dictionary<string, List<long>>>>();
        var signalToMultiplexer = new Dictionary<long, Dictionary<string, string>>();

        foreach (var statement in _contents.MultiplexerValues)
        {
            var frameId = ParseLong(statement.FrameId);
            var ids = new List<long>();

            foreach (var (lower, upper) in statement.Ranges)
            {
                // "3-5" arrives as the numbers "3" and "-5"; strip the dash.
                var from = ParseLong(lower);
                var to = ParseLong(upper[1..]);

                for (var id = from; id <= to; id++)
                {
                    ids.Add(id);
                }
            }

            if (!values.TryGetValue(frameId, out var perMultiplexer))
            {
                values[frameId] = perMultiplexer = [];
            }

            if (!perMultiplexer.TryGetValue(statement.MultiplexerSignal, out var perSignal))
            {
                perMultiplexer[statement.MultiplexerSignal] = perSignal = [];
            }

            perSignal[statement.SignalName] = ids;

            if (!signalToMultiplexer.TryGetValue(frameId, out var mapping))
            {
                signalToMultiplexer[frameId] = mapping = [];
            }

            mapping[statement.SignalName] = statement.MultiplexerSignal;
        }

        return (values, signalToMultiplexer);
    }

    private Dictionary<long, List<SignalGroup>> LoadSignalGroups()
    {
        var groups = new Dictionary<long, List<SignalGroup>>();

        foreach (var statement in _contents.SignalGroups)
        {
            var frameId = ParseLong(statement.FrameId);

            if (!groups.TryGetValue(frameId, out var list))
            {
                groups[frameId] = list = [];
            }

            var signalNames = statement.SignalNames
                .Select(name => SignalName(frameId, name))
                .ToList();

            list.Add(new SignalGroup(
                statement.Name, (int)ParseLong(statement.Repetitions), signalNames));
        }

        return groups;
    }

    private List<Message> LoadMessages(
        Bus? bus,
        Dictionary<long, Dictionary<string, Dictionary<long, NamedSignalValue>>> choices,
        Dictionary<long, List<string>> messageSenders,
        Dictionary<long, Dictionary<string, int>> signalTypes,
        Dictionary<long, Dictionary<string, Dictionary<string, List<long>>>> multiplexerValues,
        Dictionary<long, Dictionary<string, string>> signalToMultiplexer,
        Dictionary<long, List<SignalGroup>> signalGroups)
    {
        var messages = new List<Message>();

        foreach (var statement in _contents.Messages)
        {
            // Pseudo message holding signals not assigned to any real message.
            if (statement.Name == "VECTOR__INDEPENDENT_SIG_MSG")
            {
                continue;
            }

            var frameIdDbc = ParseLong(statement.FrameId);
            var frameId = (uint)(frameIdDbc & 0x7fffffff);
            var isExtendedFrame = (frameIdDbc & 0x80000000) != 0;
            var frame = _frameAttributes.GetValueOrDefault(frameIdDbc);

            var name = frame?.Message.GetValueOrDefault("SystemMessageLongSymbol")?.Value.Label
                       ?? statement.Name;

            var senders = new List<string> { NodeName(statement.Sender) };
            foreach (var sender in messageSenders.GetValueOrDefault(frameIdDbc) ?? [])
            {
                if (!senders.Contains(sender))
                {
                    senders.Add(sender);
                }
            }
            if (senders.Count == 1 && senders[0] == "Vector__XXX")
            {
                senders.Clear();
            }

            // Which signal (if any) is the single multiplexer selector of the frame;
            // with two or more selectors SG_MUL_VAL_ has to disambiguate instead.
            var selectorNames = statement.Signals
                .Where(signal => signal.MuxIndicator?.EndsWith('M') == true)
                .Select(signal => signal.Name)
                .ToList();
            var messageMultiplexer = selectorNames.Count == 1 ? selectorNames[0] : null;

            var signals = statement.Signals
                .Select(signal => BuildSignal(
                    signal, frameIdDbc, frame, messageMultiplexer,
                    choices.GetValueOrDefault(frameIdDbc),
                    signalTypes.GetValueOrDefault(frameIdDbc),
                    multiplexerValues.GetValueOrDefault(frameIdDbc),
                    signalToMultiplexer.GetValueOrDefault(frameIdDbc)))
                .ToList();

            var frameFormat = FrameFormat(frame);
            var messageComment = _messageComments.GetValueOrDefault(frameIdDbc);

            var message = new Message(
                frameId,
                name,
                (int)PythonInt.ParseInt64(statement.Length),
                signals,
                unusedBitPattern: 0xff,
                comment: messageComment is null ? null : new Comments(messageComment),
                senders: senders,
                sendType: SendType(frame),
                cycleTime: CycleTime(frame),
                isExtendedFrame: isExtendedFrame,
                isFd: frameFormat?.EndsWith("CAN_FD") == true,
                busName: bus?.Name,
                signalGroups: signalGroups.GetValueOrDefault(frameIdDbc),
                strict: _strict,
                protocol: frameFormat == "J1939PG" ? "j1939" : null,
                sortSignals: _sortSignals)
            {
                Dbc = new DbcSpecifics(frame?.Message.Count > 0 ? frame.Message : null, _definitions),
            };

            messages.Add(message);
        }

        return messages;
    }

    private Signal BuildSignal(
        DbcSignalStatement statement,
        long frameIdDbc,
        FrameAttributes? frame,
        string? messageMultiplexer,
        Dictionary<string, Dictionary<long, NamedSignalValue>>? frameChoices,
        Dictionary<string, int>? frameSignalTypes,
        Dictionary<string, Dictionary<string, List<long>>>? frameMultiplexerValues,
        Dictionary<string, string>? frameSignalToMultiplexer)
    {
        var shortName = statement.Name;
        var attributes = frame?.Signal.GetValueOrDefault(shortName);
        var name = attributes?.GetValueOrDefault("SystemSignalLongSymbol")?.Value.Label ?? shortName;

        var isFloat = frameSignalTypes?.GetValueOrDefault(shortName) is 1 or 2;
        var signalChoices = frameChoices?.GetValueOrDefault(shortName);

        var conversion = Conversion.Create(
            scale: Num(statement.Scale).ToDouble(),
            offset: Num(statement.Offset).ToDouble(),
            choices: signalChoices,
            isFloat: isFloat);

        double? minimum = null;
        double? maximum = null;

        // The exact text "[0|0]" means "no range"; "[0.0|0.0]" does not.
        if (statement.Minimum != "0" || statement.Maximum != "0")
        {
            minimum = Num(statement.Minimum).ToDouble();
            maximum = Num(statement.Maximum).ToDouble();
        }

        var receivers = statement.Receivers.Count == 1 && statement.Receivers[0] == "Vector__XXX"
            ? []
            : statement.Receivers.Select(NodeName).ToList();

        SignalValue? rawInitial = attributes?.GetValueOrDefault("GenSigStartValue")?.Value;

        long? spn = null;
        if (attributes?.GetValueOrDefault("SPN") is { } spnAttribute)
        {
            spn = spnAttribute.Value.ToInt64();
        }
        else if (_definitions.GetValueOrDefault("SPN")?.DefaultValue is { } spnDefault)
        {
            spn = spnDefault.ToInt64();
        }

        var indicator = statement.MuxIndicator;
        var isMultiplexer = indicator?.EndsWith('M') == true;

        var multiplexerIds = new List<long>();
        string? multiplexerSignal = null;

        if (indicator is not null)
        {
            string? effectiveMultiplexer;

            if (messageMultiplexer is not null)
            {
                effectiveMultiplexer = messageMultiplexer;

                if (indicator.StartsWith('m') && !indicator.EndsWith('M'))
                {
                    multiplexerIds.Add(ParseLong(indicator[1..]));
                }

                multiplexerSignal = shortName == messageMultiplexer ? null : messageMultiplexer;
            }
            else
            {
                effectiveMultiplexer = frameSignalToMultiplexer?.GetValueOrDefault(shortName);
                multiplexerSignal = effectiveMultiplexer;
            }

            if (effectiveMultiplexer is not null
                && frameMultiplexerValues?.GetValueOrDefault(effectiveMultiplexer)
                    ?.GetValueOrDefault(shortName) is { } extendedIds)
            {
                multiplexerIds.AddRange(extendedIds);
            }
        }

        var comment = _signalComments.GetValueOrDefault(frameIdDbc)?.GetValueOrDefault(shortName);

        return new Signal(
            name,
            start: (int)ParseLong(statement.Start),
            length: (int)ParseLong(statement.Length),
            byteOrder: statement.ByteOrder == "0" ? ByteOrder.BigEndian : ByteOrder.LittleEndian,
            isSigned: statement.Sign == "-",
            rawInitial: rawInitial,
            conversion: conversion,
            minimum: minimum,
            maximum: maximum,
            unit: statement.Unit == "" ? null : statement.Unit,
            comment: comment is null ? null : new Comments(comment),
            receivers: receivers,
            isMultiplexer: isMultiplexer,
            multiplexerIds: multiplexerIds.Count > 0
                ? multiplexerIds.Distinct().Order().ToList()
                : null,
            multiplexerSignal: multiplexerSignal,
            spn: spn)
        {
            Dbc = new DbcSpecifics(attributes ?? [], _definitions),
        };
    }

    private int? CycleTime(FrameAttributes? frame)
    {
        if (!_definitions.TryGetValue("GenMsgCycleTime", out var definition))
        {
            return null;
        }

        // A cycle time of 0 means "none", both as value and as default.
        if (frame?.Message.GetValueOrDefault("GenMsgCycleTime") is { } attribute)
        {
            var value = (int)attribute.Value.ToInt64();

            return value == 0 ? null : value;
        }

        if (definition.DefaultValue is { } defaultValue)
        {
            var value = (int)defaultValue.ToInt64();

            return value == 0 ? null : value;
        }

        return null;
    }

    private string? SendType(FrameAttributes? frame)
    {
        if (!_definitions.TryGetValue("GenMsgSendType", out var definition))
        {
            return null;
        }

        if (frame?.Message.GetValueOrDefault("GenMsgSendType") is { } attribute)
        {
            return definition.Choices.Count > 0
                ? definition.Choices[(int)attribute.Value.ToInt64()]
                : attribute.Value.Label;
        }

        return definition.DefaultValue?.Label;
    }

    private string? FrameFormat(FrameAttributes? frame)
    {
        if (!_definitions.TryGetValue("VFrameFormat", out var definition))
        {
            return null;
        }

        var choices = definition.Choices;
        string? defaultValue = definition.DefaultValue?.Label;

        if (definition.TypeName == "INT")
        {
            // Some tools define VFrameFormat as INT; substitute the canonical enum.
            choices = CanonicalVFrameFormatChoices;
            defaultValue = CanonicalVFrameFormatChoices[definition.DefaultValue!.Value.ToInt64()];
        }

        if (frame?.Message.GetValueOrDefault("VFrameFormat") is { } attribute)
        {
            return choices[(int)attribute.Value.ToInt64()];
        }

        return defaultValue;
    }

    private void RekeyRelationSignalNames(
        Dictionary<long, IReadOnlyList<RelationAttribute>> attributesRel)
    {
        foreach (var frameId in attributesRel.Keys.ToList())
        {
            attributesRel[frameId] = attributesRel[frameId]
                .Select(entry =>
                {
                    var longName = entry.SignalName is null
                        ? null
                        : _frameAttributes.GetValueOrDefault(frameId)
                            ?.Signal.GetValueOrDefault(entry.SignalName)
                            ?.GetValueOrDefault("SystemSignalLongSymbol")?.Value.Label;

                    return longName is null
                        ? entry
                        : new RelationAttribute(entry.NodeName, entry.Attribute, longName);
                })
                .ToList();
        }
    }

    private List<Node> LoadNodes()
    {
        var nodes = new List<Node>();

        foreach (var shortName in _contents.Nodes ?? [])
        {
            var comment = _namedComments.GetValueOrDefault(shortName);
            var attributes = _nodeAttributes.GetValueOrDefault(shortName);

            nodes.Add(new Node(
                NodeName(shortName),
                comment is null ? null : new Comments(comment))
            {
                Dbc = new DbcSpecifics(attributes, _definitions),
            });
        }

        return nodes;
    }

    private Dictionary<string, EnvironmentVariable> LoadEnvironmentVariables()
    {
        var variables = new Dictionary<string, EnvironmentVariable>();

        foreach (var statement in _contents.EnvironmentVariables)
        {
            var attributes = _envVarAttributes.GetValueOrDefault(statement.Name);
            var name = attributes?.GetValueOrDefault("SystemEnvVarLongSymbol")?.Value.Label
                       ?? statement.Name;

            variables[name] = new EnvironmentVariable(
                name,
                envType: (int)ParseLong(statement.EnvType),
                minimum: Num(statement.Minimum).ToDouble(),
                maximum: Num(statement.Maximum).ToDouble(),
                unit: statement.Unit,
                initialValue: Num(statement.InitialValue).ToDouble(),
                envId: (int)ParseLong(statement.EnvId),
                accessType: statement.AccessType,
                accessNode: statement.AccessNode,
                comment: _namedComments.GetValueOrDefault(statement.Name))
            {
                Dbc = new DbcSpecifics(attributes, _definitions),
            };
        }

        return variables;
    }

    private string SignalName(long frameIdDbc, string shortName) =>
        _frameAttributes.GetValueOrDefault(frameIdDbc)
            ?.Signal.GetValueOrDefault(shortName)
            ?.GetValueOrDefault("SystemSignalLongSymbol")?.Value.Label
        ?? shortName;

    private string NodeName(string shortName) =>
        _nodeAttributes.GetValueOrDefault(shortName)
            ?.GetValueOrDefault("SystemNodeLongSymbol")?.Value.Label
        ?? shortName;

    private FrameAttributes GetFrame(long frameId)
    {
        if (!_frameAttributes.TryGetValue(frameId, out var frame))
        {
            _frameAttributes[frameId] = frame = new FrameAttributes();
        }

        return frame;
    }

    private static Dictionary<string, Attribute> GetOrAdd(
        Dictionary<string, Dictionary<string, Attribute>> store, string key)
    {
        if (!store.TryGetValue(key, out var attributes))
        {
            store[key] = attributes = [];
        }

        return attributes;
    }

    private static string[] BuildVFrameFormatChoices()
    {
        var choices = new string[16];
        Array.Fill(choices, "reserved");
        choices[0] = "StandardCAN";
        choices[1] = "ExtendedCAN";
        choices[3] = "J1939PG";
        choices[14] = "StandardCAN_FD";
        choices[15] = "ExtendedCAN_FD";

        return choices;
    }

    private static long ParseLong(string text) => long.Parse(text, CultureInfo.InvariantCulture);

    // Like upstream's num(): an int when possible, otherwise a float.
    private static SignalValue Num(string text)
    {
        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var integer))
        {
            return integer;
        }

        return double.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
    }

    // Like upstream's to_int(): parse as decimal and truncate.
    private static long ToInt(string text) =>
        (long)decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static double ToFloat(string text) =>
        (double)decimal.Parse(text, NumberStyles.Float, CultureInfo.InvariantCulture);
}
