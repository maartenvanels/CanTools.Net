using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CanTools.Model;
using Attribute = CanTools.Model.Attribute;

namespace CanTools.Formats.Dbc;

/// <summary>
/// Writes a <see cref="Database"/> as DBC text, the port of upstream's dump_string.
/// Instead of mutating a deep copy like upstream, a non-destructive dump plan holds
/// the sanitized/shortened names and the synthesized attribute definitions.
/// </summary>
public static partial class DbcWriter
{
    private static readonly AttributeDefinition GenMsgCycleTimeDefinition =
        new("GenMsgCycleTime", 0L, "BO_", "INT", 0, 65535);

    private static readonly AttributeDefinition GenSigStartValueDefinition =
        new("GenSigStartValue", 0.0, "SG_", "FLOAT", 0, 100000000000);

    private static readonly AttributeDefinition BaudrateDefinition =
        new("Baudrate", 125000L, null, "INT", 0, 10485760);

    private static readonly AttributeDefinition VFrameFormatDefinition =
        new("VFrameFormat", "StandardCAN", "BO_", "ENUM", choices:
        [
            "StandardCAN", "ExtendedCAN", "reserved", "J1939PG",
            "reserved", "reserved", "reserved", "reserved", "reserved",
            "reserved", "reserved", "reserved", "reserved", "reserved",
            "StandardCAN_FD", "ExtendedCAN_FD",
        ]);

    private static readonly AttributeDefinition CanFdBrsDefinition =
        new("CANFD_BRS", "1", "BO_", "ENUM", choices: ["0", "1"]);

    [GeneratedRegex(@"\W")]
    private static partial Regex NonWordCharacters();

    public static string Dump(
        Database database,
        SignalSort? sortSignals = null,
        SignalSort? sortAttributeSignals = null,
        bool shortenLongNames = true)
    {
        // Omitting sortSignals means "by start bit, reversed" — unless the database
        // itself was created with SignalSorts.None, which keeps declaration order.
        sortSignals ??= SignalSorts.KeepsDeclarationOrder(database.SortSignalsOption)
            ? SignalSorts.None
            : SignalSorts.ByStartBitReversed;
        sortAttributeSignals ??= SignalSorts.ByStartBitReversed;

        var plan = new DumpPlan(database, shortenLongNames);
        var builder = new StringBuilder();

        AppendHeader(builder, database.Version);
        builder.Append("BU_: ").Append(string.Join(' ', database.Nodes.Select(n => plan.NodeName(n.Name))));
        builder.Append("\r\n");
        builder.Append(DumpValueTables(database));
        builder.Append("\r\n\r\n");
        builder.Append(string.Join("\r\n\r\n", DumpMessages(database, plan, sortSignals)));
        builder.Append("\r\n\r\n");
        builder.Append(string.Join("\r\n", DumpSenders(database, plan)));
        builder.Append("\r\n");
        builder.Append(string.Join("\r\n\r\n", DumpEnvironmentVariables(database, plan)));
        builder.Append("\r\n\r\n");
        builder.Append(string.Join("\r\n", DumpComments(database, plan, sortAttributeSignals)));
        builder.Append("\r\n");
        builder.Append(string.Join("\r\n", DumpAttributeDefinitions(plan)));
        builder.Append("\r\n");
        foreach (var line in DumpRelationAttributeDefinitions(database))
        {
            builder.Append(line).Append("\r\n");
        }
        builder.Append(string.Join("\r\n", DumpAttributeDefaults(plan)));
        builder.Append("\r\n");
        foreach (var line in DumpRelationAttributeDefaults(database))
        {
            builder.Append(line).Append("\r\n");
        }
        builder.Append(string.Join("\r\n", DumpAttributes(database, plan, sortAttributeSignals)));
        builder.Append("\r\n");
        foreach (var line in DumpRelationAttributes(database, plan))
        {
            builder.Append(line).Append("\r\n");
        }
        builder.Append(string.Join("\r\n", DumpChoices(database, plan, sortAttributeSignals)));
        builder.Append("\r\n");
        builder.Append(string.Join("\r\n", DumpSignalTypes(database, plan)));
        builder.Append("\r\n");
        builder.Append(string.Join("\r\n", DumpSignalGroups(database, plan)));
        builder.Append("\r\n");
        builder.Append(string.Join("\r\n", DumpMultiplexerValues(database, plan)));
        builder.Append("\r\n");

        return builder.ToString();
    }

    private static void AppendHeader(StringBuilder builder, string? version)
    {
        builder.Append($"VERSION \"{version ?? ""}\"\r\n\r\n\r\n");
        builder.Append("NS_ : \r\n");

        foreach (var symbol in new[]
                 {
                     "NS_DESC_", "CM_", "BA_DEF_", "BA_", "VAL_", "CAT_DEF_", "CAT_",
                     "FILTER", "BA_DEF_DEF_", "EV_DATA_", "ENVVAR_DATA_", "SGTYPE_",
                     "SGTYPE_VAL_", "BA_DEF_SGTYPE_", "BA_SGTYPE_", "SIG_TYPE_REF_",
                     "VAL_TABLE_", "SIG_GROUP_", "SIG_VALTYPE_", "SIGTYPE_VALTYPE_",
                     "BO_TX_BU_", "BA_DEF_REL_", "BA_REL_", "BA_DEF_DEF_REL_",
                     "BU_SG_REL_", "BU_EV_REL_", "BU_BO_REL_", "SG_MUL_VAL_",
                 })
        {
            builder.Append('\t').Append(symbol).Append("\r\n");
        }

        builder.Append("\r\nBS_:\r\n\r\n");
    }

    private static string DumpValueTables(Database database)
    {
        var lines = new List<string>();

        foreach (var (name, choices) in database.Dbc?.ValueTables
                 ?? new Dictionary<string, IReadOnlyDictionary<long, NamedSignalValue>>())
        {
            var pairs = string.Join(' ', choices
                .OrderByDescending(entry => entry.Key)
                .Select(entry => $"{entry.Key} \"{entry.Value.Name}\""));

            lines.Add($"VAL_TABLE_ {name} {pairs} ;");
        }

        return lines.Count == 0 ? "" : string.Join("\r\n", lines) + "\r\n";
    }

    private static List<string> DumpMessages(Database database, DumpPlan plan, SignalSort sortSignals)
    {
        var messages = new List<string>();

        foreach (var message in database.Messages)
        {
            var lines = new List<string>
            {
                $"BO_ {DbcFrameId(message)} {plan.MessageName(message)}: "
                + $"{message.Length} {SenderOrDefault(message, plan)}",
            };

            foreach (var signal in sortSignals(message.Signals))
            {
                var mux = signal.IsMultiplexer
                    ? " M"
                    : signal.MultiplexerIds is { Count: > 0 } ids ? $" m{ids[0]}" : "";
                var receivers = signal.Receivers.Count > 0
                    ? " " + string.Join(',', signal.Receivers.Select(plan.NodeName))
                    : "Vector__XXX";

                lines.Add(
                    $" SG_ {plan.SignalName(message, signal)}{mux} : "
                    + $"{signal.StartBit}|{signal.Length}@"
                    + $"{(signal.ByteOrder == ByteOrder.BigEndian ? 0 : 1)}"
                    + $"{(signal.IsSigned ? '-' : '+')}"
                    + $" ({Number(signal.Scale)},{Number(signal.Offset)})"
                    + $" [{Number(signal.Minimum ?? 0)}|{Number(signal.Maximum ?? 0)}]"
                    + $" \"{signal.Unit ?? ""}\" {receivers}");
            }

            messages.Add(string.Join("\r\n", lines));
        }

        return messages;
    }

    private static List<string> DumpSenders(Database database, DumpPlan plan) =>
        database.Messages
            .Where(message => message.Senders.Count > 1)
            .Select(message =>
                $"BO_TX_BU_ {DbcFrameId(message)} : "
                + string.Join(',', message.Senders.Select(plan.NodeName)) + ";")
            .ToList();

    private static List<string> DumpEnvironmentVariables(Database database, DumpPlan plan)
    {
        var lines = new List<string>();

        foreach (var variable in (database.Dbc?.EnvironmentVariables
                 ?? new Dictionary<string, EnvironmentVariable>()).Values)
        {
            lines.Add(
                $"EV_ {plan.EnvironmentVariableName(variable)}: {variable.EnvType} "
                + $"[{Number(variable.Minimum)}|{Number(variable.Maximum)}] "
                + $"\"{Escape(variable.Unit)}\" {Number(variable.InitialValue)} "
                + $"{variable.EnvId} {variable.AccessType} {variable.AccessNode};");
        }

        return lines;
    }

    private static List<string> DumpComments(
        Database database, DumpPlan plan, SignalSort sortAttributeSignals)
    {
        var lines = new List<string>();

        foreach (var bus in database.Buses)
        {
            if (bus.Comment is { } busComment)
            {
                lines.Add($"CM_ \"{busComment}\";");
            }
        }

        foreach (var node in database.Nodes)
        {
            if (node.Comment is { } nodeComment)
            {
                lines.Add($"CM_ BU_ {plan.NodeName(node.Name)} \"{Escape(nodeComment)}\";");
            }
        }

        foreach (var message in database.Messages)
        {
            if (message.Comment is { } messageComment)
            {
                lines.Add($"CM_ BO_ {DbcFrameId(message)} \"{Escape(messageComment)}\";");
            }

            foreach (var signal in sortAttributeSignals(message.Signals))
            {
                if (signal.Comment is { } signalComment)
                {
                    lines.Add(
                        $"CM_ SG_ {DbcFrameId(message)} {plan.SignalName(message, signal)} "
                        + $"\"{Escape(signalComment)}\";");
                }
            }
        }

        foreach (var variable in (database.Dbc?.EnvironmentVariables
                 ?? new Dictionary<string, EnvironmentVariable>()).Values)
        {
            if (variable.Comment is { } comment)
            {
                lines.Add($"CM_ EV_ {plan.EnvironmentVariableName(variable)} \"{Escape(comment)}\";");
            }
        }

        return lines;
    }

    private static List<string> DumpAttributeDefinitions(DumpPlan plan) =>
        plan.Definitions.Values
            .Select(definition => FormatDefinition(definition, "BA_DEF_ "))
            .OfType<string>()
            .ToList();

    private static List<string> DumpRelationAttributeDefinitions(Database database) =>
        (database.Dbc?.AttributeDefinitionsRel ?? new Dictionary<string, AttributeDefinition>())
        .Values
        .Select(definition => FormatDefinition(definition, "BA_DEF_REL_ ", relation: true))
        .OfType<string>()
        .ToList();

    private static string? FormatDefinition(
        AttributeDefinition definition, string keyword, bool relation = false)
    {
        var kind = definition.Kind is null
            ? relation ? " " : ""
            : definition.Kind + (relation ? "  " : " ");
        var prefix = $"{keyword}{kind} \"{definition.Name}\"";

        switch (definition.TypeName)
        {
            case "ENUM":
                var choices = string.Join(',', definition.Choices.Select(choice => $"\"{choice}\""));
                return $"{prefix} ENUM  {choices};";
            case "INT" or "HEX":
                return $"{prefix} {definition.TypeName}{Bounds(definition, integral: true)};";
            case "FLOAT":
                return $"{prefix} FLOAT{Bounds(definition, integral: false)};";
            case "STRING":
                return $"{prefix} STRING ;";
            default:
                return null;
        }
    }

    private static string Bounds(AttributeDefinition definition, bool integral)
    {
        // Like upstream, both bounds are suppressed when the minimum is unset.
        if (definition.Minimum is not { } minimum)
        {
            return "";
        }

        var maximum = definition.Maximum is { } value
            ? integral ? Number(Math.Truncate(value)) : Number(value)
            : "None";

        return $" {(integral ? Number(Math.Truncate(minimum)) : Number(minimum))} {maximum}";
    }

    private static List<string> DumpAttributeDefaults(DumpPlan plan) =>
        plan.Definitions.Values
            .Where(definition => definition.DefaultValue is not null)
            .Select(definition => $"BA_DEF_DEF_  \"{definition.Name}\" {DefaultText(definition)};")
            .ToList();

    private static List<string> DumpRelationAttributeDefaults(Database database) =>
        (database.Dbc?.AttributeDefinitionsRel ?? new Dictionary<string, AttributeDefinition>())
        .Values
        .Where(definition => definition.DefaultValue is not null)
        .Select(definition => $"BA_DEF_DEF_REL_ \"{definition.Name}\" {DefaultText(definition)};")
        .ToList();

    private static string DefaultText(AttributeDefinition definition) =>
        definition.TypeName is "STRING" or "ENUM"
            ? $"\"{definition.DefaultValue!.Value.Label}\""
            : Value(definition.DefaultValue!.Value);

    private static List<string> DumpAttributes(
        Database database, DumpPlan plan, SignalSort sortAttributeSignals)
    {
        var lines = new List<string>();

        // Database level, with the Baudrate attribute regenerated from the bus.
        foreach (var attribute in (database.Dbc?.Attributes ?? new Dictionary<string, Attribute>()).Values)
        {
            if (attribute.Name != "Baudrate")
            {
                lines.Add($"BA_ \"{attribute.Name}\" {AttributeText(attribute)};");
            }
        }

        if (database.Buses.Count == 1 && database.Buses[0].Baudrate is { } baudrate)
        {
            lines.Add($"BA_ \"Baudrate\" {baudrate};");
        }

        foreach (var variable in (database.Dbc?.EnvironmentVariables
                 ?? new Dictionary<string, EnvironmentVariable>()).Values)
        {
            var name = plan.EnvironmentVariableName(variable);

            foreach (var attribute in (variable.Dbc?.Attributes ?? new Dictionary<string, Attribute>()).Values)
            {
                if (attribute.Name != "SystemEnvVarLongSymbol")
                {
                    lines.Add($"BA_ \"{attribute.Name}\" EV_ {name} {AttributeText(attribute)};");
                }
            }

            if (name != variable.Name)
            {
                lines.Add($"BA_ \"SystemEnvVarLongSymbol\" EV_ {name} \"{variable.Name}\";");
            }
        }

        foreach (var node in database.Nodes)
        {
            var name = plan.NodeName(node.Name);

            foreach (var attribute in (node.Dbc?.Attributes ?? new Dictionary<string, Attribute>()).Values)
            {
                if (attribute.Name != "SystemNodeLongSymbol")
                {
                    lines.Add($"BA_ \"{attribute.Name}\" BU_ {name} {AttributeText(attribute)};");
                }
            }

            if (plan.NodeLongSymbol(node.Name) is { } longNodeName)
            {
                lines.Add($"BA_ \"SystemNodeLongSymbol\" BU_ {name} \"{longNodeName}\";");
            }
        }

        foreach (var message in database.Messages)
        {
            var frameId = DbcFrameId(message);

            // A synthesized VFrameFormat value replaces an existing attribute, but an
            // existing attribute is never removed when synthesis does not apply.
            long? formatIndex = plan.Definitions.TryGetValue("VFrameFormat", out var formatDefinition)
                ? VFrameFormatIndex(message, formatDefinition)
                : null;
            var formatEmitted = false;

            foreach (var attribute in (message.Dbc?.Attributes ?? new Dictionary<string, Attribute>()).Values)
            {
                if (attribute.Name is "SystemMessageLongSymbol" or "GenMsgCycleTime")
                {
                    continue;
                }

                if (attribute.Name == "VFrameFormat" && formatIndex is not null)
                {
                    lines.Add($"BA_ \"VFrameFormat\" BO_ {frameId} {formatIndex};");
                    formatEmitted = true;
                    continue;
                }

                lines.Add($"BA_ \"{attribute.Name}\" BO_ {frameId} {AttributeText(attribute)};");
            }

            // GenMsgCycleTime is regenerated from the model field (or dropped when it
            // equals the default).
            var cycleTime = message.CycleTime ?? 0;
            if (plan.Definitions.TryGetValue("GenMsgCycleTime", out var cycleDefinition)
                && cycleDefinition.DefaultValue != (SignalValue)cycleTime)
            {
                lines.Add($"BA_ \"GenMsgCycleTime\" BO_ {frameId} {cycleTime};");
            }

            if (formatIndex is not null && !formatEmitted)
            {
                lines.Add($"BA_ \"VFrameFormat\" BO_ {frameId} {formatIndex};");
            }

            if (plan.MessageLongSymbol(message) is { } longMessageName)
            {
                lines.Add($"BA_ \"SystemMessageLongSymbol\" BO_ {frameId} \"{longMessageName}\";");
            }

            foreach (var signal in sortAttributeSignals(message.Signals))
            {
                var signalName = plan.SignalName(message, signal);

                foreach (var attribute in (signal.Dbc?.Attributes ?? new Dictionary<string, Attribute>()).Values)
                {
                    if (attribute.Name is not ("SystemSignalLongSymbol" or "GenSigStartValue"))
                    {
                        lines.Add($"BA_ \"{attribute.Name}\" SG_ {frameId} {signalName} {AttributeText(attribute)};");
                    }
                }

                if (signal.RawInitial is { } rawInitial)
                {
                    lines.Add($"BA_ \"GenSigStartValue\" SG_ {frameId} {signalName} {Value(rawInitial)};");
                }

                if (plan.SignalLongSymbol(message, signal) is { } longSignalName)
                {
                    lines.Add($"BA_ \"SystemSignalLongSymbol\" SG_ {frameId} {signalName} \"{longSignalName}\";");
                }
            }
        }

        return lines;
    }

    private static long? VFrameFormatIndex(Message message, AttributeDefinition definition)
    {
        var choices = definition.Choices;
        var defaultValue = definition.DefaultValue?.Label;

        if (definition.TypeName == "INT")
        {
            choices = VFrameFormatDefinition.Choices;
            defaultValue = choices[(int)definition.DefaultValue!.Value.ToInt64()];
        }

        var frameFormat = message.Protocol == "j1939" ? "J1939PG"
            : message.IsFd && message.IsExtendedFrame ? "ExtendedCAN_FD"
            : message.IsFd ? "StandardCAN_FD"
            : message.IsExtendedFrame ? "ExtendedCAN"
            : "StandardCAN";

        var index = choices.ToList().IndexOf(frameFormat);

        return index >= 0 && frameFormat != defaultValue ? index : null;
    }

    private static List<string> DumpRelationAttributes(Database database, DumpPlan plan)
    {
        var lines = new List<string>();

        foreach (var (frameId, entries) in database.Dbc?.AttributesRel
                 ?? new Dictionary<long, IReadOnlyList<RelationAttribute>>())
        {
            // Like upstream, signal relations suppress node relations of the same frame.
            var hasSignalRelations = entries.Any(entry => entry.SignalName is not null);

            foreach (var entry in entries)
            {
                if (hasSignalRelations != entry.SignalName is not null)
                {
                    continue;
                }

                lines.Add(entry.SignalName is { } signalName
                    ? $"BA_REL_ \"{entry.Attribute.Name}\" BU_SG_REL_ {entry.NodeName} "
                      + $"SG_ {frameId} {plan.SignalNameByFrameId(database, frameId, signalName)} "
                      + $"{AttributeText(entry.Attribute)};"
                    : $"BA_REL_ \"{entry.Attribute.Name}\" BU_BO_REL_ {entry.NodeName} "
                      + $"{frameId} {AttributeText(entry.Attribute)};");
            }
        }

        return lines;
    }

    private static List<string> DumpChoices(
        Database database, DumpPlan plan, SignalSort sortAttributeSignals)
    {
        var lines = new List<string>();

        foreach (var message in database.Messages)
        {
            foreach (var signal in sortAttributeSignals(message.Signals))
            {
                if (signal.Choices is not { } choices)
                {
                    continue;
                }

                var pairs = string.Join(' ', choices
                    .Select(entry => $"{entry.Key} \"{entry.Value.Name}\""));

                lines.Add(
                    $"VAL_ {DbcFrameId(message)} {plan.SignalName(message, signal)} {pairs} ;");
            }
        }

        return lines;
    }

    private static List<string> DumpSignalTypes(Database database, DumpPlan plan)
    {
        var lines = new List<string>();

        foreach (var message in database.Messages)
        {
            foreach (var signal in message.Signals)
            {
                if (!signal.IsFloat)
                {
                    continue;
                }

                var type = signal.Length switch
                {
                    32 => 1,
                    64 => 2,
                    _ => throw new CanToolsException(
                        $"Signal '{signal.Name}' is a float of {signal.Length} bits; "
                        + "only 32 and 64 can be written to DBC."),
                };

                lines.Add(
                    $"SIG_VALTYPE_ {DbcFrameId(message)} {plan.SignalName(message, signal)} : {type};");
            }
        }

        return lines;
    }

    private static List<string> DumpSignalGroups(Database database, DumpPlan plan)
    {
        var lines = new List<string>();

        foreach (var message in database.Messages)
        {
            foreach (var group in message.SignalGroups ?? [])
            {
                // Only reference signals that actually exist in the message.
                var memberNames = group.SignalNames
                    .Select(name => plan.SignalNameByOriginal(message, name))
                    .OfType<string>()
                    .ToList();

                lines.Add(
                    $"SIG_GROUP_ {DbcFrameId(message)} {plan.SanitizedName(group.Name)} "
                    + $"{group.Repetitions} : {string.Join(' ', memberNames)};");
            }
        }

        return lines;
    }

    private static List<string> DumpMultiplexerValues(Database database, DumpPlan plan)
    {
        var extendedMuxNeeded = database.Messages.Any(message =>
            message.Signals.Count(signal => signal.IsMultiplexer) > 1
            || message.Signals.Any(signal => signal.MultiplexerIds is { Count: > 1 }));

        if (!extendedMuxNeeded)
        {
            return [];
        }

        var lines = new List<string>();

        foreach (var message in database.Messages)
        {
            foreach (var signal in message.Signals)
            {
                if (signal.MultiplexerIds is not { Count: > 0 } ids
                    || signal.MultiplexerSignal is not { } multiplexerSignal)
                {
                    continue;
                }

                var ranges = string.Join(", ", CoalesceRanges(ids));
                var selectorName = plan.SignalNameByOriginal(message, multiplexerSignal)
                    ?? multiplexerSignal;

                lines.Add(
                    $"SG_MUL_VAL_ {DbcFrameId(message)} {plan.SignalName(message, signal)} "
                    + $"{selectorName} {ranges};");
            }
        }

        return lines;
    }

    private static IEnumerable<string> CoalesceRanges(IReadOnlyList<long> ids)
    {
        var sorted = ids.Order().ToList();
        var start = sorted[0];
        var previous = sorted[0];

        foreach (var id in sorted.Skip(1))
        {
            if (id != previous + 1)
            {
                yield return $"{start}-{previous}";
                start = id;
            }

            previous = id;
        }

        yield return $"{start}-{previous}";
    }

    private static string SenderOrDefault(Message message, DumpPlan plan) =>
        message.Senders.Count > 0 ? plan.NodeName(message.Senders[0]) : "Vector__XXX";

    private static uint DbcFrameId(Message message) =>
        message.IsExtendedFrame ? message.FrameId | 0x80000000 : message.FrameId;

    private static string Escape(string text) => text.Replace("\"", "\\\"");

    private static string AttributeText(Attribute attribute) =>
        attribute.Definition.TypeName == "STRING"
            ? $"\"{attribute.Value.Label}\""
            : Value(attribute.Value);

    private static string Value(SignalValue value) =>
        value.IsLabel ? value.Label! : Number(value.ToDouble(), value.IsInteger);

    // Python str() semantics: integers plain, floats shortest-roundtrip with at
    // least one decimal and a lowercase exponent.
    private static string Number(double value, bool isInteger = false)
    {
        if (isInteger || (double.IsInteger(value) && Math.Abs(value) < 1e15))
        {
            return ((long)value).ToString(CultureInfo.InvariantCulture);
        }

        var text = value.ToString("R", CultureInfo.InvariantCulture).Replace("E", "e");

        if (!text.Contains('.') && !text.Contains('e'))
        {
            text += ".0";
        }

        return text;
    }

    /// <summary>
    /// The names and definitions this dump will use: sanitized (non-word characters
    /// replaced), made unique, optionally shortened to 32 characters, plus the
    /// synthesized attribute definitions.
    /// </summary>
    private sealed class DumpPlan
    {
        private readonly Dictionary<string, string> _nodeNames = [];
        private readonly Dictionary<string, string> _nodeLongSymbols = [];
        private readonly Dictionary<Message, string> _messageNames = [];
        private readonly Dictionary<Message, string> _messageLongSymbols = [];
        private readonly Dictionary<Message, Dictionary<Signal, string>> _signalNames = [];
        private readonly Dictionary<Message, Dictionary<Signal, string>> _signalLongSymbols = [];
        private readonly Dictionary<EnvironmentVariable, string> _environmentVariableNames = [];

        public DumpPlan(Database database, bool shorten)
        {
            var nodeConverter = new LongNamesConverter(
                database.Nodes.Select(node => Sanitize(node.Name)));

            foreach (var node in database.Nodes)
            {
                var sanitized = Sanitize(node.Name);
                var finalName = shorten ? nodeConverter.Shorten(sanitized) : sanitized;
                _nodeNames[node.Name] = finalName;

                if (finalName != sanitized)
                {
                    _nodeLongSymbols[node.Name] = sanitized;
                }
            }

            var messageConverter = new LongNamesConverter(
                database.Messages.Select(message => Sanitize(message.Name)));

            foreach (var message in database.Messages)
            {
                var sanitized = Sanitize(message.Name);
                var finalName = shorten ? messageConverter.Shorten(sanitized) : sanitized;
                _messageNames[message] = finalName;

                if (finalName != sanitized)
                {
                    _messageLongSymbols[message] = sanitized;
                }

                var signalConverter = new LongNamesConverter(
                    message.Signals.Select(signal => Sanitize(signal.Name)));
                var names = new Dictionary<Signal, string>();
                var longSymbols = new Dictionary<Signal, string>();

                foreach (var signal in message.Signals)
                {
                    var sanitizedSignal = Sanitize(signal.Name);
                    var finalSignal = shorten ? signalConverter.Shorten(sanitizedSignal) : sanitizedSignal;
                    names[signal] = finalSignal;

                    if (finalSignal != sanitizedSignal)
                    {
                        longSymbols[signal] = sanitizedSignal;
                    }
                }

                _signalNames[message] = names;
                _signalLongSymbols[message] = longSymbols;
            }

            var variables = (database.Dbc?.EnvironmentVariables
                ?? new Dictionary<string, EnvironmentVariable>()).Values.ToList();
            var variableConverter = new LongNamesConverter(variables.Select(v => v.Name));

            foreach (var variable in variables)
            {
                _environmentVariableNames[variable] =
                    shorten ? variableConverter.Shorten(variable.Name) : variable.Name;
            }

            Definitions = BuildDefinitions(database);
        }

        public Dictionary<string, AttributeDefinition> Definitions { get; }

        public string NodeName(string originalName) =>
            _nodeNames.GetValueOrDefault(originalName, originalName);

        public string? NodeLongSymbol(string originalName) =>
            _nodeLongSymbols.GetValueOrDefault(originalName);

        public string MessageName(Message message) => _messageNames[message];

        public string? MessageLongSymbol(Message message) =>
            _messageLongSymbols.GetValueOrDefault(message);

        public string SignalName(Message message, Signal signal) => _signalNames[message][signal];

        public string? SignalLongSymbol(Message message, Signal signal) =>
            _signalLongSymbols[message].GetValueOrDefault(signal);

        public string? SignalNameByOriginal(Message message, string originalName)
        {
            var signal = message.Signals.FirstOrDefault(s => s.Name == originalName);

            return signal is null ? null : _signalNames[message][signal];
        }

        public string SignalNameByFrameId(Database database, long dbcFrameId, string originalName)
        {
            var message = database.Messages.FirstOrDefault(m => DbcFrameId(m) == dbcFrameId);

            return message is null
                ? originalName
                : SignalNameByOriginal(message, originalName) ?? originalName;
        }

        public string EnvironmentVariableName(EnvironmentVariable variable) =>
            _environmentVariableNames[variable];

        public string SanitizedName(string name) => Sanitize(name);

        private Dictionary<string, AttributeDefinition> BuildDefinitions(Database database)
        {
            var definitions = new Dictionary<string, AttributeDefinition>(
                database.Dbc?.AttributeDefinitions ?? new Dictionary<string, AttributeDefinition>());

            if (!definitions.ContainsKey("GenMsgCycleTime")
                && database.Messages.Any(message => message.CycleTime is not null))
            {
                definitions["GenMsgCycleTime"] = GenMsgCycleTimeDefinition;
            }

            if (!definitions.ContainsKey("GenSigStartValue")
                && database.Messages.Any(m => m.Signals.Any(s => s.RawInitial is not null)))
            {
                definitions["GenSigStartValue"] = GenSigStartValueDefinition;
            }

            if (database.Buses.Count == 1 && database.Buses[0].Baudrate is not null)
            {
                definitions.TryAdd("Baudrate", BaudrateDefinition);
            }
            else
            {
                definitions.Remove("Baudrate");
            }

            if (database.Dbc?.Attributes.TryGetValue("BusType", out var busType) == true
                && busType.Value == (SignalValue)"CAN FD")
            {
                definitions.TryAdd("VFrameFormat", VFrameFormatDefinition);
                definitions.TryAdd("CANFD_BRS", CanFdBrsDefinition);
            }

            foreach (var (kind, name, used) in new (string, string, bool)[]
                     {
                         ("BU_", "SystemNodeLongSymbol", _nodeLongSymbols.Count > 0),
                         ("BO_", "SystemMessageLongSymbol", _messageLongSymbols.Count > 0),
                         ("SG_", "SystemSignalLongSymbol",
                          _signalLongSymbols.Values.Any(map => map.Count > 0)),
                         ("EV_", "SystemEnvVarLongSymbol",
                          _environmentVariableNames.Any(entry => entry.Key.Name != entry.Value)),
                     })
            {
                if (used)
                {
                    definitions.TryAdd(name, new AttributeDefinition(name, "", kind, "STRING"));
                }
            }

            return definitions;
        }

        private static string Sanitize(string name)
        {
            var sanitized = NonWordCharacters().Replace(name, "_");

            return sanitized.Length > 0 && char.IsDigit(sanitized[0]) ? "_" + sanitized : sanitized;
        }
    }
}
