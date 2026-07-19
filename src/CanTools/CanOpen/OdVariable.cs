namespace CanTools.CanOpen;

/// <summary>A single object dictionary variable (an index or one subindex).</summary>
public sealed class OdVariable : OdEntry
{
    public OdVariable(int index, int subindex, string name)
        : base(index, name)
    {
        Subindex = subindex;
    }

    public int Subindex { get; }

    public CanOpenDataType DataType { get; internal set; }

    /// <summary>The lowercased access type: ro, rw, wo, const, rww or rwr.</summary>
    public string AccessType { get; internal set; } = "rw";

    public bool IsReadable => AccessType.Contains('r') || AccessType == "const";

    public bool IsWritable => AccessType.Contains('w');

    /// <summary>True when the defining ObjectType was DOMAIN, regardless of DataType.</summary>
    public bool IsDomain { get; internal set; }

    public bool PdoMappable { get; internal set; }

    /// <summary>The LowLimit, or null. Signed types reinterpret hex two's-complement.</summary>
    public long? Minimum { get; internal set; }

    /// <summary>The HighLimit, or null.</summary>
    public long? Maximum { get; internal set; }

    /// <summary>The decoded DefaultValue, or null when absent or invalid.</summary>
    public OdValue? Default { get; internal set; }

    /// <summary>The DefaultValue text exactly as written, or null when absent.</summary>
    public string? DefaultRaw { get; internal set; }

    /// <summary>True when the DefaultValue contains a $NODEID expression.</summary>
    public bool IsRelative { get; internal set; }

    /// <summary>The decoded DCF ParameterValue, or null when absent or invalid.</summary>
    public OdValue? Value { get; internal set; }

    /// <summary>The ParameterValue text exactly as written, or null when absent.</summary>
    public string? ValueRaw { get; internal set; }

    /// <summary>
    /// True when <see cref="Value"/> was assigned through the API rather than read
    /// from the file, so a writer formats it afresh instead of echoing
    /// <see cref="ValueRaw"/>.
    /// </summary>
    internal bool ValueIsOverridden { get; set; }

    /// <summary>Non-standard scaling factor extension; 1 when absent.</summary>
    public double Factor { get; internal set; } = 1;

    public string Description { get; internal set; } = "";

    public string Unit { get; internal set; } = "";

    /// <summary>The size in bits, or 8 for types without a fixed size.</summary>
    public int BitLength => DataType.BitLength() ?? 8;

    public override string ToString() => $"Variable 0x{Index:X4}sub{Subindex:X} {Name}";
}
