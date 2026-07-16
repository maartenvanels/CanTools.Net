namespace CanTools.CanOpen;

/// <summary>An object dictionary entry with subindexed members: an array or a record.</summary>
public abstract class OdComposite : OdEntry
{
    private readonly SortedDictionary<int, OdVariable> _bySubindex = [];
    private readonly Dictionary<string, OdVariable> _byName = [];

    private protected OdComposite(int index, string name)
        : base(index, name)
    {
    }

    /// <summary>The explicitly defined members in subindex order.</summary>
    public IReadOnlyCollection<OdVariable> Members => _bySubindex.Values;

    public virtual OdVariable this[int subindex] =>
        _bySubindex.TryGetValue(subindex, out var member)
            ? member
            : throw new KeyNotFoundException(
                $"{Kind} 0x{Index:X4} has no member at subindex {subindex}.");

    public OdVariable this[string name] =>
        _byName.TryGetValue(name, out var member)
            ? member
            : throw new KeyNotFoundException($"{Kind} 0x{Index:X4} has no member named '{name}'.");

    public bool TryGetMember(int subindex, out OdVariable? member) =>
        _bySubindex.TryGetValue(subindex, out member);

    internal void AddMember(OdVariable member)
    {
        _bySubindex[member.Subindex] = member;
        _byName[member.Name] = member;
    }

    // "Array"/"Record", the same word ToString uses.
    private protected string Kind => GetType().Name.Replace("Od", "");
}
