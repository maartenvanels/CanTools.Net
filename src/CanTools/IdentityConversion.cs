namespace CanTools;

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

    public override SignalValue ScaledToRaw(SignalValue scaledValue)
    {
        if (!scaledValue.IsNumeric)
        {
            throw new ArgumentException($"A numeric value is required, got '{scaledValue}'.");
        }

        return NumericScaledToRaw(scaledValue);
    }

    public override SignalValue NumericScaledToRaw(SignalValue scaledValue) =>
        RoundUnlessFloat(scaledValue, IsFloat);

    public override string ToString() => IsFloat ? "raw (float)" : "raw";
}
