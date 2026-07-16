namespace CanTools.CanOpen;

/// <summary>An object dictionary record: named members at explicit subindexes.</summary>
public sealed class OdRecord : OdEntry
{
    private readonly SortedDictionary<int, OdVariable> _bySubindex = [];
    private readonly Dictionary<string, OdVariable> _byName = [];

    public OdRecord(int index, string name)
        : base(index, name)
    {
    }

    /// <summary>The members in subindex order.</summary>
    public IReadOnlyCollection<OdVariable> Members => _bySubindex.Values;

    public OdVariable this[int subindex] =>
        _bySubindex.TryGetValue(subindex, out var member)
            ? member
            : throw new KeyNotFoundException(
                $"Record 0x{Index:X4} has no member at subindex {subindex}.");

    public OdVariable this[string name] =>
        _byName.TryGetValue(name, out var member)
            ? member
            : throw new KeyNotFoundException($"Record 0x{Index:X4} has no member named '{name}'.");

    public bool TryGetMember(int subindex, out OdVariable? member) =>
        _bySubindex.TryGetValue(subindex, out member);

    internal void AddMember(OdVariable member)
    {
        _bySubindex[member.Subindex] = member;
        _byName[member.Name] = member;
    }
}
