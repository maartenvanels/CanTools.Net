namespace CanTools;

/// <summary>
/// A named value of a signal: maps an integer raw value to a human-readable label,
/// optionally with per-language descriptions (used by ARXML).
/// </summary>
public sealed class NamedSignalValue : IEquatable<NamedSignalValue>
{
    public NamedSignalValue(long value, string name, IReadOnlyDictionary<string, string>? comments = null)
    {
        Value = value;
        Name = name ?? throw new ArgumentNullException(nameof(name));
        Comments = comments ?? new Dictionary<string, string>();
    }

    /// <summary>The integer raw value that is mapped.</summary>
    public long Value { get; }

    /// <summary>The human-readable label for the value.</summary>
    public string Name { get; }

    /// <summary>Descriptions of the named value, indexed by language.</summary>
    public IReadOnlyDictionary<string, string> Comments { get; }

    public bool Equals(NamedSignalValue? other)
    {
        if (other is null)
        {
            return false;
        }

        if (Value != other.Value || Name != other.Name || Comments.Count != other.Comments.Count)
        {
            return false;
        }

        foreach (var (language, comment) in Comments)
        {
            if (!other.Comments.TryGetValue(language, out var otherComment) || comment != otherComment)
            {
                return false;
            }
        }

        return true;
    }

    public override bool Equals(object? obj) => obj switch
    {
        NamedSignalValue named => Equals(named),
        string label => Name == label,
        _ => false,
    };

    public override int GetHashCode() => HashCode.Combine(Value, Name);

    public static bool operator ==(NamedSignalValue? left, string? right) =>
        left is null ? right is null : left.Name == right;

    public static bool operator !=(NamedSignalValue? left, string? right) => !(left == right);

    public static bool operator ==(string? left, NamedSignalValue? right) => right == left;

    public static bool operator !=(string? left, NamedSignalValue? right) => !(right == left);

    public override string ToString() => Name;
}
