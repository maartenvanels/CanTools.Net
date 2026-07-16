using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using CanTools.Model;

namespace CanTools.Formats.Kcd;

/// <summary>Reads KCD (Kayak) files into a <see cref="Database"/>.</summary>
public static class KcdReader
{
    private static readonly XNamespace Ns = "http://kayak.2codeornot2code.org/1.0";

    public static Database LoadFile(
        string path,
        Encoding? encoding = null,
        bool strict = true,
        SignalSort? sortSignals = null)
    {
        return LoadString(File.ReadAllText(path, encoding ?? Encoding.UTF8), strict, sortSignals);
    }

    public static Database LoadString(string text, bool strict = true, SignalSort? sortSignals = null)
    {
        var database = new Database(strict: strict, sortSignals: sortSignals);
        database.AddKcdString(text, sortSignals);

        return database;
    }

    internal static (List<Message> Messages, List<Node> Nodes, List<Bus> Buses, string? Version)
        Load(string text, bool strict, SignalSort? sortSignals)
    {
        XElement root;
        try
        {
            root = XDocument.Parse(text).Root!;
        }
        catch (XmlException exception)
        {
            throw new ParseException(
                $"syntax error: line {exception.LineNumber}, column {exception.LinePosition - 1}");
        }

        if (root.Name != Ns + "NetworkDefinition")
        {
            var tag = root.Name.NamespaceName == ""
                ? root.Name.LocalName
                : $"{{{root.Name.NamespaceName}}}{root.Name.LocalName}";

            throw new ParseException(
                $"Expected root element tag {{{Ns}}}NetworkDefinition, but got {tag}.");
        }

        var version = (string?)root.Element(Ns + "Document")?.Attribute("version");

        var nodeNamesById = new Dictionary<string, string>();
        var nodes = new List<Node>();

        foreach (var node in root.Elements(Ns + "Node"))
        {
            var name = (string)node.Attribute("name")!;
            nodes.Add(new Node(name));

            if ((string?)node.Attribute("id") is { } id)
            {
                nodeNamesById[id] = name;
            }
        }

        var buses = new List<Bus>();
        var messages = new List<Message>();

        foreach (var busElement in root.Elements(Ns + "Bus"))
        {
            var busName = (string)busElement.Attribute("name")!;
            buses.Add(new Bus(
                busName,
                baudrate: (int?)busElement.Attribute("baudrate") ?? 500000));

            foreach (var messageElement in busElement.Elements(Ns + "Message"))
            {
                messages.Add(LoadMessage(
                    messageElement, busName, nodeNamesById, strict, sortSignals));
            }
        }

        return (messages, nodes, buses, version);
    }

    private static Message LoadMessage(
        XElement element,
        string busName,
        Dictionary<string, string> nodeNamesById,
        bool strict,
        SignalSort? sortSignals)
    {
        var signals = new List<Signal>();

        // Multiplexed layers come before the plain signals, like upstream; the
        // stable default sort relies on this insertion order.
        foreach (var multiplex in element.Elements(Ns + "Multiplex"))
        {
            var selector = LoadSignal(multiplex, nodeNamesById, isMultiplexer: true);
            signals.Add(selector);

            foreach (var group in multiplex.Elements(Ns + "MuxGroup"))
            {
                var multiplexerId = long.Parse((string)group.Attribute("count")!);

                foreach (var signalElement in group.Elements(Ns + "Signal"))
                {
                    signals.Add(LoadSignal(
                        signalElement, nodeNamesById,
                        multiplexerIds: [multiplexerId],
                        multiplexerSignal: selector.Name));
                }
            }
        }

        foreach (var signalElement in element.Elements(Ns + "Signal"))
        {
            signals.Add(LoadSignal(signalElement, nodeNamesById));
        }

        var lengthText = (string?)element.Attribute("length") ?? "auto";
        var length = lengthText == "auto" ? AutoLength(signals) : int.Parse(lengthText);

        var senders = new List<string>();
        if (element.Element(Ns + "Producer") is { } producer)
        {
            foreach (var nodeRef in producer.Elements(Ns + "NodeRef"))
            {
                if (nodeNamesById.TryGetValue((string)nodeRef.Attribute("id")!, out var name))
                {
                    senders.Add(name);
                }
            }
        }

        var comment = element.Element(Ns + "Notes")?.Value;

        return new Message(
            frameId: (uint)ParseInteger((string)element.Attribute("id")!),
            name: (string)element.Attribute("name")!,
            length: length,
            signals: signals,
            unusedBitPattern: 0xff,
            comment: comment is null ? null : new Comments(comment),
            senders: senders,
            cycleTime: (int?)element.Attribute("interval"),
            isExtendedFrame: (string?)element.Attribute("format") == "extended",
            busName: busName,
            strict: strict,
            sortSignals: sortSignals);
    }

    private static Signal LoadSignal(
        XElement element,
        Dictionary<string, string> nodeNamesById,
        bool isMultiplexer = false,
        IReadOnlyList<long>? multiplexerIds = null,
        string? multiplexerSignal = null)
    {
        var offset = (int?)element.Attribute("offset") ?? 0;
        var byteOrder = (string?)element.Attribute("endianess") == "big"
            ? ByteOrder.BigEndian
            : ByteOrder.LittleEndian;

        var isSigned = false;
        var isFloat = false;
        double scale = 1;
        double intercept = 0;
        double? minimum = null;
        double? maximum = null;
        string? unit = null;

        if (element.Element(Ns + "Value") is { } value)
        {
            var type = (string?)value.Attribute("type");
            isSigned = type == "signed";
            isFloat = type is "single" or "double";
            scale = (double?)value.Attribute("slope") ?? 1;
            intercept = (double?)value.Attribute("intercept") ?? 0;
            minimum = (double?)value.Attribute("min");
            maximum = (double?)value.Attribute("max");
            unit = (string?)value.Attribute("unit");
        }

        Dictionary<long, NamedSignalValue>? choices = null;
        if (element.Element(Ns + "LabelSet") is { } labelSet)
        {
            choices = [];

            foreach (var label in labelSet.Elements(Ns + "Label"))
            {
                var labelValue = long.Parse((string)label.Attribute("value")!);
                choices[labelValue] = new NamedSignalValue(labelValue, (string)label.Attribute("name")!);
            }
        }

        var receivers = new List<string>();
        if (element.Element(Ns + "Consumer") is { } consumer)
        {
            foreach (var nodeRef in consumer.Elements(Ns + "NodeRef"))
            {
                if (nodeNamesById.TryGetValue((string)nodeRef.Attribute("id")!, out var name))
                {
                    receivers.Add(name);
                }
            }
        }

        var comment = element.Element(Ns + "Notes")?.Value;

        return new Signal(
            name: (string)element.Attribute("name")!,
            start: StartBit(offset, byteOrder),
            length: (int?)element.Attribute("length") ?? 1,
            byteOrder: byteOrder,
            isSigned: isSigned,
            conversion: Conversion.Create(scale, intercept, choices, isFloat),
            minimum: minimum,
            maximum: maximum,
            unit: unit,
            comment: comment is null ? null : new Comments(comment),
            receivers: receivers,
            isMultiplexer: isMultiplexer,
            multiplexerIds: multiplexerIds,
            multiplexerSignal: multiplexerSignal);
    }

    // KCD offsets are sequential bit positions; DBC start bits count from the MSB
    // within each byte for big-endian signals.
    private static int StartBit(int offset, ByteOrder byteOrder) =>
        byteOrder == ByteOrder.BigEndian ? 8 * (offset / 8) + (7 - offset % 8) : offset;

    private static int AutoLength(IReadOnlyList<Signal> signals)
    {
        if (signals.Count == 0)
        {
            return 0;
        }

        // The signal reaching furthest into the frame determines the length; on a
        // tie the last-listed signal wins, like Python's stable sort.
        var bestKey = -1;
        Signal? best = null;

        foreach (var signal in signals)
        {
            var key = SignalSorts.NetworkStartBit(signal);

            if (key >= bestKey)
            {
                bestKey = key;
                best = signal;
            }
        }

        return (bestKey + best!.Length + 7) / 8;
    }

    // Like Python's int(x, 0): base prefix aware.
    private static long ParseInteger(string text)
    {
        if (text.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            return long.Parse(text[2..], NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        }

        if (text.StartsWith("0b", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToInt64(text[2..], 2);
        }

        if (text.StartsWith("0o", StringComparison.OrdinalIgnoreCase))
        {
            return Convert.ToInt64(text[2..], 8);
        }

        return long.Parse(text, CultureInfo.InvariantCulture);
    }
}
