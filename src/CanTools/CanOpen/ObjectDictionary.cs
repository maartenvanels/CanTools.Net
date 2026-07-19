using System.Diagnostics.CodeAnalysis;
using CanTools.Formats.Eds;

namespace CanTools.CanOpen;

/// <summary>
/// A CANopen object dictionary: entries by index and by name, plus the device and
/// commissioning information from the EDS/DCF file it was loaded from.
/// </summary>
public sealed class ObjectDictionary
{
    private readonly SortedDictionary<int, OdEntry> _byIndex = [];
    private readonly Dictionary<string, OdEntry> _byName = [];

    /// <summary>The entries in ascending index order.</summary>
    public IReadOnlyCollection<OdEntry> Entries => _byIndex.Values;

    /// <summary>The [Comments] lines of the file, joined with newlines.</summary>
    public string Comments { get; internal set; } = "";

    /// <summary>The commissioned bitrate in bit/s, or null.</summary>
    public int? Bitrate { get; set; }

    /// <summary>The commissioned node id, or null.</summary>
    public int? NodeId { get; set; }

    public DeviceInformation DeviceInformation { get; } = new();

    /// <summary>
    /// The source file this dictionary was loaded from, kept verbatim so it can be
    /// written back with edits (see <c>DcfWriter</c>). Null for dictionaries not
    /// loaded from a file.
    /// </summary>
    internal IniDocument? SourceDocument { get; set; }

    public OdEntry this[int index] =>
        _byIndex.TryGetValue(index, out var entry)
            ? entry
            : throw new KeyNotFoundException(
                $"The object dictionary has no entry at index 0x{index:X4}.");

    public OdEntry this[string name] =>
        _byName.TryGetValue(name, out var entry)
            ? entry
            : throw new KeyNotFoundException(
                $"The object dictionary has no entry named '{name}'.");

    public bool Contains(int index) => _byIndex.ContainsKey(index);

    public bool Contains(string name) => _byName.ContainsKey(name);

    public bool TryGetEntry(int index, [MaybeNullWhen(false)] out OdEntry entry) =>
        _byIndex.TryGetValue(index, out entry);

    public bool TryGetEntry(string name, [MaybeNullWhen(false)] out OdEntry entry) =>
        _byName.TryGetValue(name, out entry);

    /// <summary>
    /// The variable at index/subindex: the entry itself for plain variables, or the
    /// member of a record or array. Null when there is no such variable.
    /// </summary>
    public OdVariable? GetVariable(int index, int subindex = 0)
    {
        if (!_byIndex.TryGetValue(index, out var entry))
        {
            return null;
        }

        return entry switch
        {
            OdVariable variable when subindex == 0 => variable,
            OdRecord record when record.TryGetMember(subindex, out var member) => member,
            OdArray array => TrySynthesized(array, subindex),
            _ => null,
        };
    }

    private static OdVariable? TrySynthesized(OdArray array, int subindex)
    {
        try
        {
            return array[subindex];
        }
        catch (KeyNotFoundException)
        {
            return null;
        }
    }

    /// <summary>
    /// Sets the configured value (the DCF <c>ParameterValue</c>) of the variable at
    /// <paramref name="index"/>/<paramref name="subIndex"/>, so a subsequent write
    /// emits it. Throws when there is no such variable.
    /// </summary>
    public void SetValue(int index, int subIndex, OdValue value)
    {
        var variable = GetVariable(index, subIndex)
            ?? throw new KeyNotFoundException(
                $"The object dictionary has no variable at 0x{index:X4}sub{subIndex:X}.");

        variable.Value = value;
        variable.ValueIsOverridden = true;
    }

    /// <summary>Sets the configured value of the entry named <paramref name="name"/>.</summary>
    public void SetValue(string name, OdValue value, int subIndex = 0) =>
        SetValue(this[name].Index, subIndex, value);

    internal void Add(OdEntry entry)
    {
        _byIndex[entry.Index] = entry;
        _byName[entry.Name] = entry;
    }
}
