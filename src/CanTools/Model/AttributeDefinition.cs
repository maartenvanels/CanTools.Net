namespace CanTools.Model;

/// <summary>
/// The definition of a DBC attribute (BA_DEF_): its object kind, value type, bounds,
/// enum choices and default value.
/// </summary>
public sealed class AttributeDefinition
{
    public AttributeDefinition(
        string name,
        SignalValue? defaultValue = null,
        string? kind = null,
        string? typeName = null,
        double? minimum = null,
        double? maximum = null,
        IReadOnlyList<string>? choices = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        DefaultValue = defaultValue;
        Kind = kind;
        TypeName = typeName;
        Minimum = minimum;
        Maximum = maximum;
        Choices = choices ?? [];
    }

    public string Name { get; }

    /// <summary>The default value from BA_DEF_DEF_, or null if unavailable.</summary>
    public SignalValue? DefaultValue { get; }

    /// <summary>The object kind the attribute applies to (BU_, BO_, SG_, EV_), or null for the database itself.</summary>
    public string? Kind { get; }

    /// <summary>The value type: INT, HEX, FLOAT, STRING or ENUM.</summary>
    public string? TypeName { get; }

    public double? Minimum { get; }

    public double? Maximum { get; }

    /// <summary>The allowed values for ENUM attributes.</summary>
    public IReadOnlyList<string> Choices { get; }

    public override string ToString() => $"AttributeDefinition {Name} ({TypeName ?? "untyped"})";
}
