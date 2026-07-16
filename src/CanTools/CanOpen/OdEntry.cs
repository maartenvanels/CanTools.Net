namespace CanTools.CanOpen;

/// <summary>
/// One object dictionary entry: a plain variable, an array, or a record.
/// </summary>
public abstract class OdEntry
{
    private protected OdEntry(int index, string name)
    {
        Index = index;
        Name = name ?? throw new ArgumentNullException(nameof(name));
    }

    /// <summary>The 16-bit object dictionary index.</summary>
    public int Index { get; }

    public string Name { get; }

    public string? StorageLocation { get; internal set; }

    /// <summary>Non-standard options of the defining section, verbatim.</summary>
    public IReadOnlyDictionary<string, string> CustomOptions { get; internal set; } =
        new Dictionary<string, string>();

    public override string ToString() => $"{GetType().Name.Replace("Od", "")} 0x{Index:X4} {Name}";
}
