using System.Diagnostics.CodeAnalysis;

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
    public int? Bitrate { get; internal set; }

    /// <summary>The commissioned node id, or null.</summary>
    public int? NodeId { get; internal set; }

    public DeviceInformation DeviceInformation { get; } = new();

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

    internal void Add(OdEntry entry)
    {
        _byIndex[entry.Index] = entry;
        _byName[entry.Name] = entry;
    }
}
