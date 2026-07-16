namespace CanTools;

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
        // Like Python's dict lookup, an integral float raw value (a float signal
        // with a value table, as KCD allows) matches its integer choice key.
        if (decodeChoices
            && TryGetIntegral(rawValue, out var key)
            && _choices.TryGetValue(key, out var choice))
        {
            return choice;
        }

        return _conversion.RawToScaled(rawValue, decodeChoices: false);
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
