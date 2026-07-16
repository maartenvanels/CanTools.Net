namespace CanTools;

/// <summary>
/// Converts between raw (on-the-wire) and scaled (physical) signal values, optionally
/// mapping raw values to named choices. Create instances through <see cref="Create"/>,
/// which picks the most precise implementation for the given parameters.
/// </summary>
public abstract class Conversion
{
    /// <summary>The scaling factor of the conversion.</summary>
    public abstract double Scale { get; }

    /// <summary>The offset of the conversion.</summary>
    public abstract double Offset { get; }

    /// <summary>True when the raw value is a floating point type instead of an integer.</summary>
    public abstract bool IsFloat { get; }

    /// <summary>Optional mapping of raw values to named choices.</summary>
    public virtual IReadOnlyDictionary<long, NamedSignalValue>? Choices => null;

    public static Conversion Create(
        double scale = 1,
        double offset = 0,
        IReadOnlyDictionary<long, NamedSignalValue>? choices = null,
        bool isFloat = false)
    {
        if (choices is not null)
        {
            return new NamedSignalConversion(scale, offset, choices, isFloat);
        }

        if (scale == 1 && offset == 0)
        {
            return new IdentityConversion(isFloat);
        }

        if (!isFloat && double.IsInteger(scale) && double.IsInteger(offset))
        {
            return new LinearIntegerConversion((long)scale, (long)offset);
        }

        return new LinearConversion(scale, offset, isFloat);
    }

    /// <summary>
    /// Converts a raw value to its scaled value, or to its named choice when
    /// <paramref name="decodeChoices"/> is true and the raw value has one.
    /// </summary>
    public abstract SignalValue RawToScaled(SignalValue rawValue, bool decodeChoices = true);

    /// <summary>Converts a scaled value, choice label or named value to the raw value.</summary>
    public abstract SignalValue ScaledToRaw(SignalValue scaledValue);

    /// <summary>Converts a numeric scaled value to the raw value.</summary>
    public abstract SignalValue NumericScaledToRaw(SignalValue scaledValue);

    /// <summary>Looks up the raw value for a choice label.</summary>
    public virtual long ChoiceToNumber(string label) =>
        throw new KeyNotFoundException($"There is no choice named '{label}'.");

    // Python's round(): banker's rounding, result is an integer.
    private protected static long Round(double value) =>
        checked((long)Math.Round(value, MidpointRounding.ToEven));

    private protected static SignalValue RoundUnlessFloat(SignalValue value, bool isFloat)
    {
        if (isFloat || value.IsInteger)
        {
            return value;
        }

        return Round(value.ToDouble());
    }
}
