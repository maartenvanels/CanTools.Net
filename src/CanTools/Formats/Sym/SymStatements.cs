namespace CanTools.Formats.Sym;

// Raw parsed statements; values are kept as token strings, like the DBC statements.

internal sealed record SymSignalDefinition(
    string Name,
    string TypeName,
    string? LengthText,
    bool IsBigEndian,
    List<(string Prefix, string Value)> Attributes,
    string? Comment,
    string? StartText = null);

internal sealed record SymMuxLine(
    string Name,
    string Start,
    string Length,
    string MuxIdText,
    bool IsBigEndian,
    string? Comment);

internal sealed class SymSymbol
{
    public SymSymbol(string name)
    {
        Name = name;
    }

    public string Name { get; }

    public string? IdText;
    public string? IdRangeEndText;
    public string? IdComment;
    public string? LengthText;
    public string? CycleTimeText;
    public string? TypeText;
    public readonly List<SymMuxLine> MuxLines = [];
    public readonly List<(string Name, string Start)> SignalReferences = [];
    public readonly List<SymSignalDefinition> Variables = [];
}

/// <summary>All statements of one SYM file, in file order per kind.</summary>
internal sealed class SymFileContents
{
    public string? Version;
    public readonly Dictionary<string, Dictionary<long, NamedSignalValue>> Enums = [];
    public readonly List<SymSignalDefinition> SignalTemplates = [];
    public readonly List<SymSymbol> Send = [];
    public readonly List<SymSymbol> Receive = [];
    public readonly List<SymSymbol> SendReceive = [];
}
