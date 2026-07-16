namespace CanTools;

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
