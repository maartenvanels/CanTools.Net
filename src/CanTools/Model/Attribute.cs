namespace CanTools.Model;

/// <summary>A DBC attribute value (BA_) attached to a database, node, message or signal.</summary>
public sealed class Attribute
{
    public Attribute(SignalValue value, AttributeDefinition definition)
    {
        Value = value;
        Definition = definition ?? throw new ArgumentNullException(nameof(definition));
    }

    public string Name => Definition.Name;

    public SignalValue Value { get; }

    public AttributeDefinition Definition { get; }

    public override string ToString() => $"{Name} = {Value}";
}
