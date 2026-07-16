using CanTools.Packing;

namespace CanTools.Tests;

// Ported from the encode/decode parts of tests/test_database.py. The fields are built
// in code from the signal definitions of the referenced dbc files; the expected bytes
// are copied verbatim from the upstream tests.
public class MessageCodecTests
{
    private sealed record Field(
        string Name,
        int StartBit,
        int Length,
        ByteOrder ByteOrder = ByteOrder.LittleEndian,
        bool IsSigned = false,
        Conversion? Scaling = null) : ICodecField
    {
        public Conversion Conversion { get; } = Scaling ?? Conversion.Create();
    }

    private static readonly Conversion Float32 = Conversion.Create(isFloat: true);
    private static readonly Conversion Float64 = Conversion.Create(isFloat: true);

    private static MessageCodec Codec(params ICodecField[] fields) =>
        MessageCodec.Create(fields, numberOfBytes: 8);

    private static Dictionary<string, SignalValue> Values(params (string Name, SignalValue Value)[] entries) =>
        entries.ToDictionary(e => e.Name, e => e.Value);

    private static void AssertRoundTrip(
        MessageCodec codec,
        Dictionary<string, SignalValue> decoded,
        byte[] encoded,
        bool decodeChoices = true,
        bool scaling = true)
    {
        Assert.Equal(encoded, codec.Encode(decoded, scaling));

        var actual = codec.Decode(encoded, decodeChoices, scaling);

        Assert.Equal(decoded.Count, actual.Count);
        foreach (var (name, value) in decoded)
        {
            Assert.True(actual[name] == value, $"{name}: expected {value}, got {actual[name]}");
        }
    }

    // ported from test_database.py::test_padding_bit_order (message 0)
    [Fact]
    public void Big_endian_signals_at_byte_boundaries()
    {
        var codec = Codec(
            new Field("A", 6, 15, ByteOrder.BigEndian),
            new Field("B", 7, 1, ByteOrder.BigEndian),
            new Field("C", 38, 15, ByteOrder.BigEndian),
            new Field("D", 39, 1, ByteOrder.BigEndian));

        AssertRoundTrip(
            codec,
            Values(("B", 1), ("A", 0x2c9), ("D", 0), ("C", 0x2c9)),
            [0x82, 0xc9, 0x00, 0x00, 0x02, 0xc9, 0x00, 0x00]);
    }

    // ported from test_database.py::test_padding_bit_order (message 1)
    [Fact]
    public void Little_endian_signals_at_byte_boundaries()
    {
        var codec = Codec(
            new Field("E", 0, 1),
            new Field("F", 1, 15),
            new Field("G", 32, 1),
            new Field("H", 33, 15));

        AssertRoundTrip(
            codec,
            Values(("E", 1), ("F", 0x2c9), ("G", 0), ("H", 0x2c9)),
            [0x93, 0x05, 0x00, 0x00, 0x92, 0x05, 0x00, 0x00]);
    }

    // ported from test_database.py::test_padding_bit_order (message 2)
    [Fact]
    public void Little_endian_nibbles()
    {
        var codec = Codec(
            new Field("I", 0, 4),
            new Field("J", 4, 4),
            new Field("K", 8, 4));

        AssertRoundTrip(
            codec,
            Values(("I", 1), ("J", 2), ("K", 3)),
            [0x21, 0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]);
    }

    // ported from test_database.py::test_padding_bit_order (messages 3 and 4)
    [Fact]
    public void Full_width_64_bit_signals()
    {
        var bigEndian = Codec(new Field("L", 7, 64, ByteOrder.BigEndian));
        AssertRoundTrip(
            bigEndian,
            Values(("L", 0x0123456789abcdefL)),
            [0x01, 0x23, 0x45, 0x67, 0x89, 0xab, 0xcd, 0xef]);

        var littleEndian = Codec(new Field("M", 0, 64));
        AssertRoundTrip(
            littleEndian,
            Values(("M", 0x0123456789abcdefL)),
            [0xef, 0xcd, 0xab, 0x89, 0x67, 0x45, 0x23, 0x01]);
    }

    // verified against upstream: 64-bit unsigned signals accept the full ulong range
    [Fact]
    public void Unsigned_64_bit_signal_holds_the_maximum_value()
    {
        var codec = Codec(new Field("L", 7, 64, ByteOrder.BigEndian));

        AssertRoundTrip(
            codec,
            Values(("L", ulong.MaxValue)),
            [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff]);
    }

    // verified against upstream: bitstruct raises OverflowError when a raw value does
    // not fit the field width, independent of message-level range checks
    [Fact]
    public void Raw_value_outside_the_field_width_throws()
    {
        var unsigned = Codec(new Field("I", 0, 4));
        Assert.Throws<OverflowException>(() => unsigned.Encode(Values(("I", 17))));
        Assert.Throws<OverflowException>(() => unsigned.Encode(Values(("I", -1))));

        var signed = Codec(new Field("s7", 1, 7, IsSigned: true));
        Assert.Throws<OverflowException>(() => signed.Encode(Values(("s7", 100))));
        Assert.Throws<OverflowException>(() => signed.Encode(Values(("s7", -65))));
    }

    private static MessageCodec MotohawkExampleMessage() => Codec(
        new Field("Temperature", 0, 12, ByteOrder.BigEndian, IsSigned: true,
            Scaling: Conversion.Create(scale: 0.01, offset: 250)),
        new Field("AverageRadius", 6, 6, ByteOrder.BigEndian,
            Scaling: Conversion.Create(scale: 0.1)),
        new Field("Enable", 7, 1, ByteOrder.BigEndian,
            Scaling: Conversion.Create(choices: new Dictionary<long, NamedSignalValue>
            {
                [0] = new(0, "Disabled"),
                [1] = new(1, "Enabled"),
            })));

    // ported from test_database.py::test_motohawk_encode_decode
    [Fact]
    public void Scaled_big_endian_signals_encode_and_decode()
    {
        var codec = MotohawkExampleMessage();

        AssertRoundTrip(
            codec,
            Values(("Temperature", 250.55), ("AverageRadius", 3.2), ("Enable", 1)),
            [0xc0, 0x06, 0xe0, 0x00, 0x00, 0x00, 0x00, 0x00],
            decodeChoices: false);

        // Temperature 229.53 has a negative raw value.
        AssertRoundTrip(
            codec,
            Values(("Temperature", 229.53), ("AverageRadius", 0.0), ("Enable", 0)),
            [0x01, 0x00, 0x20, 0x00, 0x00, 0x00, 0x00, 0x00],
            decodeChoices: false);
    }

    // ported from test_database.py::test_motohawk_encode_decode (enumerated values)
    [Fact]
    public void Choice_labels_encode_and_decode()
    {
        var codec = MotohawkExampleMessage();

        AssertRoundTrip(
            codec,
            Values(("Temperature", 250.55), ("AverageRadius", 3.2), ("Enable", "Enabled")),
            [0xc0, 0x06, 0xe0, 0x00, 0x00, 0x00, 0x00, 0x00]);
    }

    // ported from test_database.py::test_encode_decode_no_scaling
    [Fact]
    public void Scaling_can_be_disabled()
    {
        var codec = MotohawkExampleMessage();

        AssertRoundTrip(
            codec,
            Values(("Temperature", 55), ("AverageRadius", 32), ("Enable", "Enabled")),
            [0xc0, 0x06, 0xe0, 0x00, 0x00, 0x00, 0x00, 0x00],
            scaling: false);
    }

    // ported from test_database.py::test_signed_dbc
    [Theory]
    [MemberData(nameof(SignedMessages))]
    public void Signed_signals_encode_and_decode(
        ICodecField[] fields,
        (string Name, SignalValue Value)[] decoded,
        byte[] encoded)
    {
        AssertRoundTrip(MessageCodec.Create(fields, 8), Values(decoded), encoded);
    }

    public static TheoryData<ICodecField[], (string, SignalValue)[], byte[]> SignedMessages() => new()
    {
        {
            [new Field("s64", 0, 64, IsSigned: true)],
            [("s64", -5)],
            [0xfb, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff]
        },
        {
            [new Field("s33", 0, 33, IsSigned: true)],
            [("s33", -5)],
            [0xfb, 0xff, 0xff, 0xff, 0x01, 0x00, 0x00, 0x00]
        },
        {
            [new Field("s32", 0, 32, IsSigned: true)],
            [("s32", -5)],
            [0xfb, 0xff, 0xff, 0xff, 0x00, 0x00, 0x00, 0x00]
        },
        {
            [new Field("s64big", 7, 64, ByteOrder.BigEndian, IsSigned: true)],
            [("s64big", -5)],
            [0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xff, 0xfb]
        },
        {
            [new Field("s33big", 7, 33, ByteOrder.BigEndian, IsSigned: true)],
            [("s33big", -5)],
            [0xff, 0xff, 0xff, 0xfd, 0x80, 0x00, 0x00, 0x00]
        },
        {
            [new Field("s32big", 7, 32, ByteOrder.BigEndian, IsSigned: true)],
            [("s32big", -5)],
            [0xff, 0xff, 0xff, 0xfb, 0x00, 0x00, 0x00, 0x00]
        },
        {
            [
                new Field("s3big", 39, 3, ByteOrder.BigEndian, IsSigned: true),
                new Field("s3", 34, 3, IsSigned: true),
                new Field("s10big", 40, 10, ByteOrder.BigEndian, IsSigned: true),
                new Field("s8big", 0, 8, ByteOrder.BigEndian, IsSigned: true),
                new Field("s7big", 62, 7, ByteOrder.BigEndian, IsSigned: true),
                new Field("s9", 17, 9, IsSigned: true),
                new Field("s8", 26, 8, IsSigned: true),
                new Field("s7", 1, 7, IsSigned: true),
            ],
            [
                ("s7", -40), ("s8big", 0x5a), ("s9", 0xa5), ("s8", -43),
                ("s3big", -4), ("s3", 1), ("s10big", -253), ("s7big", -9),
            ],
            [0xb0, 0xb4, 0x4a, 0x55, 0x87, 0x01, 0x81, 0xf7]
        },
    };

    // ported from test_database.py::test_floating_point_dbc
    [Fact]
    public void Double_precision_signal_encodes_and_decodes()
    {
        var codec = Codec(new Field("Signal1", 0, 64, Scaling: Float64));

        AssertRoundTrip(
            codec,
            Values(("Signal1", -129.448)),
            [0x75, 0x93, 0x18, 0x04, 0x56, 0x2e, 0x60, 0xc0]);
    }

    // ported from test_database.py::test_floating_point_dbc
    [Fact]
    public void Single_precision_signals_encode_and_decode()
    {
        var codec = Codec(
            new Field("Signal2", 32, 32, Scaling: Float32),
            new Field("Signal1", 0, 32, Scaling: Float32));

        AssertRoundTrip(
            codec,
            Values(("Signal1", 129.5), ("Signal2", 1234500.5)),
            [0x00, 0x80, 0x01, 0x43, 0x24, 0xb2, 0x96, 0x49]);
    }

    // upstream: utils.decode_data raises DecodeError on a size mismatch
    [Fact]
    public void Wrong_data_size_throws()
    {
        var codec = Codec(new Field("I", 0, 4));

        var error = Assert.Throws<DecodeException>(() => codec.Decode(new byte[4]));
        Assert.Equal("Wrong data size: 4 instead of 8 bytes", error.Message);

        Assert.Throws<DecodeException>(() => codec.Decode(new byte[9], allowExcess: false));
    }

    // upstream: utils.decode_data with allow_excess trims extra bytes
    [Fact]
    public void Excess_data_is_ignored_when_allowed()
    {
        var codec = Codec(new Field("I", 0, 4));
        var data = new byte[9];
        data[0] = 0x07;

        var decoded = codec.Decode(data, allowExcess: true);

        Assert.True(decoded["I"] == 7);
    }

    // upstream: utils.decode_data with allow_truncated drops signals outside the data
    [Fact]
    public void Truncated_data_drops_unavailable_signals()
    {
        var codec = Codec(
            new Field("I", 0, 4),
            new Field("J", 4, 4),
            new Field("K", 8, 4));

        var decoded = codec.Decode([0x21], allowTruncated: true);

        Assert.Equal(2, decoded.Count);
        Assert.True(decoded["I"] == 1);
        Assert.True(decoded["J"] == 2);
    }

    // upstream: encode_data requires a value for every signal of the codec
    [Fact]
    public void Missing_signal_value_throws()
    {
        var codec = Codec(new Field("I", 0, 4), new Field("J", 4, 4));

        Assert.Throws<EncodeException>(() => codec.Encode(Values(("I", 1))));
    }

    // upstream: _encode_signal_values validates NamedSignalValue name/value pairs
    [Fact]
    public void Named_value_with_wrong_label_throws()
    {
        var codec = MotohawkExampleMessage();
        var values = Values(
            ("Temperature", 250.55),
            ("AverageRadius", 3.2),
            ("Enable", new NamedSignalValue(1, "Enabled")));

        Assert.Equal([0xc0, 0x06, 0xe0, 0x00, 0x00, 0x00, 0x00, 0x00], codec.Encode(values));

        values["Enable"] = new NamedSignalValue(1, "Wrong");
        Assert.Throws<EncodeException>(() => codec.Encode(values));
    }

    // upstream: formats.padding_mask marks the bits no signal covers
    [Fact]
    public void Padding_mask_covers_unused_bits()
    {
        var codec = MotohawkExampleMessage();

        Assert.Equal(
            new byte[] { 0x00, 0x00, 0x1f, 0xff, 0xff, 0xff, 0xff, 0xff },
            codec.PaddingMask.ToArray());
    }

    // upstream: encode_data returns 0 for a codec without signals
    [Fact]
    public void Codec_without_signals_encodes_zeroes()
    {
        var codec = MessageCodec.Create([], 8);

        Assert.Equal(new byte[8], codec.Encode(Values()));
        Assert.Empty(codec.Decode(new byte[8]));
    }
}
