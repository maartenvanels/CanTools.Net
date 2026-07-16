using System.Globalization;

namespace CanTools.CanOpen;

/// <summary>
/// A decoded object dictionary value: an integer, a floating point number, a string,
/// or raw bytes (octet strings and domains). Numeric values of different kinds
/// compare equal when they represent the same number.
/// </summary>
public readonly struct OdValue : IEquatable<OdValue>
{
    private enum Kind : byte
    {
        Int64,
        UInt64,
        Double,
        Text,
        Bytes,
    }

    private readonly Kind _kind;
    private readonly ulong _bits;
    private readonly object? _reference;

    private OdValue(Kind kind, ulong bits, object? reference)
    {
        _kind = kind;
        _bits = bits;
        _reference = reference;
    }

    public static implicit operator OdValue(long value) => new(Kind.Int64, (ulong)value, null);

    public static implicit operator OdValue(int value) => (long)value;

    public static implicit operator OdValue(ulong value) => new(Kind.UInt64, value, null);

    public static implicit operator OdValue(double value) =>
        new(Kind.Double, BitConverter.DoubleToUInt64Bits(value), null);

    public static implicit operator OdValue(string text) =>
        new(Kind.Text, 0, text ?? throw new ArgumentNullException(nameof(text)));

    public static implicit operator OdValue(byte[] data) =>
        new(Kind.Bytes, 0, data ?? throw new ArgumentNullException(nameof(data)));

    public bool IsNumeric => _kind is Kind.Int64 or Kind.UInt64 or Kind.Double;

    public bool IsInteger => _kind is Kind.Int64 or Kind.UInt64;

    /// <summary>The string value, or null when this is not a string.</summary>
    public string? Text => _reference as string;

    /// <summary>The raw bytes, or null when this is not an octet string or domain.</summary>
    public byte[]? Bytes => _reference as byte[];

    public double ToDouble() => _kind switch
    {
        Kind.Int64 => (long)_bits,
        Kind.UInt64 => _bits,
        Kind.Double => BitConverter.UInt64BitsToDouble(_bits),
        _ => throw new InvalidOperationException("The value is not numeric."),
    };

    public long ToInt64() => _kind switch
    {
        Kind.Int64 => (long)_bits,
        Kind.UInt64 => checked((long)_bits),
        Kind.Double => checked((long)BitConverter.UInt64BitsToDouble(_bits)),
        _ => throw new InvalidOperationException("The value is not numeric."),
    };

    public ulong ToUInt64() => _kind switch
    {
        Kind.Int64 => checked((ulong)(long)_bits),
        Kind.UInt64 => _bits,
        Kind.Double => checked((ulong)BitConverter.UInt64BitsToDouble(_bits)),
        _ => throw new InvalidOperationException("The value is not numeric."),
    };

    public bool Equals(OdValue other)
    {
        if (_kind == Kind.Text || other._kind == Kind.Text)
        {
            return _kind == other._kind && (string)_reference! == (string)other._reference!;
        }

        if (_kind == Kind.Bytes || other._kind == Kind.Bytes)
        {
            return _kind == other._kind
                   && ((byte[])_reference!).AsSpan().SequenceEqual((byte[])other._reference!);
        }

        if (_kind == other._kind)
        {
            return _bits == other._bits;
        }

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
        OdValue value => Equals(value),
        long value => Equals((OdValue)value),
        int value => Equals((OdValue)value),
        ulong value => Equals((OdValue)value),
        double value => Equals((OdValue)value),
        string value => Equals((OdValue)value),
        byte[] value => Equals((OdValue)value),
        _ => false,
    };

    public override int GetHashCode() => _kind switch
    {
        Kind.Text => _reference!.GetHashCode(),
        Kind.Bytes => ((byte[])_reference!).Length,
        _ => ToDouble().GetHashCode(),
    };

    public static bool operator ==(OdValue left, OdValue right) => left.Equals(right);

    public static bool operator !=(OdValue left, OdValue right) => !left.Equals(right);

    public override string ToString() => _kind switch
    {
        Kind.Int64 => ((long)_bits).ToString(CultureInfo.InvariantCulture),
        Kind.UInt64 => _bits.ToString(CultureInfo.InvariantCulture),
        Kind.Double => BitConverter.UInt64BitsToDouble(_bits).ToString(CultureInfo.InvariantCulture),
        Kind.Text => (string)_reference!,
        _ => Convert.ToHexString((byte[])_reference!),
    };
}
