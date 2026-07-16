namespace CanTools.Model;

/// <summary>A DBC environment variable (EV_).</summary>
public sealed class EnvironmentVariable
{
    public EnvironmentVariable(
        string name,
        int envType,
        double minimum,
        double maximum,
        string unit,
        double initialValue,
        int envId,
        string accessType,
        string accessNode,
        string? comment = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        EnvType = envType;
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit;
        InitialValue = initialValue;
        EnvId = envId;
        AccessType = accessType;
        AccessNode = accessNode;
        Comment = comment;
    }

    public string Name { get; }

    public int EnvType { get; }

    public double Minimum { get; }

    public double Maximum { get; }

    public string Unit { get; }

    public double InitialValue { get; }

    public int EnvId { get; }

    public string AccessType { get; }

    public string AccessNode { get; }

    public string? Comment { get; }

    /// <summary>DBC-specific properties such as attributes, or null.</summary>
    public DbcSpecifics? Dbc { get; init; }

    public override string ToString() => $"EnvironmentVariable {Name}";
}
