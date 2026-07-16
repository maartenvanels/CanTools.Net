namespace CanTools.Model;

/// <summary>
/// DBC-format-specific properties of a database, node, message or signal:
/// attributes, attribute definitions and (at database level) environment
/// variables and value tables. Dictionaries preserve file order.
/// </summary>
public sealed class DbcSpecifics
{
    public DbcSpecifics(
        IReadOnlyDictionary<string, Attribute>? attributes = null,
        IReadOnlyDictionary<string, AttributeDefinition>? attributeDefinitions = null,
        IReadOnlyDictionary<string, EnvironmentVariable>? environmentVariables = null,
        IReadOnlyDictionary<string, IReadOnlyDictionary<long, NamedSignalValue>>? valueTables = null,
        IReadOnlyDictionary<long, IReadOnlyList<RelationAttribute>>? attributesRel = null,
        IReadOnlyDictionary<string, AttributeDefinition>? attributeDefinitionsRel = null)
    {
        Attributes = attributes ?? new Dictionary<string, Attribute>();
        AttributeDefinitions = attributeDefinitions ?? new Dictionary<string, AttributeDefinition>();
        EnvironmentVariables = environmentVariables ?? new Dictionary<string, EnvironmentVariable>();
        ValueTables = valueTables
            ?? new Dictionary<string, IReadOnlyDictionary<long, NamedSignalValue>>();
        AttributesRel = attributesRel ?? new Dictionary<long, IReadOnlyList<RelationAttribute>>();
        AttributeDefinitionsRel = attributeDefinitionsRel
            ?? new Dictionary<string, AttributeDefinition>();
    }

    /// <summary>The attributes of the parent object.</summary>
    public IReadOnlyDictionary<string, Attribute> Attributes { get; }

    /// <summary>All attribute definitions. Only populated at database level.</summary>
    public IReadOnlyDictionary<string, AttributeDefinition> AttributeDefinitions { get; }

    /// <summary>All environment variables. Only populated at database level.</summary>
    public IReadOnlyDictionary<string, EnvironmentVariable> EnvironmentVariables { get; }

    /// <summary>All named value tables (VAL_TABLE_). Only populated at database level.</summary>
    public IReadOnlyDictionary<string, IReadOnlyDictionary<long, NamedSignalValue>> ValueTables { get; }

    /// <summary>Relation attributes (BA_REL_) keyed by raw frame id. Only at database level.</summary>
    public IReadOnlyDictionary<long, IReadOnlyList<RelationAttribute>> AttributesRel { get; }

    /// <summary>Relation attribute definitions (BA_DEF_REL_). Only at database level.</summary>
    public IReadOnlyDictionary<string, AttributeDefinition> AttributeDefinitionsRel { get; }
}
