using System.Globalization;
using CanTools.Packing;

namespace CanTools.Model;

/// <summary>
/// A CAN message: frame id, length, signals and multiplexing structure, with
/// encode/decode of signal values including simple and extended multiplexing.
///
/// When <c>strict</c> is true (the default), construction fails if signals overlap
/// or do not fit in the message.
/// </summary>
public sealed class Message
{
    private readonly List<Signal> _signals;
    private readonly bool _strict;
    private CodecNode _codec = null!;
    private Dictionary<string, Signal> _signalsByName = null!;

    public Message(
        uint frameId,
        string name,
        int length,
        IReadOnlyList<Signal> signals,
        byte unusedBitPattern = 0x00,
        Comments? comment = null,
        IReadOnlyList<string>? senders = null,
        string? sendType = null,
        int? cycleTime = null,
        bool isExtendedFrame = false,
        bool isFd = false,
        string? busName = null,
        IReadOnlyList<SignalGroup>? signalGroups = null,
        bool strict = true,
        string? protocol = null,
        SignalSort? sortSignals = null)
    {
        if (isExtendedFrame)
        {
            if (frameId > 0x1fffffff)
            {
                throw new CanToolsException(
                    $"Extended frame id 0x{frameId:x} is more than 29 bits in message {name}.");
            }
        }
        else if (frameId > 0x7ff)
        {
            throw new CanToolsException(
                $"Standard frame id 0x{frameId:x} is more than 11 bits in message {name}.");
        }

        FrameId = frameId;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Length = length;
        UnusedBitPattern = unusedBitPattern;
        Comments = comment;
        Senders = senders ?? [];
        SendType = sendType;
        CycleTime = cycleTime;
        IsExtendedFrame = isExtendedFrame;
        IsFd = isFd;
        BusName = busName;
        SignalGroups = signalGroups;
        Protocol = protocol;
        _signals = [.. (sortSignals ?? SignalSorts.ByStartBit)(signals)];
        _strict = strict;
        Refresh();
    }

    public uint FrameId { get; }

    public string Name { get; }

    /// <summary>The message data length in bytes.</summary>
    public int Length { get; }

    /// <summary>All signals of the message, including every multiplexed layer.</summary>
    public IReadOnlyList<Signal> Signals => _signals;

    /// <summary>The bit pattern used for unused bits when encoding with padding.</summary>
    public byte UnusedBitPattern { get; }

    /// <summary>The message comment, or null if unavailable.</summary>
    public string? Comment => Comments?.Resolve();

    public Comments? Comments { get; }

    /// <summary>The names of all nodes sending this message.</summary>
    public IReadOnlyList<string> Senders { get; }

    /// <summary>The names of all nodes receiving at least one signal of this message.</summary>
    public IReadOnlySet<string> Receivers { get; private set; } = new HashSet<string>();

    public string? SendType { get; }

    /// <summary>The message cycle time in milliseconds, or null if unavailable.</summary>
    public int? CycleTime { get; }

    public bool IsExtendedFrame { get; }

    /// <summary>True if the message requires CAN FD.</summary>
    public bool IsFd { get; }

    public string? BusName { get; }

    public IReadOnlyList<SignalGroup>? SignalGroups { get; }

    /// <summary>The message protocol, or null. Only "j1939" is currently recognized.</summary>
    public string? Protocol { get; }

    /// <summary>DBC-specific properties such as attributes, or null.</summary>
    public DbcSpecifics? Dbc { get; init; }

    /// <summary>
    /// The signal names as a tree: plain signals are leaves, multiplexer selectors
    /// carry the available signals per multiplexer value.
    /// </summary>
    public IReadOnlyList<SignalTreeNode> SignalTree { get; private set; } = [];

    public bool IsMultiplexed => _codec.Multiplexers.Count > 0;

    public Signal GetSignalByName(string name) => _signalsByName[name];

    /// <summary>Recomputes the internal codec state after signals were modified.</summary>
    public void Refresh() => Refresh(_strict);

    /// <summary>
    /// Recomputes the internal codec state. When <paramref name="strict"/> is true,
    /// signals are validated to fit the message without overlapping.
    /// </summary>
    public void Refresh(bool strict)
    {
        foreach (var signal in _signals)
        {
            if (signal.Length <= 0)
            {
                throw new CanToolsException(
                    $"The signal {signal.Name} length {signal.Length} is not greater "
                    + $"than 0 in message {Name}.");
            }
        }

        _codec = CreateCodec(parentSignal: null, multiplexerId: null);
        SignalTree = CreateSignalTree(_codec);
        Receivers = _signals.SelectMany(signal => signal.Receivers).ToHashSet();

        // Signals may share a name across multiplexed layers; the last one wins,
        // like upstream's dict comprehension.
        _signalsByName = new Dictionary<string, Signal>();
        foreach (var signal in _signals)
        {
            _signalsByName[signal.Name] = signal;
        }

        if (strict)
        {
            var messageBits = new string?[8 * Length];
            CheckSignalTree(messageBits, SignalTree);
        }
    }

    /// <summary>Encodes signal values into frame data.</summary>
    /// <remarks>
    /// When <paramref name="strict"/> is true, exactly the required signals must be
    /// given and their values must be within the allowed ranges. When
    /// <paramref name="padding"/> is true, unused bits are set to
    /// <see cref="UnusedBitPattern"/>.
    /// </remarks>
    public byte[] Encode(
        IReadOnlyDictionary<string, SignalValue> data,
        bool scaling = true,
        bool padding = false,
        bool strict = true)
    {
        if (strict)
        {
            AssertSignalsEncodable(data, scaling);
        }

        var buffer = new byte[Length];
        var paddingMask = EncodeNode(_codec, data, scaling, buffer, needsMask: padding);

        if (padding)
        {
            for (var i = 0; i < buffer.Length; i++)
            {
                buffer[i] |= (byte)(paddingMask![i] & UnusedBitPattern);
            }
        }

        return buffer;
    }

    /// <summary>Decodes frame data into signal values.</summary>
    public Dictionary<string, SignalValue> Decode(
        ReadOnlySpan<byte> data,
        bool decodeChoices = true,
        bool scaling = true,
        bool allowTruncated = false,
        bool allowExcess = true)
    {
        return DecodeNode(_codec, data, decodeChoices, scaling, allowTruncated, allowExcess);
    }

    /// <summary>
    /// Given a superset of the signal values needed to encode the message, returns
    /// exactly the required ones, following the active multiplexer selections.
    /// </summary>
    public Dictionary<string, SignalValue> GatherSignals(
        IReadOnlyDictionary<string, SignalValue> inputData)
    {
        var result = new Dictionary<string, SignalValue>();
        GatherSignals(inputData, _codec, result);

        return result;
    }

    /// <summary>
    /// Validates that the given signal values suffice to encode the message: all
    /// required signals present, no unknown extras, and every value in range.
    /// </summary>
    public void AssertSignalsEncodable(
        IReadOnlyDictionary<string, SignalValue> inputData,
        bool scaling,
        bool assertValuesValid = true,
        bool assertAllKnown = true)
    {
        var usedSignals = GatherSignals(inputData);

        if (assertAllKnown && inputData.Count != usedSignals.Count)
        {
            var extras = inputData.Keys
                .Where(name => !usedSignals.ContainsKey(name))
                .Order(StringComparer.Ordinal);

            throw new EncodeException(
                "The following signals were specified but are not required to encode "
                + $"the message:{{{string.Join(", ", extras.Select(name => $"'{name}'"))}}}");
        }

        if (assertValuesValid)
        {
            AssertSignalValuesValid(usedSignals, scaling);
        }
    }

    private CodecNode CreateCodec(string? parentSignal, long? multiplexerId)
    {
        var signals = new List<Signal>();
        var multiplexers = new Dictionary<string, SortedDictionary<long, CodecNode>>();

        foreach (var signal in _signals)
        {
            if (signal.MultiplexerSignal != parentSignal)
            {
                continue;
            }

            if (multiplexerId is { } id
                && (signal.MultiplexerIds is null || !signal.MultiplexerIds.Contains(id)))
            {
                continue;
            }

            if (signal.IsMultiplexer)
            {
                var childIds = new SortedSet<long>();

                foreach (var other in _signals)
                {
                    if (other.MultiplexerSignal == signal.Name && other.MultiplexerIds is not null)
                    {
                        childIds.UnionWith(other.MultiplexerIds);
                    }
                }

                // A multiplexer value can be announced by the value table alone,
                // without any signals of its own (an empty layer).
                if (signal.Conversion.Choices is { } choices)
                {
                    childIds.UnionWith(choices.Keys);
                }

                if (childIds.Count > 0)
                {
                    var children = new SortedDictionary<long, CodecNode>();
                    multiplexers[signal.Name] = children;

                    foreach (var childId in childIds)
                    {
                        children[childId] = CreateCodec(signal.Name, childId);
                    }
                }
            }

            signals.Add(signal);
        }

        return new CodecNode(MessageCodec.Create(signals, Length), signals, multiplexers);
    }

    private static IReadOnlyList<SignalTreeNode> CreateSignalTree(CodecNode node)
    {
        var tree = new List<SignalTreeNode>(node.Signals.Count);

        foreach (var signal in node.Signals)
        {
            if (node.Multiplexers.TryGetValue(signal.Name, out var children))
            {
                var multiplexed = new SortedDictionary<long, IReadOnlyList<SignalTreeNode>>();

                foreach (var (multiplexerId, child) in children)
                {
                    multiplexed[multiplexerId] = CreateSignalTree(child);
                }

                tree.Add(new SignalTreeNode(signal.Name, multiplexed));
            }
            else
            {
                tree.Add(new SignalTreeNode(signal.Name));
            }
        }

        return tree;
    }

    private byte[]? EncodeNode(
        CodecNode node,
        IReadOnlyDictionary<string, SignalValue> data,
        bool scaling,
        Span<byte> buffer,
        bool needsMask)
    {
        node.Codec.EncodeInto(data, buffer, scaling);
        var paddingMask = needsMask ? node.Codec.PaddingMask.ToArray() : null;

        foreach (var (selectorName, children) in node.Multiplexers)
        {
            var multiplexerId = GetMuxNumber(data, selectorName);

            if (!children.TryGetValue(multiplexerId, out var child))
            {
                throw new EncodeException(
                    $"Expected multiplexer id in {{{FormatOr(children.Keys)}}}, "
                    + $"for multiplexer \"{selectorName}\" but got {multiplexerId}");
            }

            var childMask = EncodeNode(child, data, scaling, buffer, needsMask);

            for (var i = 0; needsMask && i < paddingMask!.Length; i++)
            {
                paddingMask[i] &= childMask![i];
            }
        }

        return paddingMask;
    }

    private Dictionary<string, SignalValue> DecodeNode(
        CodecNode node,
        ReadOnlySpan<byte> data,
        bool decodeChoices,
        bool scaling,
        bool allowTruncated,
        bool allowExcess)
    {
        var decoded = node.Codec.Decode(data, decodeChoices, scaling, allowTruncated, allowExcess);

        foreach (var (selectorName, children) in node.Multiplexers)
        {
            if (allowTruncated && !decoded.ContainsKey(selectorName))
            {
                continue;
            }

            var multiplexerId = GetMuxNumber(decoded, selectorName);

            if (!children.TryGetValue(multiplexerId, out var child))
            {
                throw new DecodeException(
                    $"expected multiplexer id {FormatOr(children.Keys)}, "
                    + $"but got {multiplexerId}");
            }

            foreach (var (name, value) in
                     DecodeNode(child, data, decodeChoices, scaling, allowTruncated, allowExcess))
            {
                decoded[name] = value;
            }
        }

        return decoded;
    }

    private void GatherSignals(
        IReadOnlyDictionary<string, SignalValue> inputData,
        CodecNode node,
        Dictionary<string, SignalValue> result)
    {
        foreach (var signal in node.Signals)
        {
            if (!inputData.TryGetValue(signal.Name, out var value))
            {
                throw new EncodeException(
                    $"The signal \"{signal.Name}\" is required for encoding.");
            }

            result[signal.Name] = value;
        }

        foreach (var (selectorName, children) in node.Multiplexers)
        {
            var multiplexerId = GetMuxNumber(inputData, selectorName);

            if (!children.TryGetValue(multiplexerId, out var child))
            {
                throw new EncodeException(
                    $"A valid value for the multiplexer selector signal \"{selectorName}\" "
                    + $"is required: Expected one of {{{FormatOr(children.Keys)}}}, "
                    + $"but got {inputData[selectorName]}");
            }

            GatherSignals(inputData, child, result);
        }
    }

    private void AssertSignalValuesValid(
        IReadOnlyDictionary<string, SignalValue> data,
        bool scaling)
    {
        foreach (var (signalName, signalValue) in data)
        {
            var signal = GetSignalByName(signalName);

            if (signalValue.IsLabel)
            {
                signal.ChoiceToNumber(signalValue.Label!);
                continue;
            }

            SignalValue scaledValue;
            SignalValue rawValue;

            if (scaling)
            {
                scaledValue = signalValue;
                rawValue = signal.Conversion.NumericScaledToRaw(signalValue);
            }
            else
            {
                scaledValue = signal.Conversion.RawToScaled(signalValue, decodeChoices: false);
                rawValue = signalValue;
            }

            // A raw value present in the value table is valid regardless of the range.
            if (signal.Choices is { } choices
                && NamedSignalConversion.TryGetIntegral(rawValue, out var choiceKey)
                && choices.ContainsKey(choiceKey))
            {
                continue;
            }

            var tolerance = Math.Abs(signal.Scale) * 1e-6;

            if (signal.Minimum is { } minimum && scaledValue.ToDouble() < minimum - tolerance)
            {
                throw new EncodeException(
                    $"Expected signal \"{signal.Name}\" value greater than or equal to "
                    + $"{Format(minimum)} in message \"{Name}\", but got {scaledValue}.");
            }

            if (signal.Maximum is { } maximum && scaledValue.ToDouble() > maximum + tolerance)
            {
                throw new EncodeException(
                    $"Expected signal \"{signal.Name}\" value smaller than or equal to "
                    + $"{Format(maximum)} in message \"{Name}\", but got {scaledValue}.");
            }
        }
    }

    private long GetMuxNumber(IReadOnlyDictionary<string, SignalValue> data, string selectorName)
    {
        var value = data[selectorName];

        if (value.IsLabel)
        {
            try
            {
                return GetSignalByName(selectorName).ChoiceToNumber(value.Label!);
            }
            catch (KeyNotFoundException)
            {
                throw new EncodeException(
                    $"Invalid multiplexer value '{value.Label}' for signal '{selectorName}'.");
            }
        }

        return value.IsInteger ? value.ToInt64() : (long)value.ToDouble();
    }

    private void CheckSignalTree(string?[] messageBits, IReadOnlyList<SignalTreeNode> tree)
    {
        foreach (var node in tree)
        {
            if (node.Multiplexed is { } multiplexed)
            {
                CheckMux(messageBits, node.Name, multiplexed);
            }
            else
            {
                CheckSignal(messageBits, GetSignalByName(node.Name));
            }
        }
    }

    private void CheckMux(
        string?[] messageBits,
        string selectorName,
        IReadOnlyDictionary<long, IReadOnlyList<SignalTreeNode>> multiplexed)
    {
        CheckSignal(messageBits, GetSignalByName(selectorName));

        // Layers of different multiplexer values may overlap each other, but not
        // the selector or any signal outside the multiplexed layers.
        var baseBits = (string?[])messageBits.Clone();

        foreach (var multiplexerId in multiplexed.Keys.Order())
        {
            var layerBits = (string?[])baseBits.Clone();
            CheckSignalTree(layerBits, multiplexed[multiplexerId]);

            for (var i = 0; i < messageBits.Length; i++)
            {
                if (layerBits[i] is not null)
                {
                    messageBits[i] = layerBits[i];
                }
            }
        }
    }

    private void CheckSignal(string?[] messageBits, Signal signal)
    {
        var sequentialStart = SignalSorts.NetworkStartBit(signal);

        if (sequentialStart + signal.Length > messageBits.Length)
        {
            throw new CanToolsException(
                $"The signal {signal.Name} does not fit in message {Name}.");
        }

        foreach (var networkBit in CoveredNetworkBits(signal))
        {
            if (messageBits[networkBit] is { } other)
            {
                throw new CanToolsException(
                    $"The signals {signal.Name} and {other} are overlapping in message {Name}.");
            }

            messageBits[networkBit] = signal.Name;
        }
    }

    private static IEnumerable<int> CoveredNetworkBits(Signal signal)
    {
        if (signal.ByteOrder == ByteOrder.BigEndian)
        {
            var start = SignalSorts.NetworkStartBit(signal);

            return Enumerable.Range(start, signal.Length);
        }

        return Enumerable.Range(signal.StartBit, signal.Length)
            .Select(BitNumbering.SawtoothToNetwork)
            .Order();
    }

    private static string FormatOr(IEnumerable<long> items)
    {
        var texts = items.Select(item => item.ToString(CultureInfo.InvariantCulture)).ToList();

        return texts.Count == 1
            ? texts[0]
            : $"{string.Join(", ", texts[..^1])} or {texts[^1]}";
    }

    private static string Format(double value) => value.ToString(CultureInfo.InvariantCulture);

    public override string ToString() =>
        $"Message {Name} (0x{FrameId:X}, {Length} bytes, {Signals.Count} signals)";

    private sealed record CodecNode(
        MessageCodec Codec,
        List<Signal> Signals,
        Dictionary<string, SortedDictionary<long, CodecNode>> Multiplexers);
}
