using System.Globalization;

namespace CanTools;

/// <summary>
/// The value of a signal: an integer, a floating point number, a choice label, or a
/// named value from a value table. Mirrors cantools' int | float | str | NamedSignalValue
/// decode results. Numeric values of different kinds compare equal when they represent
/// the same number; labels and named values compare equal by label.
/// </summary>
public readonly struct SignalValue : IEquatable<SignalValue>
{
    private enum Kind : byte
    {
        Int64,
        UInt64,
        Double,
        Label,
        Named,
    }

    private readonly Kind _kind;
    private readonly ulong _bits;
    private readonly object? _reference;

    private SignalValue(Kind kind, ulong bits, object? reference)
    {
        _kind = kind;
        _bits = bits;
        _reference = reference;
    }

    public static implicit operator SignalValue(long value) => new(Kind.Int64, (ulong)value, null);

    public static implicit operator SignalValue(int value) => (long)value;

    public static implicit operator SignalValue(ulong value) => new(Kind.UInt64, value, null);

    public static implicit operator SignalValue(double value) =>
        new(Kind.Double, BitConverter.DoubleToUInt64Bits(value), null);

    public static implicit operator SignalValue(string label) =>
        new(Kind.Label, 0, label ?? throw new ArgumentNullException(nameof(label)));

    public static implicit operator SignalValue(NamedSignalValue named) =>
        new(Kind.Named, 0, named ?? throw new ArgumentNullException(nameof(named)));

    public bool IsNumeric => _kind is Kind.Int64 or Kind.UInt64 or Kind.Double;

    public bool IsInteger => _kind is Kind.Int64 or Kind.UInt64;

    public bool IsLabel => _kind is Kind.Label or Kind.Named;

    /// <summary>The label when this value is a choice label or named value, otherwise null.</summary>
    public string? Label => _kind switch
    {
        Kind.Label => (string)_reference!,
        Kind.Named => ((NamedSignalValue)_reference!).Name,
        _ => null,
    };

    /// <summary>The named value when this value came from a value table, otherwise null.</summary>
    public NamedSignalValue? Named => _reference as NamedSignalValue;

    public double ToDouble() => _kind switch
    {
        Kind.Int64 => (long)_bits,
        Kind.UInt64 => _bits,
        Kind.Double => BitConverter.UInt64BitsToDouble(_bits),
        _ => throw new InvalidOperationException($"'{Label}' is not a numeric value."),
    };

    public long ToInt64() => _kind switch
    {
        Kind.Int64 => (long)_bits,
        Kind.UInt64 => checked((long)_bits),
        Kind.Double => checked((long)BitConverter.UInt64BitsToDouble(_bits)),
        _ => throw new InvalidOperationException($"'{Label}' is not a numeric value."),
    };

    public ulong ToUInt64() => _kind switch
    {
        Kind.Int64 => checked((ulong)(long)_bits),
        Kind.UInt64 => _bits,
        Kind.Double => checked((ulong)BitConverter.UInt64BitsToDouble(_bits)),
        _ => throw new InvalidOperationException($"'{Label}' is not a numeric value."),
    };

    public bool Equals(SignalValue other)
    {
        if (IsLabel || other.IsLabel)
        {
            if (_kind == Kind.Named && other._kind == Kind.Named)
            {
                return ((NamedSignalValue)_reference!).Equals((NamedSignalValue)other._reference!);
            }

            return IsLabel && other.IsLabel && Label == other.Label;
        }

        if (_kind == other._kind)
        {
            return _bits == other._bits;
        }

        // Mixed numeric kinds compare by value, like Python's 1 == 1.0.
        if (_kind == Kind.UInt64 && other._kind == Kind.Int64)
        {
            return (long)other._bits >= 0 && _bits == other._bits;
        }

        if (_kind == Kind.Int64 && other._kind == Kind.UInt64)
        {
            return (long)_bits >= 0 && _bits == other._bits;
        }

        return ToDouble() == other.ToDouble();
    }

    public override bool Equals(object? obj) => obj switch
    {
        SignalValue value => Equals(value),
        long value => Equals((SignalValue)value),
        int value => Equals((SignalValue)value),
        ulong value => Equals((SignalValue)value),
        double value => Equals((SignalValue)value),
        string value => Equals((SignalValue)value),
        NamedSignalValue value => Equals((SignalValue)value),
        _ => false,
    };

    public override int GetHashCode() =>
        IsLabel ? Label!.GetHashCode() : ToDouble().GetHashCode();

    public static bool operator ==(SignalValue left, SignalValue right) => left.Equals(right);

    public static bool operator !=(SignalValue left, SignalValue right) => !left.Equals(right);

    public override string ToString() => _kind switch
    {
        Kind.Int64 => ((long)_bits).ToString(CultureInfo.InvariantCulture),
        Kind.UInt64 => _bits.ToString(CultureInfo.InvariantCulture),
        Kind.Double => BitConverter.UInt64BitsToDouble(_bits).ToString(CultureInfo.InvariantCulture),
        _ => Label!,
    };
}
