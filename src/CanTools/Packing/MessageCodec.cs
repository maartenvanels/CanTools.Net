namespace CanTools.Packing;

/// <summary>
/// Packs and unpacks the fields of one message layout into/from frame data. This is the
/// port of cantools' encode_data/decode_data plus the compiled bitstruct formats; instead
/// of two format strings that are OR-ed together, each field is written and read at its
/// absolute bit position directly.
/// </summary>
public sealed class MessageCodec
{
    private readonly FieldLayout[] _layouts;
    private readonly byte[] _paddingMask;

    private MessageCodec(ICodecField[] fields, int byteLength)
    {
        Fields = fields;
        ByteLength = byteLength;
        _layouts = new FieldLayout[fields.Length];
        _paddingMask = new byte[byteLength];
        _paddingMask.AsSpan().Fill(0xff);

        for (var i = 0; i < fields.Length; i++)
        {
            var layout = new FieldLayout(fields[i]);
            _layouts[i] = layout;

            if (layout.FitsIn(byteLength))
            {
                layout.ClearFieldBits(_paddingMask);
            }
        }
    }

    public static MessageCodec Create(IEnumerable<ICodecField> fields, int numberOfBytes)
    {
        ArgumentNullException.ThrowIfNull(fields);
        ArgumentOutOfRangeException.ThrowIfNegative(numberOfBytes);

        return new MessageCodec(fields.ToArray(), numberOfBytes);
    }

    public IReadOnlyList<ICodecField> Fields { get; }

    /// <summary>The size of the frame data in bytes.</summary>
    public int ByteLength { get; }

    /// <summary>Mask with a 1 for every bit that is not covered by any field.</summary>
    public ReadOnlySpan<byte> PaddingMask => _paddingMask;

    /// <summary>Encodes the signal values into a new frame data buffer.</summary>
    public byte[] Encode(IReadOnlyDictionary<string, SignalValue> signalValues, bool scaling = true)
    {
        var data = new byte[ByteLength];
        EncodeInto(signalValues, data, scaling);

        return data;
    }

    /// <summary>
    /// Encodes the signal values by OR-ing their bits into <paramref name="destination"/>.
    /// The buffer is not cleared first, so multiplexed layers can be combined by encoding
    /// them into the same buffer.
    /// </summary>
    public void EncodeInto(
        IReadOnlyDictionary<string, SignalValue> signalValues,
        Span<byte> destination,
        bool scaling = true)
    {
        if (destination.Length < ByteLength)
        {
            throw new ArgumentException(
                $"Destination is {destination.Length} bytes but the message needs {ByteLength}.",
                nameof(destination));
        }

        foreach (var layout in _layouts)
        {
            var field = layout.Field;

            if (!signalValues.TryGetValue(field.Name, out var value))
            {
                throw new EncodeException(
                    $"The signal dictionary is missing a value for signal '{field.Name}'.");
            }

            if (!layout.FitsIn(ByteLength))
            {
                throw new EncodeException(
                    $"Signal '{field.Name}' does not fit in {ByteLength} bytes of data.");
            }

            layout.WriteBits(destination, RawToBits(field, ToRawValue(field, value, scaling)));
        }
    }

    /// <summary>Decodes frame data into a dictionary of signal values.</summary>
    public Dictionary<string, SignalValue> Decode(
        ReadOnlySpan<byte> data,
        bool decodeChoices = true,
        bool scaling = true,
        bool allowTruncated = false,
        bool allowExcess = true)
    {
        var actualLength = data.Length;

        if (actualLength != ByteLength)
        {
            if (actualLength > ByteLength && allowExcess)
            {
                data = data[..ByteLength];
            }
            else if (actualLength < ByteLength && allowTruncated)
            {
                // Pad with 0xff so every field can be read; fields that extend into the
                // padding are dropped below, like upstream.
                Span<byte> padded = ByteLength <= 256 ? stackalloc byte[ByteLength] : new byte[ByteLength];
                padded.Fill(0xff);
                data.CopyTo(padded);
                return DecodePadded(padded, actualLength * 8, decodeChoices, scaling);
            }

            if (data.Length != ByteLength)
            {
                throw new DecodeException(
                    $"Wrong data size: {actualLength} instead of {ByteLength} bytes");
            }
        }

        return DecodePadded(data, actualLength * 8, decodeChoices, scaling);
    }

    private Dictionary<string, SignalValue> DecodePadded(
        ReadOnlySpan<byte> data,
        int availableBits,
        bool decodeChoices,
        bool scaling)
    {
        var decoded = new Dictionary<string, SignalValue>(_layouts.Length);

        foreach (var layout in _layouts)
        {
            if (layout.SequentialEndBit > availableBits)
            {
                if (availableBits < ByteLength * 8)
                {
                    // Truncated frame: this field lies (partly) outside the received bytes.
                    continue;
                }

                throw new DecodeException(
                    $"Signal '{layout.Field.Name}' does not fit in {ByteLength} bytes of data.");
            }

            var field = layout.Field;
            var raw = BitsToRaw(field, layout.ReadBits(data));

            if (scaling)
            {
                decoded[field.Name] = field.Conversion.RawToScaled(raw, decodeChoices);
            }
            else if (decodeChoices
                     && NamedSignalConversion.TryGetIntegral(raw, out var key)
                     && field.Conversion.Choices?.TryGetValue(key, out var choice) == true)
            {
                decoded[field.Name] = choice!;
            }
            else
            {
                decoded[field.Name] = raw;
            }
        }

        return decoded;
    }

    private static SignalValue ToRawValue(ICodecField field, SignalValue value, bool scaling)
    {
        var conversion = field.Conversion;

        if (value.IsNumeric)
        {
            if (scaling)
            {
                return conversion.NumericScaledToRaw(value);
            }

            return conversion.IsFloat || value.IsInteger
                ? value
                : (SignalValue)(long)Math.Round(value.ToDouble(), MidpointRounding.ToEven);
        }

        if (value.Named is { } named)
        {
            // Reject a named value whose label/value pair does not match the value table.
            if (conversion.RawToScaled(named.Value, decodeChoices: true) != value)
            {
                throw new EncodeException(
                    $"Invalid named value for signal '{field.Name}': "
                    + $"name {named.Name}, value {named.Value}.");
            }

            return named.Value;
        }

        return conversion.ChoiceToNumber(value.Label!);
    }

    private static ulong RawToBits(ICodecField field, SignalValue raw)
    {
        if (field.Conversion.IsFloat)
        {
            return field.Length switch
            {
                16 => BitConverter.HalfToUInt16Bits((Half)raw.ToDouble()),
                32 => BitConverter.SingleToUInt32Bits((float)raw.ToDouble()),
                64 => BitConverter.DoubleToUInt64Bits(raw.ToDouble()),
                _ => throw new EncodeException(
                    $"Signal '{field.Name}' is a float of {field.Length} bits; only 16, 32 and 64 are supported."),
            };
        }

        // Like bitstruct, integers that do not fit the field width are an error even
        // when no strict range checks run at the message level.
        if (field.IsSigned)
        {
            long value;
            try
            {
                value = raw.ToInt64();
            }
            catch (OverflowException)
            {
                throw new OverflowException($"Signed integer value {raw} out of range.");
            }

            var min = field.Length == 64 ? long.MinValue : -(1L << (field.Length - 1));
            var max = field.Length == 64 ? long.MaxValue : (1L << (field.Length - 1)) - 1;

            if (value < min || value > max)
            {
                throw new OverflowException($"Signed integer value {value} out of range.");
            }

            return (ulong)value & LengthMask(field.Length);
        }

        ulong unsignedValue;
        try
        {
            unsignedValue = raw.ToUInt64();
        }
        catch (OverflowException)
        {
            throw new OverflowException($"Unsigned integer value {raw} out of range.");
        }

        if (unsignedValue > LengthMask(field.Length))
        {
            throw new OverflowException($"Unsigned integer value {unsignedValue} out of range.");
        }

        return unsignedValue;
    }

    private static SignalValue BitsToRaw(ICodecField field, ulong bits)
    {
        if (field.Conversion.IsFloat)
        {
            return field.Length switch
            {
                16 => (double)BitConverter.UInt16BitsToHalf((ushort)bits),
                32 => BitConverter.UInt32BitsToSingle((uint)bits),
                64 => BitConverter.UInt64BitsToDouble(bits),
                _ => throw new DecodeException(
                    $"Signal '{field.Name}' is a float of {field.Length} bits; only 16, 32 and 64 are supported."),
            };
        }

        if (field.IsSigned)
        {
            var signBit = 1UL << (field.Length - 1);

            return (bits & signBit) == 0
                ? (long)bits
                : (long)(bits | ~LengthMask(field.Length));
        }

        return field.Length == 64 && bits > long.MaxValue ? bits : (long)bits;
    }

    private static ulong LengthMask(int length) =>
        length == 64 ? ulong.MaxValue : (1UL << length) - 1;

    /// <summary>
    /// Precomputed bit positions of one field. Bits are numbered in network order:
    /// bit 0 is the most significant bit of the first byte.
    /// </summary>
    private readonly struct FieldLayout
    {
        public FieldLayout(ICodecField field)
        {
            Field = field;

            if (field.ByteOrder == ByteOrder.BigEndian)
            {
                // Normalize the DBC sawtooth start bit to the network position of the MSB.
                MsbNetworkBit = 8 * (field.StartBit / 8) + (7 - field.StartBit % 8);
                SequentialEndBit = MsbNetworkBit + field.Length;
            }
            else
            {
                MsbNetworkBit = -1;
                SequentialEndBit = field.StartBit + field.Length;
            }
        }

        public ICodecField Field { get; }

        /// <summary>Network bit position of the MSB; -1 for little endian fields.</summary>
        public int MsbNetworkBit { get; }

        /// <summary>First bit index past the field, used for bounds and truncation checks.</summary>
        public int SequentialEndBit { get; }

        public bool FitsIn(int byteLength) => SequentialEndBit <= byteLength * 8;

        public ulong ReadBits(ReadOnlySpan<byte> data)
        {
            var field = Field;
            var value = 0UL;

            if (field.ByteOrder == ByteOrder.BigEndian)
            {
                for (var i = 0; i < field.Length; i++)
                {
                    value = (value << 1) | GetBit(data, MsbNetworkBit + i);
                }
            }
            else
            {
                for (var i = field.Length - 1; i >= 0; i--)
                {
                    value = (value << 1) | GetBit(data, ToNetwork(field.StartBit + i));
                }
            }

            return value;
        }

        public void WriteBits(Span<byte> data, ulong value)
        {
            var field = Field;

            if (field.ByteOrder == ByteOrder.BigEndian)
            {
                for (var i = 0; i < field.Length; i++)
                {
                    SetBit(data, MsbNetworkBit + i, (value >> (field.Length - 1 - i)) & 1);
                }
            }
            else
            {
                for (var i = 0; i < field.Length; i++)
                {
                    SetBit(data, ToNetwork(field.StartBit + i), (value >> i) & 1);
                }
            }
        }

        public void ClearFieldBits(Span<byte> mask)
        {
            var field = Field;

            if (field.ByteOrder == ByteOrder.BigEndian)
            {
                for (var i = 0; i < field.Length; i++)
                {
                    ClearBit(mask, MsbNetworkBit + i);
                }
            }
            else
            {
                for (var i = 0; i < field.Length; i++)
                {
                    ClearBit(mask, ToNetwork(field.StartBit + i));
                }
            }
        }

        // Sawtooth numbering counts bits from the LSB within each byte, network
        // numbering from the MSB.
        private static int ToNetwork(int sawtoothBit) =>
            8 * (sawtoothBit / 8) + (7 - sawtoothBit % 8);

        private static ulong GetBit(ReadOnlySpan<byte> data, int networkBit) =>
            (ulong)(data[networkBit >> 3] >> (7 - (networkBit & 7))) & 1;

        private static void SetBit(Span<byte> data, int networkBit, ulong bit) =>
            data[networkBit >> 3] |= (byte)(bit << (7 - (networkBit & 7)));

        private static void ClearBit(Span<byte> data, int networkBit) =>
            data[networkBit >> 3] &= (byte)~(1 << (7 - (networkBit & 7)));
    }
}
