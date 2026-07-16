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
    public virtual SignalValue ScaledToRaw(SignalValue scaledValue)
    {
        if (!scaledValue.IsNumeric)
        {
            throw new ArgumentException($"A numeric value is required, got '{scaledValue}'.");
        }

        return NumericScaledToRaw(scaledValue);
    }

    /// <summary>Converts a numeric scaled value to the raw value.</summary>
    public abstract SignalValue NumericScaledToRaw(SignalValue scaledValue);

    /// <summary>Looks up the raw value for a choice label.</summary>
    public virtual long ChoiceToNumber(string label) =>
        throw new KeyNotFoundException($"There is no choice named '{label}'.");

    // Python's round(): banker's rounding, result is an integer.
    private protected static long Round(double value) =>
        checked((long)Math.Round(value, MidpointRounding.ToEven));

    internal static SignalValue RoundUnlessFloat(SignalValue value, bool isFloat)
    {
        if (isFloat || value.IsInteger)
        {
            return value;
        }

        return Round(value.ToDouble());
    }
}

/// <summary>Conversion with scale 1 and offset 0: raw and scaled values are identical.</summary>
internal sealed class IdentityConversion : Conversion
{
    public IdentityConversion(bool isFloat)
    {
        IsFloat = isFloat;
    }

    public override double Scale => 1;

    public override double Offset => 0;

    public override bool IsFloat { get; }

    public override SignalValue RawToScaled(SignalValue rawValue, bool decodeChoices = true) => rawValue;

    public override SignalValue NumericScaledToRaw(SignalValue scaledValue) =>
        RoundUnlessFloat(scaledValue, IsFloat);

    public override string ToString() => IsFloat ? "raw (float)" : "raw";
}

/// <summary>Linear conversion using floating point arithmetic.</summary>
internal sealed class LinearConversion : Conversion
{
    public LinearConversion(double scale, double offset, bool isFloat)
    {
        Scale = scale;
        Offset = offset;
        IsFloat = isFloat;
    }

    public override double Scale { get; }

    public override double Offset { get; }

    public override bool IsFloat { get; }

    public override SignalValue RawToScaled(SignalValue rawValue, bool decodeChoices = true) =>
        rawValue.ToDouble() * Scale + Offset;

    public override SignalValue NumericScaledToRaw(SignalValue scaledValue)
    {
        var raw = (scaledValue.ToDouble() - Offset) / Scale;

        if (IsFloat)
        {
            return raw;
        }

        return Round(raw);
    }

    public override string ToString() => $"raw * {Scale} + {Offset}";
}

/// <summary>
/// Linear conversion whose scale and offset are integers, allowing exact integer
/// arithmetic without floating point precision loss.
/// </summary>
internal sealed class LinearIntegerConversion : Conversion
{
    private readonly long _scale;
    private readonly long _offset;

    public LinearIntegerConversion(long scale, long offset)
    {
        _scale = scale;
        _offset = offset;
    }

    public override double Scale => _scale;

    public override double Offset => _offset;

    public override bool IsFloat => false;

    public override SignalValue RawToScaled(SignalValue rawValue, bool decodeChoices = true)
    {
        if (rawValue.IsInteger)
        {
            return rawValue.ToInt64() * _scale + _offset;
        }

        return rawValue.ToDouble() * _scale + _offset;
    }

    public override SignalValue NumericScaledToRaw(SignalValue scaledValue)
    {
        // Only divide via floating point when the value is not an exact multiple of
        // the scale; this mirrors cantools' divmod trick to avoid precision loss.
        if (scaledValue.IsInteger)
        {
            var value = scaledValue.ToInt64() - _offset;
            var quotient = Math.DivRem(value, _scale, out var remainder);

            return remainder == 0 ? quotient : Round((double)value / _scale);
        }

        var difference = scaledValue.ToDouble() - _offset;
        var floorQuotient = Math.Floor(difference / _scale);

        if (difference - floorQuotient * _scale == 0)
        {
            return Round(floorQuotient);
        }

        return Round(difference / _scale);
    }

    public override string ToString() => $"raw * {_scale} + {_offset}";
}

/// <summary>
/// Conversion for signals with a value table: raw values with a named choice decode to
/// that choice, all other values fall back to the underlying linear conversion.
/// </summary>
internal sealed class NamedSignalConversion : Conversion
{
    private readonly Conversion _conversion;
    private readonly IReadOnlyDictionary<long, NamedSignalValue> _choices;
    private readonly Dictionary<string, long> _labelToValue;

    public NamedSignalConversion(
        double scale,
        double offset,
        IReadOnlyDictionary<long, NamedSignalValue> choices,
        bool isFloat)
    {
        _conversion = Create(scale, offset, choices: null, isFloat);
        _choices = choices;
        _labelToValue = new Dictionary<string, long>(choices.Count);

        foreach (var (value, named) in choices)
        {
            _labelToValue[named.Name] = value;
        }
    }

    public override double Scale => _conversion.Scale;

    public override double Offset => _conversion.Offset;

    public override bool IsFloat => _conversion.IsFloat;

    public override IReadOnlyDictionary<long, NamedSignalValue> Choices => _choices;

    public override SignalValue RawToScaled(SignalValue rawValue, bool decodeChoices = true)
    {
        if (decodeChoices && TryGetChoice(rawValue, out var choice))
        {
            return choice;
        }

        return _conversion.RawToScaled(rawValue, decodeChoices: false);
    }

    // Like Python's dict lookup, an integral float raw value (a float signal with a
    // value table, as KCD allows) matches its integer choice key.
    internal bool TryGetChoice(SignalValue rawValue, out NamedSignalValue choice)
    {
        if (TryGetIntegral(rawValue, out var key) && _choices.TryGetValue(key, out choice!))
        {
            return true;
        }

        choice = null!;

        return false;
    }

    internal static bool TryGetIntegral(SignalValue value, out long key)
    {
        if (value.IsInteger)
        {
            key = value.ToInt64();

            return true;
        }

        if (value.IsNumeric)
        {
            var number = value.ToDouble();

            if (double.IsInteger(number) && number is >= long.MinValue and <= long.MaxValue)
            {
                key = (long)number;

                return true;
            }
        }

        key = 0;

        return false;
    }

    public override SignalValue ScaledToRaw(SignalValue scaledValue)
    {
        if (scaledValue.IsNumeric)
        {
            return _conversion.ScaledToRaw(scaledValue);
        }

        if (scaledValue.Named is { } named)
        {
            return named.Value;
        }

        return ChoiceToNumber(scaledValue.Label!);
    }

    public override SignalValue NumericScaledToRaw(SignalValue scaledValue) =>
        _conversion.NumericScaledToRaw(scaledValue);

    public override long ChoiceToNumber(string label)
    {
        if (!_labelToValue.TryGetValue(label, out var value))
        {
            throw new KeyNotFoundException($"There is no choice named '{label}'.");
        }

        return value;
    }
}
