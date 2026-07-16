using CanTools.Packing;

namespace CanTools.Model;

/// <summary>
/// A CAN signal: position, size, byte order, scaling and multiplexing information.
/// A signal is part of a message.
/// </summary>
public sealed class Signal : ICodecField
{
    public Signal(
        string name,
        int start,
        int length,
        ByteOrder byteOrder = ByteOrder.LittleEndian,
        bool isSigned = false,
        SignalValue? rawInitial = null,
        SignalValue? rawInvalid = null,
        Conversion? conversion = null,
        double? minimum = null,
        double? maximum = null,
        string? unit = null,
        Comments? comment = null,
        IReadOnlyList<string>? receivers = null,
        bool isMultiplexer = false,
        IReadOnlyList<long>? multiplexerIds = null,
        string? multiplexerSignal = null,
        long? spn = null)
    {
        Name = name ?? throw new ArgumentNullException(nameof(name));
        StartBit = start;
        Length = length;
        ByteOrder = byteOrder;
        IsSigned = isSigned;
        Conversion = conversion ?? Conversion.Create();
        RawInitial = rawInitial;
        Initial = rawInitial is { } initial ? Conversion.RawToScaled(initial) : (SignalValue?)null;
        RawInvalid = rawInvalid;
        Invalid = rawInvalid is { } invalid ? Conversion.RawToScaled(invalid) : (SignalValue?)null;
        Minimum = minimum;
        Maximum = maximum;
        Unit = unit;
        Comments = comment;
        Receivers = receivers ?? [];
        IsMultiplexer = isMultiplexer;
        MultiplexerIds = multiplexerIds;
        MultiplexerSignal = multiplexerSignal;
        Spn = spn;
    }

    public string Name { get; }

    /// <summary>DBC start bit: LSB position for little endian, MSB position for big endian.</summary>
    public int StartBit { get; }

    /// <summary>The size of the signal in bits.</summary>
    public int Length { get; }

    public ByteOrder ByteOrder { get; }

    /// <summary>True if the signal is signed. Ignored when <see cref="IsFloat"/> is true.</summary>
    public bool IsSigned { get; }

    /// <summary>Converts between raw and scaled values; also carries choices and floatness.</summary>
    public Conversion Conversion { get; set; }

    /// <summary>The raw initial value, or null if unavailable.</summary>
    public SignalValue? RawInitial { get; }

    /// <summary>The scaled initial value, or null if unavailable.</summary>
    public SignalValue? Initial { get; }

    /// <summary>The raw value marking the signal as invalid, or null if unavailable.</summary>
    public SignalValue? RawInvalid { get; }

    /// <summary>The scaled value marking the signal as invalid, or null if unavailable.</summary>
    public SignalValue? Invalid { get; }

    /// <summary>The scaled minimum value, or null if unavailable.</summary>
    public double? Minimum { get; }

    /// <summary>The scaled maximum value, or null if unavailable.</summary>
    public double? Maximum { get; }

    public string? Unit { get; }

    /// <summary>The signal comment, or null if unavailable.</summary>
    public string? Comment => Comments?.Resolve();

    public Comments? Comments { get; }

    /// <summary>The names of all nodes receiving this signal.</summary>
    public IReadOnlyList<string> Receivers { get; }

    /// <summary>True if this signal selects the active multiplexed layer of its message.</summary>
    public bool IsMultiplexer { get; }

    /// <summary>The multiplexer values for which this signal is present, or null if unmultiplexed.</summary>
    public IReadOnlyList<long>? MultiplexerIds { get; }

    /// <summary>The name of the multiplexer selector this signal depends on, or null.</summary>
    public string? MultiplexerSignal { get; }

    /// <summary>The J1939 Suspect Parameter Number, or null if unavailable.</summary>
    public long? Spn { get; }

    /// <summary>DBC-specific properties such as attributes, or null.</summary>
    public DbcSpecifics? Dbc { get; init; }

    public double Scale => Conversion.Scale;

    public double Offset => Conversion.Offset;

    public IReadOnlyDictionary<long, NamedSignalValue>? Choices => Conversion.Choices;

    public bool IsFloat => Conversion.IsFloat;

    public SignalValue RawToScaled(SignalValue rawValue, bool decodeChoices = true) =>
        Conversion.RawToScaled(rawValue, decodeChoices);

    public SignalValue ScaledToRaw(SignalValue scaledValue) =>
        Conversion.ScaledToRaw(scaledValue);

    public long ChoiceToNumber(string choice)
    {
        try
        {
            return Conversion.ChoiceToNumber(choice);
        }
        catch (KeyNotFoundException exception)
        {
            throw new KeyNotFoundException(
                $"Choice {choice} not found in Signal {Name}.", exception);
        }
    }

    public override string ToString() => $"Signal {Name} ({StartBit}|{Length}, {ByteOrder})";
}
