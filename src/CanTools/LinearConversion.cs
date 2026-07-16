namespace CanTools;

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

    public override SignalValue ScaledToRaw(SignalValue scaledValue)
    {
        if (!scaledValue.IsNumeric)
        {
            throw new ArgumentException($"A numeric value is required, got '{scaledValue}'.");
        }

        return NumericScaledToRaw(scaledValue);
    }

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
