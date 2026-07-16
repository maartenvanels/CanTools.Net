namespace CanTools.Formats.Dbc;

// Raw parsed statements; all values are kept as token strings because several
// upstream quirks depend on the exact text (e.g. "[0|0]" collapsing to no range).

internal sealed record DbcSignalStatement(
    string Name,
    string? MuxIndicator,
    string Start,
    string Length,
    string ByteOrder,
    string Sign,
    string Scale,
    string Offset,
    string Minimum,
    string Maximum,
    string Unit,
    List<string> Receivers);

internal sealed record DbcMessageStatement(
    string FrameId,
    string Name,
    string Length,
    string Sender,
    List<DbcSignalStatement> Signals);

internal sealed record DbcCommentStatement(
    string Target,      // "", "BU_", "BO_", "SG_" or "EV_"
    string? FrameId,
    string? Name,       // node, signal or environment variable name
    string Text);

internal sealed record DbcAttributeDefinitionStatement(
    string? Kind,       // "BU_", "BO_", "SG_", "EV_" or a relation kind
    string Name,
    string TypeName,
    List<string> StringValues,
    List<string> NumberValues);

internal sealed record DbcAttributeDefaultStatement(string Name, string Value);

internal sealed record DbcAttributeStatement(
    string Name,
    string Target,      // "", "BU_", "BO_", "SG_" or "EV_"
    string? FrameId,
    string? TargetName, // node, signal or environment variable name
    string Value);

internal sealed record DbcRelationAttributeStatement(
    string Name,
    string RelationKind, // "BU_SG_REL_" or "BU_BO_REL_"
    string NodeName,
    string FrameId,
    string? SignalName,
    string Value);

internal sealed record DbcChoicesStatement(
    string? FrameId,    // null for the environment-variable form
    string Name,
    List<(string Value, string Text)> Pairs);

internal sealed record DbcValueTableStatement(
    string Name,
    List<(string Value, string Text)> Pairs);

internal sealed record DbcSignalTypeStatement(string FrameId, string SignalName, string Type);

internal sealed record DbcMultiplexerValuesStatement(
    string FrameId,
    string SignalName,
    string MultiplexerSignal,
    List<(string Lower, string Upper)> Ranges);

internal sealed record DbcSignalGroupStatement(
    string FrameId,
    string Name,
    string Repetitions,
    List<string> SignalNames);

internal sealed record DbcEnvironmentVariableStatement(
    string Name,
    string EnvType,
    string Minimum,
    string Maximum,
    string Unit,
    string InitialValue,
    string EnvId,
    string AccessType,
    string AccessNode);

internal sealed record DbcMessageSendersStatement(string FrameId, List<string> Senders);

/// <summary>All statements of one DBC file, in file order per kind.</summary>
internal sealed class DbcFileContents
{
    public string? Version;
    public List<string>? Nodes;
    public readonly List<DbcMessageStatement> Messages = [];
    public readonly List<DbcCommentStatement> Comments = [];
    public readonly List<DbcAttributeDefinitionStatement> AttributeDefinitions = [];
    public readonly List<DbcAttributeDefaultStatement> AttributeDefaults = [];
    public readonly List<DbcAttributeStatement> Attributes = [];
    public readonly List<DbcAttributeDefinitionStatement> RelationAttributeDefinitions = [];
    public readonly List<DbcAttributeDefaultStatement> RelationAttributeDefaults = [];
    public readonly List<DbcRelationAttributeStatement> RelationAttributes = [];
    public readonly List<DbcChoicesStatement> Choices = [];
    public readonly List<DbcValueTableStatement> ValueTables = [];
    public readonly List<DbcSignalTypeStatement> SignalTypes = [];
    public readonly List<DbcMultiplexerValuesStatement> MultiplexerValues = [];
    public readonly List<DbcSignalGroupStatement> SignalGroups = [];
    public readonly List<DbcEnvironmentVariableStatement> EnvironmentVariables = [];
    public readonly List<DbcMessageSendersStatement> MessageSenders = [];
}
