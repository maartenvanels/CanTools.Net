using System.Globalization;
using CanTools.Model;

namespace CanTools.Formats.Sym;

/// <summary>Turns parsed SYM statements into messages, mirroring upstream's loader.</summary>
internal sealed class SymBuilder
{
    private readonly SymFileContents _contents;
    private readonly bool _strict;
    private readonly SignalSort? _sortSignals;
    private readonly Dictionary<string, Signal> _templates = [];

    public SymBuilder(SymFileContents contents, bool strict, SignalSort? sortSignals)
    {
        _contents = contents;
        _strict = strict;
        _sortSignals = sortSignals;

        foreach (var definition in contents.SignalTemplates)
        {
            _templates[definition.Name] = BuildSignal(definition, start: 0);
        }
    }

    public List<Message> BuildMessages()
    {
        var messages = new List<Message>();

        foreach (var (symbols, senders) in new (List<SymSymbol>, string[])[]
                 {
                     (_contents.Send, ["ECU"]),
                     (_contents.Receive, ["Peripherals"]),
                     (_contents.SendReceive, ["ECU", "Peripherals"]),
                 })
        {
            foreach (var symbol in symbols)
            {
                if (symbol.IdText is null)
                {
                    // Sections without an ID only extend a multiplexed message.
                    continue;
                }

                messages.AddRange(BuildSymbolMessages(symbol, symbols, senders));
            }
        }

        return messages;
    }

    private List<Message> BuildSymbolMessages(
        SymSymbol symbol, List<SymSymbol> sectionSymbols, string[] senders)
    {
        var frameId = long.Parse(symbol.IdText![..^1], NumberStyles.HexNumber);
        var frameIdEnd = symbol.IdRangeEndText is { } range
            ? long.Parse(range[1..^1], NumberStyles.HexNumber)
            : frameId;

        var isExtendedFrame = symbol.IdText.Length == 9
            || symbol.TypeText?.ToLowerInvariant() is "extended" or "fdextended";

        var signals = new List<Signal>();
        string? multiplexerName = null;

        if (symbol.MuxLines.Count > 0)
        {
            var mux = symbol.MuxLines[0];
            multiplexerName = mux.Name;
            var byteOrder = mux.IsBigEndian ? ByteOrder.BigEndian : ByteOrder.LittleEndian;

            signals.Add(new Signal(
                mux.Name,
                start: ConvertStart(int.Parse(mux.Start), byteOrder),
                length: int.Parse(mux.Length),
                byteOrder: byteOrder,
                comment: mux.Comment is null ? null : new Comments(mux.Comment),
                isMultiplexer: true));
        }

        AppendChunkSignals(signals, symbol, multiplexerName);

        // Same-named symbols without an ID contribute the other multiplexed layers,
        // keyed by their own mux id but under the first section's selector.
        foreach (var continuation in sectionSymbols)
        {
            if (!ReferenceEquals(continuation, symbol) && continuation.Name == symbol.Name)
            {
                AppendChunkSignals(signals, continuation, multiplexerName);
            }
        }

        var comment = symbol.IdComment;
        var messages = new List<Message>();

        for (var id = frameId; id <= frameIdEnd; id++)
        {
            messages.Add(new Message(
                frameId: (uint)id,
                name: symbol.Name,
                length: symbol.LengthText is { } lengthText ? int.Parse(lengthText) : 8,
                signals: signals,
                unusedBitPattern: 0xff,
                comment: comment is null ? null : new Comments(comment),
                senders: senders,
                cycleTime: symbol.CycleTimeText is { } cycleTime
                    ? (int)double.Parse(cycleTime, CultureInfo.InvariantCulture)
                    : null,
                isExtendedFrame: isExtendedFrame,
                strict: _strict,
                sortSignals: _sortSignals));
        }

        return messages;
    }

    private void AppendChunkSignals(List<Signal> signals, SymSymbol chunk, string? multiplexerName)
    {
        IReadOnlyList<long>? multiplexerIds = null;

        if (multiplexerName is not null && chunk.MuxLines.Count > 0)
        {
            multiplexerIds = [ParseMuxId(chunk.MuxLines[0].MuxIdText)];
        }

        foreach (var (name, start) in chunk.SignalReferences)
        {
            if (!_templates.TryGetValue(name, out var template))
            {
                throw new ParseException($"Signal '{name}' is not defined.");
            }

            signals.Add(new Signal(
                template.Name,
                start: ConvertStart(int.Parse(start), template.ByteOrder),
                length: template.Length,
                byteOrder: template.ByteOrder,
                isSigned: template.IsSigned,
                conversion: template.Conversion,
                minimum: template.Minimum,
                maximum: template.Maximum,
                unit: template.Unit,
                comment: template.Comments,
                multiplexerIds: multiplexerIds,
                multiplexerSignal: multiplexerName,
                spn: template.Spn));
        }

        foreach (var definition in chunk.Variables)
        {
            signals.Add(BuildSignal(
                definition,
                start: ConvertStart(int.Parse(definition.StartText!), OrderOf(definition)),
                multiplexerIds: multiplexerIds,
                multiplexerSignal: multiplexerName));
        }
    }

    private Signal BuildSignal(
        SymSignalDefinition definition,
        int start,
        IReadOnlyList<long>? multiplexerIds = null,
        string? multiplexerSignal = null)
    {
        var isSigned = false;
        var isFloat = false;
        int length;
        Dictionary<long, NamedSignalValue>? choices = null;
        double? minimum = null;
        double? maximum = null;

        switch (definition.TypeName)
        {
            case "unsigned":
            case "string":
            case "raw":
                length = int.Parse(definition.LengthText!);
                break;
            case "signed":
                isSigned = true;
                length = int.Parse(definition.LengthText!);
                break;
            case "float":
                isFloat = true;
                length = 32;
                break;
            case "double":
                isFloat = true;
                length = 64;
                break;
            case "bit":
                length = 1;
                minimum = 0;
                maximum = 1;
                break;
            case "char":
                length = 8;
                break;
            default:
                choices = LookUpEnum(definition.TypeName);
                length = int.Parse(definition.LengthText!);
                break;
        }

        double scale = 1;
        double offset = 0;
        string? unit = null;
        long? spn = null;

        foreach (var (prefix, value) in definition.Attributes)
        {
            switch (prefix)
            {
                case "/u:":
                    unit = value;
                    break;
                case "/f:":
                    scale = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "/o:":
                    offset = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "/min:":
                    minimum = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "/max:":
                    maximum = double.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "/spn:":
                    spn = long.Parse(value);
                    break;
                case "/e:":
                    choices = LookUpEnum(value);
                    break;
                // /d: (default), /ln: (long name) and /p: (decimal places) are
                // accepted but not represented in the model, like upstream.
            }
        }

        return new Signal(
            definition.Name,
            start: start,
            length: length,
            byteOrder: OrderOf(definition),
            isSigned: isSigned,
            conversion: Conversion.Create(scale, offset, choices, isFloat),
            minimum: minimum,
            maximum: maximum,
            unit: unit,
            comment: definition.Comment is null ? null : new Comments(definition.Comment),
            multiplexerIds: multiplexerIds,
            multiplexerSignal: multiplexerSignal,
            spn: spn);
    }

    private Dictionary<long, NamedSignalValue> LookUpEnum(string name)
    {
        if (!_contents.Enums.TryGetValue(name, out var values))
        {
            throw new ParseException($"Enum '{name}' is not defined.");
        }

        return values;
    }

    private static ByteOrder OrderOf(SymSignalDefinition definition) =>
        definition.IsBigEndian ? ByteOrder.BigEndian : ByteOrder.LittleEndian;

    // SYM stores big-endian start bits in sawtooth numbering; the model uses the
    // MSB position, like DBC.
    private static int ConvertStart(int start, ByteOrder byteOrder) =>
        byteOrder == ByteOrder.BigEndian ? 8 * (start / 8) + (7 - start % 8) : start;

    private static long ParseMuxId(string text) =>
        text.EndsWith('h')
            ? long.Parse(text[..^1], NumberStyles.HexNumber)
            : long.Parse(text, CultureInfo.InvariantCulture);
}
