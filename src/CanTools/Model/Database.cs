using System.Diagnostics.CodeAnalysis;

namespace CanTools.Model;

/// <summary>
/// All messages, signals and definitions of a CAN network. Load one from a file with
/// the format-specific readers (e.g. <c>CanTools.Formats.Dbc.DbcReader</c>) or build
/// it in code.
///
/// When <c>strict</c> is true (the default), signals must fit their messages without
/// overlapping.
/// </summary>
public sealed class Database
{
    private readonly List<Message> _messages;
    private readonly List<Node> _nodes;
    private readonly List<Bus> _buses;
    private readonly bool _strict;
    private Dictionary<string, Message> _messagesByName = null!;
    private Dictionary<uint, Message> _messagesByFrameId = null!;

    public Database(
        IReadOnlyList<Message>? messages = null,
        IReadOnlyList<Node>? nodes = null,
        IReadOnlyList<Bus>? buses = null,
        string? version = null,
        DbcSpecifics? dbcSpecifics = null,
        uint frameIdMask = 0xffffffff,
        bool strict = true,
        SignalSort? sortSignals = null)
    {
        _messages = [.. messages ?? []];
        _nodes = [.. nodes ?? []];
        _buses = [.. buses ?? []];
        Version = version;
        Dbc = dbcSpecifics;
        FrameIdMask = frameIdMask;
        _strict = strict;
        SortSignalsOption = sortSignals;
        Refresh();
    }

    /// <summary>The signal ordering this database was created with, or null for the default.</summary>
    internal SignalSort? SortSignalsOption { get; private set; }

    public IReadOnlyList<Message> Messages => _messages;

    public IReadOnlyList<Node> Nodes => _nodes;

    public IReadOnlyList<Bus> Buses => _buses;

    /// <summary>The database version, or null if unavailable.</summary>
    public string? Version { get; set; }

    /// <summary>DBC-specific properties such as attributes and value tables, or null.</summary>
    public DbcSpecifics? Dbc { get; private set; }

    /// <summary>The mask applied to frame ids when looking up messages.</summary>
    public uint FrameIdMask { get; }

    public Message GetMessageByName(string name) =>
        _messagesByName.TryGetValue(name, out var message)
            ? message
            : throw new KeyNotFoundException($"There is no message named '{name}' in this database.");

    public bool TryGetMessageByName(string name, [MaybeNullWhen(false)] out Message message) =>
        _messagesByName.TryGetValue(name, out message);

    public Message GetMessageByFrameId(uint frameId, bool forceExtendedId = false) =>
        _messagesByFrameId.TryGetValue(LookupId(frameId, forceExtendedId), out var message)
            ? message
            : throw new KeyNotFoundException(
                $"There is no message with frame id 0x{frameId:X} in this database.");

    public bool TryGetMessageByFrameId(
        uint frameId, [MaybeNullWhen(false)] out Message message, bool forceExtendedId = false)
    {
        return _messagesByFrameId.TryGetValue(LookupId(frameId, forceExtendedId), out message);
    }

    public Node GetNodeByName(string name) =>
        _nodes.FirstOrDefault(node => node.Name == name)
        ?? throw new KeyNotFoundException($"There is no node named '{name}' in this database.");

    public Bus GetBusByName(string name) =>
        _buses.FirstOrDefault(bus => bus.Name == name)
        ?? throw new KeyNotFoundException($"There is no bus named '{name}' in this database.");

    public byte[] EncodeMessage(
        string name,
        IReadOnlyDictionary<string, SignalValue> data,
        bool scaling = true,
        bool padding = false,
        bool strict = true)
    {
        return GetMessageByName(name).Encode(data, scaling, padding, strict);
    }

    public byte[] EncodeMessage(
        uint frameId,
        IReadOnlyDictionary<string, SignalValue> data,
        bool scaling = true,
        bool padding = false,
        bool strict = true,
        bool forceExtendedId = false)
    {
        return GetMessageByFrameId(frameId, forceExtendedId).Encode(data, scaling, padding, strict);
    }

    public Dictionary<string, SignalValue> DecodeMessage(
        string name,
        ReadOnlySpan<byte> data,
        bool decodeChoices = true,
        bool scaling = true,
        bool allowTruncated = false,
        bool allowExcess = true)
    {
        return GetMessageByName(name)
            .Decode(data, decodeChoices, scaling, allowTruncated, allowExcess);
    }

    public Dictionary<string, SignalValue> DecodeMessage(
        uint frameId,
        ReadOnlySpan<byte> data,
        bool decodeChoices = true,
        bool scaling = true,
        bool allowTruncated = false,
        bool allowExcess = true,
        bool forceExtendedId = false)
    {
        return GetMessageByFrameId(frameId, forceExtendedId)
            .Decode(data, decodeChoices, scaling, allowTruncated, allowExcess);
    }

    /// <summary>Parses DBC text and adds its contents to this database.</summary>
    public void AddDbcString(string text, SignalSort? sortSignals = null)
    {
        SortSignalsOption ??= sortSignals;
        var (messages, nodes, buses, version, dbc) =
            Formats.Dbc.DbcReader.Load(text, _strict, sortSignals ?? SortSignalsOption);

        Merge(messages, nodes, buses, version, dbc);
    }

    /// <summary>Parses KCD text and adds its contents to this database.</summary>
    public void AddKcdString(string text, SignalSort? sortSignals = null)
    {
        SortSignalsOption ??= sortSignals;
        var (messages, nodes, buses, version) =
            Formats.Kcd.KcdReader.Load(text, _strict, sortSignals ?? SortSignalsOption);

        Merge(messages, nodes, buses, version, dbcSpecifics: null);
    }

    /// <summary>Reads and parses a KCD file into this database.</summary>
    public void AddKcdFile(
        string path,
        System.Text.Encoding? encoding = null,
        SignalSort? sortSignals = null)
    {
        AddKcdString(
            File.ReadAllText(path, encoding ?? System.Text.Encoding.UTF8), sortSignals);
    }

    /// <summary>Parses SYM text and adds its contents to this database.</summary>
    public void AddSymString(string text, SignalSort? sortSignals = null)
    {
        SortSignalsOption ??= sortSignals;
        var (messages, version) =
            Formats.Sym.SymReader.Load(text, _strict, sortSignals ?? SortSignalsOption);

        Merge(messages, nodes: [], buses: [], version, dbcSpecifics: null);
    }

    /// <summary>Reads and parses a SYM file into this database.</summary>
    public void AddSymFile(
        string path,
        System.Text.Encoding? encoding = null,
        SignalSort? sortSignals = null)
    {
        AddSymString(
            File.ReadAllText(path, encoding ?? System.Text.Encoding.UTF8), sortSignals);
    }

    /// <summary>Renders the database as DBC text; see <c>DbcWriter.Dump</c>.</summary>
    public string ToDbcString(
        SignalSort? sortSignals = null,
        SignalSort? sortAttributeSignals = null,
        bool shortenLongNames = true)
    {
        return Formats.Dbc.DbcWriter.Dump(this, sortSignals, sortAttributeSignals, shortenLongNames);
    }

    /// <summary>Reads and parses a DBC file (cp1252 by default) into this database.</summary>
    public void AddDbcFile(
        string path,
        System.Text.Encoding? encoding = null,
        SignalSort? sortSignals = null)
    {
        var text = File.ReadAllText(path, encoding ?? Formats.FormatEncodings.Cp1252);

        AddDbcString(text, sortSignals);
    }

    /// <summary>Adds messages, nodes, buses and metadata from another database.</summary>
    internal void Merge(
        IReadOnlyList<Message> messages,
        IReadOnlyList<Node> nodes,
        IReadOnlyList<Bus> buses,
        string? version,
        DbcSpecifics? dbcSpecifics)
    {
        _messages.AddRange(messages);
        _nodes.Clear();
        _nodes.AddRange(nodes);
        _buses.Clear();
        _buses.AddRange(buses);
        Version = version;
        Dbc = dbcSpecifics;
        Refresh();
    }

    /// <summary>
    /// Rebuilds the internal lookup tables. Call after modifying messages directly.
    /// </summary>
    public void Refresh()
    {
        _messagesByName = new Dictionary<string, Message>();
        _messagesByFrameId = new Dictionary<uint, Message>();

        foreach (var message in _messages)
        {
            message.Refresh(_strict);

            // Later messages silently replace earlier ones with the same name or
            // masked frame id, like upstream (which merely logs a warning).
            var maskedId = message.FrameId & FrameIdMask;

            if (message.IsExtendedFrame)
            {
                maskedId |= 0x80000000;
            }

            _messagesByName[message.Name] = message;
            _messagesByFrameId[maskedId] = message;
        }
    }

    private uint LookupId(uint frameId, bool forceExtendedId)
    {
        if (forceExtendedId || frameId > 0x7ff)
        {
            frameId |= 0x80000000;
        }

        return frameId & (0x80000000 | FrameIdMask);
    }
}
