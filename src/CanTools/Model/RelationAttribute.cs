namespace CanTools.Model;

/// <summary>
/// A relation attribute (BA_REL_): a node-to-signal value (BU_SG_REL_, with a
/// signal name) or a node-to-message value (BU_BO_REL_, without one).
/// </summary>
public sealed class RelationAttribute
{
    public RelationAttribute(string nodeName, Attribute attribute, string? signalName = null)
    {
        NodeName = nodeName ?? throw new ArgumentNullException(nameof(nodeName));
        Attribute = attribute ?? throw new ArgumentNullException(nameof(attribute));
        SignalName = signalName;
    }

    /// <summary>The signal of the relation, or null for node-to-message attributes.</summary>
    public string? SignalName { get; internal set; }

    public string NodeName { get; }

    public Attribute Attribute { get; }

    public override string ToString() =>
        SignalName is null
            ? $"{Attribute} (node {NodeName})"
            : $"{Attribute} (node {NodeName}, signal {SignalName})";
}
