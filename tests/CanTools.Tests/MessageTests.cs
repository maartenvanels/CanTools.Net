using CanTools.Model;

namespace CanTools.Tests;

// Ported from the Message-level parts of tests/test_database.py. Messages are built in
// code from the signal definitions of the referenced dbc files; expected bytes and
// error texts are copied verbatim from the upstream tests.
public class MessageTests
{
    private static Dictionary<string, SignalValue> Values(params (string Name, SignalValue Value)[] entries) =>
        entries.ToDictionary(e => e.Name, e => e.Value);

    private static IReadOnlyDictionary<long, NamedSignalValue> Choices(params (long Value, string Name)[] entries) =>
        entries.ToDictionary(e => e.Value, e => new NamedSignalValue(e.Value, e.Name));

    private static void AssertRoundTrip(
        Message message,
        Dictionary<string, SignalValue> decoded,
        byte[] encoded,
        bool decodeChoices = true)
    {
        Assert.Equal(encoded, message.Encode(decoded));

        var actual = message.Decode(encoded, decodeChoices);

        Assert.Equal(decoded.Count, actual.Count);
        foreach (var (name, value) in decoded)
        {
            Assert.True(actual[name] == value, $"{name}: expected {value}, got {actual[name]}");
        }
    }

    private static void AssertLeaf(SignalTreeNode node, string name)
    {
        Assert.Equal(name, node.Name);
        Assert.Null(node.Multiplexed);
    }

    private static IReadOnlyDictionary<long, IReadOnlyList<SignalTreeNode>> AssertMux(
        SignalTreeNode node, string name, params long[] multiplexerIds)
    {
        Assert.Equal(name, node.Name);
        Assert.NotNull(node.Multiplexed);
        Assert.Equal(multiplexerIds, node.Multiplexed.Keys.Order());

        return node.Multiplexed;
    }

    private static void AssertLeaves(IReadOnlyList<SignalTreeNode> nodes, params string[] names)
    {
        Assert.Equal(names, nodes.Select(node => node.Name));
        Assert.All(nodes, node => Assert.Null(node.Multiplexed));
    }

    // ported from test_database.py::test_multiplex_extended
    private static Message ExtendedMultiplexMessage() => new(
        frameId: 1,
        name: "M0",
        length: 8,
        signals:
        [
            new Signal("S0", 0, 4, isMultiplexer: true),
            new Signal("S1", 4, 4, isMultiplexer: true, multiplexerIds: [0], multiplexerSignal: "S0"),
            new Signal("S2", 8, 8, multiplexerIds: [0], multiplexerSignal: "S1"),
            new Signal("S3", 16, 16, multiplexerIds: [0], multiplexerSignal: "S1"),
            new Signal("S4", 8, 24, multiplexerIds: [2], multiplexerSignal: "S1"),
            new Signal("S5", 4, 28, multiplexerIds: [1], multiplexerSignal: "S0"),
            new Signal("S6", 32, 8, isMultiplexer: true),
            new Signal("S7", 40, 24, multiplexerIds: [1], multiplexerSignal: "S6"),
            new Signal("S8", 40, 8, multiplexerIds: [2], multiplexerSignal: "S6"),
        ]);

    // ported from test_database.py::test_multiplex_extended (signal tree)
    [Fact]
    public void Extended_multiplex_signal_tree_is_nested()
    {
        var message = ExtendedMultiplexMessage();
        var tree = message.SignalTree;

        Assert.True(message.IsMultiplexed);
        Assert.Equal(2, tree.Count);

        var s0 = AssertMux(tree[0], "S0", 0, 1);
        var s0Mux0 = Assert.Single(s0[0]);
        var s1 = AssertMux(s0Mux0, "S1", 0, 2);
        AssertLeaves(s1[0], "S2", "S3");
        AssertLeaves(s1[2], "S4");
        AssertLeaves(s0[1], "S5");

        var s6 = AssertMux(tree[1], "S6", 1, 2);
        AssertLeaves(s6[1], "S7");
        AssertLeaves(s6[2], "S8");
    }

    // ported from test_database.py::test_multiplex_extended (encode/decode)
    [Theory]
    [MemberData(nameof(ExtendedMultiplexCases))]
    public void Extended_multiplex_encodes_and_decodes(
        (string, SignalValue)[] decoded, byte[] encoded)
    {
        AssertRoundTrip(ExtendedMultiplexMessage(), Values(decoded), encoded);
    }

    public static TheoryData<(string, SignalValue)[], byte[]> ExtendedMultiplexCases() => new()
    {
        {
            [("S0", 0), ("S1", 2), ("S4", 10000), ("S6", 1), ("S7", 33)],
            [0x20, 0x10, 0x27, 0x00, 0x01, 0x21, 0x00, 0x00]
        },
        {
            [("S0", 0), ("S1", 0), ("S2", 100), ("S3", 5000), ("S6", 2), ("S8", 22)],
            [0x00, 0x64, 0x88, 0x13, 0x02, 0x16, 0x00, 0x00]
        },
        {
            [("S0", 1), ("S5", 3), ("S6", 1), ("S7", 772)],
            [0x31, 0x00, 0x00, 0x00, 0x01, 0x04, 0x03, 0x00]
        },
    };

    // ported from test_database.py::test_multiplex_extended (unknown signals)
    [Fact]
    public void Strict_encode_rejects_unknown_signals_but_non_strict_ignores_them()
    {
        var message = ExtendedMultiplexMessage();
        var values = Values(("S0", 1), ("S5", 3), ("S6", 1), ("S7", 772));
        var encoded = message.Encode(values);

        values["UndefinedMultiplexerSignal"] = "Boo!";

        Assert.Equal(encoded, message.Encode(values, strict: false));
        Assert.Throws<EncodeException>(() => message.Encode(values));
    }

    // The signal layout of tests/files/dbc/multiplex.dbc, built in code.
    private static List<Signal> MultiplexDbcSignals(
        IReadOnlyDictionary<long, NamedSignalValue>? multiplexorChoices = null,
        IReadOnlyDictionary<long, NamedSignalValue>? bitLChoices = null)
    {
        long[] all = [8, 16, 24];
        long[] only24 = [24];

        return
        [
            new Signal("Multiplexor", 2, 6, isMultiplexer: true,
                conversion: multiplexorChoices is null ? null : Conversion.Create(choices: multiplexorChoices)),
            new Signal("BIT_J", 18, 1, multiplexerIds: all, multiplexerSignal: "Multiplexor"),
            new Signal("BIT_C", 19, 1, multiplexerIds: all, multiplexerSignal: "Multiplexor"),
            new Signal("BIT_G", 23, 1, multiplexerIds: all, multiplexerSignal: "Multiplexor"),
            new Signal("BIT_L", 24, 1, multiplexerIds: all, multiplexerSignal: "Multiplexor",
                conversion: bitLChoices is null ? null : Conversion.Create(choices: bitLChoices)),
            new Signal("BIT_A", 26, 1, multiplexerIds: only24, multiplexerSignal: "Multiplexor"),
            new Signal("BIT_K", 28, 1, multiplexerIds: only24, multiplexerSignal: "Multiplexor"),
            new Signal("BIT_E", 29, 1, multiplexerIds: only24, multiplexerSignal: "Multiplexor"),
            new Signal("BIT_D", 32, 1, multiplexerIds: only24, multiplexerSignal: "Multiplexor"),
            new Signal("BIT_B", 33, 1, multiplexerIds: only24, multiplexerSignal: "Multiplexor"),
            new Signal("BIT_H", 38, 1, multiplexerIds: only24, multiplexerSignal: "Multiplexor"),
            new Signal("BIT_F", 39, 1, multiplexerIds: only24, multiplexerSignal: "Multiplexor"),
        ];
    }

    private static IReadOnlyDictionary<long, NamedSignalValue> MultiplexorChoices(bool withEmptyLayer = false) =>
        withEmptyLayer
            ? Choices((4, "MULTIPLEXOR_4_NO_SIGNALS"), (8, "MULTIPLEXOR_8"), (16, "MULTIPLEXOR_16"), (24, "MULTIPLEXOR_24"))
            : Choices((8, "MULTIPLEXOR_8"), (16, "MULTIPLEXOR_16"), (24, "MULTIPLEXOR_24"));

    // ported from test_database.py::test_multiplex
    [Fact]
    public void Multiplexed_message_encodes_and_decodes_each_layer()
    {
        var message = new Message(1, "Message1", 8, MultiplexDbcSignals());

        Assert.True(message.IsMultiplexed);

        var mux = AssertMux(message.SignalTree.Single(), "Multiplexor", 8, 16, 24);
        AssertLeaves(mux[8], "BIT_J", "BIT_C", "BIT_G", "BIT_L");
        AssertLeaves(mux[16], "BIT_J", "BIT_C", "BIT_G", "BIT_L");
        AssertLeaves(mux[24],
            "BIT_J", "BIT_C", "BIT_G", "BIT_L", "BIT_A", "BIT_K",
            "BIT_E", "BIT_D", "BIT_B", "BIT_H", "BIT_F");

        AssertRoundTrip(
            message,
            Values(("Multiplexor", 8), ("BIT_C", 1), ("BIT_G", 1), ("BIT_J", 1), ("BIT_L", 1)),
            [0x20, 0x00, 0x8c, 0x01, 0x00, 0x00, 0x00, 0x00]);
        AssertRoundTrip(
            message,
            Values(("Multiplexor", 16), ("BIT_C", 1), ("BIT_G", 1), ("BIT_J", 1), ("BIT_L", 1)),
            [0x40, 0x00, 0x8c, 0x01, 0x00, 0x00, 0x00, 0x00]);
        AssertRoundTrip(
            message,
            Values(
                ("Multiplexor", 24),
                ("BIT_A", 1), ("BIT_B", 1), ("BIT_C", 1), ("BIT_D", 1), ("BIT_E", 1),
                ("BIT_F", 1), ("BIT_G", 1), ("BIT_H", 1), ("BIT_J", 1), ("BIT_K", 1), ("BIT_L", 1)),
            [0x60, 0x00, 0x8c, 0x35, 0xc3, 0x00, 0x00, 0x00]);
    }

    // ported from test_database.py::test_multiplex_choices
    [Fact]
    public void Multiplexer_and_signal_choices_encode_and_decode_as_labels()
    {
        var message = new Message(1, "Message1", 8, MultiplexDbcSignals(
            MultiplexorChoices(),
            bitLChoices: Choices((0, "Off"), (1, "On"))));

        AssertRoundTrip(
            message,
            Values(("Multiplexor", "MULTIPLEXOR_8"), ("BIT_C", 1), ("BIT_G", 1), ("BIT_J", 1), ("BIT_L", "On")),
            [0x20, 0x00, 0x8c, 0x01, 0x00, 0x00, 0x00, 0x00]);

        AssertRoundTrip(
            message,
            Values(("Multiplexor", 8), ("BIT_C", 1), ("BIT_G", 1), ("BIT_J", 1), ("BIT_L", 1)),
            [0x20, 0x00, 0x8c, 0x01, 0x00, 0x00, 0x00, 0x00],
            decodeChoices: false);
    }

    // ported from test_database.py::test_multiplex_choices (message 2)
    [Fact]
    public void Multiplexer_value_from_the_value_table_may_have_no_signals()
    {
        var message = new Message(1, "Message2", 8, MultiplexDbcSignals(
            MultiplexorChoices(withEmptyLayer: true)));

        var mux = AssertMux(message.SignalTree.Single(), "Multiplexor", 4, 8, 16, 24);
        Assert.Empty(mux[4]);

        AssertRoundTrip(
            message,
            Values(("Multiplexor", 4)),
            [0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00],
            decodeChoices: false);
    }

    // ported from test_database.py::test_multiplex_bad_multiplexer
    [Fact]
    public void Unknown_multiplexer_value_gives_a_helpful_error()
    {
        var message = new Message(1, "Message1", 8, MultiplexDbcSignals(
            MultiplexorChoices(),
            bitLChoices: Choices((0, "Off"), (1, "On"))));

        var encodeError = Assert.Throws<EncodeException>(
            () => message.Encode(Values(("Multiplexor", 7))));
        Assert.Equal(
            "A valid value for the multiplexer selector signal \"Multiplexor\" is "
            + "required: Expected one of {8, 16 or 24}, but got 7",
            encodeError.Message);

        var decodeError = Assert.Throws<DecodeException>(
            () => message.Decode([0x1f, 0xff, 0x73, 0xfe, 0xff, 0xff, 0xff, 0xff]));
        Assert.Equal("expected multiplexer id 8, 16 or 24, but got 7", decodeError.Message);

        // Message3 of multiplex_choices.dbc: a single multiplexer id and no choices.
        var single = new Message(1, "Message3", 8,
        [
            new Signal("Multiplexor", 2, 6, isMultiplexer: true),
            new Signal("BIT_J", 18, 1, multiplexerIds: [8], multiplexerSignal: "Multiplexor"),
            new Signal("BIT_C", 19, 1, multiplexerIds: [8], multiplexerSignal: "Multiplexor"),
            new Signal("BIT_G", 23, 1, multiplexerIds: [8], multiplexerSignal: "Multiplexor"),
            new Signal("BIT_L", 24, 1, multiplexerIds: [8], multiplexerSignal: "Multiplexor"),
        ]);

        encodeError = Assert.Throws<EncodeException>(
            () => single.Encode(Values(("Multiplexor", 7))));
        Assert.Equal(
            "A valid value for the multiplexer selector signal \"Multiplexor\" is "
            + "required: Expected one of {8}, but got 7",
            encodeError.Message);

        decodeError = Assert.Throws<DecodeException>(
            () => single.Decode([0x1f, 0xff, 0x73, 0xfe, 0xff, 0xff, 0xff, 0xff]));
        Assert.Equal("expected multiplexer id 8, but got 7", decodeError.Message);
    }

    // ported from test_database.py::test_padding_one
    [Fact]
    public void Padding_fills_unused_bits_with_the_unused_bit_pattern()
    {
        var message = new Message(1, "M0", 8,
            [
                new Signal("S1", 3, 4, ByteOrder.BigEndian),
                new Signal("S2", 15, 4, ByteOrder.BigEndian),
                new Signal("S3", 11, 8, ByteOrder.BigEndian),
                new Signal("S4", 19, 1, ByteOrder.BigEndian),
                new Signal("S5", 17, 17, ByteOrder.BigEndian),
                new Signal("S6", 47, 15, ByteOrder.BigEndian),
            ],
            unusedBitPattern: 0xff);

        var decoded = Values(("S1", 0), ("S2", 2), ("S3", 55), ("S4", 1), ("S5", 2323), ("S6", 3224));
        var encoded = message.Encode(decoded, padding: true);

        Assert.Equal(new byte[] { 0xf0, 0x23, 0x7c, 0x12, 0x27, 0x19, 0x31, 0xff }, encoded);

        var roundTripped = message.Decode(encoded);
        Assert.Equal(decoded.Count, roundTripped.Count);
        foreach (var (name, value) in decoded)
        {
            Assert.True(roundTripped[name] == value, $"{name}: expected {value}, got {roundTripped[name]}");
        }
    }

    // ported from test_database.py::test_multiplex_choices_padding_one
    [Fact]
    public void Padding_only_fills_bits_unused_by_the_active_multiplexed_layer()
    {
        var message = new Message(1, "Message1", 8, MultiplexDbcSignals(
                MultiplexorChoices(),
                bitLChoices: Choices((0, "Off"), (1, "On"))),
            unusedBitPattern: 0xff);

        var decoded = Values(
            ("Multiplexor", "MULTIPLEXOR_8"),
            ("BIT_C", 0), ("BIT_G", 0), ("BIT_J", 0), ("BIT_L", "Off"));

        var encoded = message.Encode(decoded, padding: true);

        Assert.Equal(new byte[] { 0x23, 0xff, 0x73, 0xfe, 0xff, 0xff, 0xff, 0xff }, encoded);

        var roundTripped = message.Decode(encoded);
        Assert.True(roundTripped["Multiplexor"] == "MULTIPLEXOR_8");
        Assert.True(roundTripped["BIT_L"] == "Off");
    }

    // ported from test_database.py::test_strict_no_multiplexer (not overlapping)
    [Theory]
    [InlineData(7, 1, ByteOrder.BigEndian, 6, 1, ByteOrder.BigEndian)]
    [InlineData(7, 8, ByteOrder.BigEndian, 15, 8, ByteOrder.BigEndian)]
    [InlineData(7, 7, ByteOrder.BigEndian, 0, 2, ByteOrder.BigEndian)]
    [InlineData(2, 6, ByteOrder.LittleEndian, 0, 2, ByteOrder.LittleEndian)]
    [InlineData(2, 7, ByteOrder.LittleEndian, 1, 9, ByteOrder.BigEndian)]
    public void Strict_mode_accepts_non_overlapping_signals(
        int start0, int length0, ByteOrder order0,
        int start1, int length1, ByteOrder order1)
    {
        _ = new Message(1, "M", 7,
        [
            new Signal("S0", start0, length0, order0),
            new Signal("S1", start1, length1, order1),
        ]);
    }

    // ported from test_database.py::test_strict_no_multiplexer (overlapping)
    [Theory]
    [InlineData(7, 1, ByteOrder.BigEndian, 7, 1, ByteOrder.BigEndian)]
    [InlineData(7, 8, ByteOrder.BigEndian, 5, 10, ByteOrder.BigEndian)]
    [InlineData(2, 7, ByteOrder.LittleEndian, 1, 10, ByteOrder.BigEndian)]
    public void Strict_mode_rejects_overlapping_signals(
        int start0, int length0, ByteOrder order0,
        int start1, int length1, ByteOrder order1)
    {
        var error = Assert.Throws<CanToolsException>(() => new Message(1, "M", 7,
        [
            new Signal("S0", start0, length0, order0),
            new Signal("S1", start1, length1, order1),
        ]));

        Assert.Equal("The signals S1 and S0 are overlapping in message M.", error.Message);
    }

    // ported from test_database.py::test_strict_no_multiplexer (signal outside the message)
    [Theory]
    [InlineData(56, 2, ByteOrder.BigEndian)]
    [InlineData(63, 2, ByteOrder.LittleEndian)]
    [InlineData(64, 1, ByteOrder.BigEndian)]
    [InlineData(64, 1, ByteOrder.LittleEndian)]
    [InlineData(7, 65, ByteOrder.BigEndian)]
    [InlineData(0, 65, ByteOrder.LittleEndian)]
    public void Strict_mode_rejects_signals_outside_the_message(int start, int length, ByteOrder order)
    {
        var error = Assert.Throws<CanToolsException>(
            () => new Message(1, "M", 8, [new Signal("S", start, length, order)]));

        Assert.Equal("The signal S does not fit in message M.", error.Message);
    }

    private static Signal Mux(string name, int start, int length,
                              bool isMultiplexer = false, long[]? ids = null, string? selector = null) =>
        new(name, start, length, ByteOrder.BigEndian,
            isMultiplexer: isMultiplexer, multiplexerIds: ids, multiplexerSignal: selector);

    // ported from test_database.py::test_strict_multiplexer (not overlapping)
    [Fact]
    public void Strict_mode_allows_signals_of_different_multiplexed_layers_to_share_bits()
    {
        _ = new Message(1, "M", 7,
        [
            Mux("S0", 7, 2, isMultiplexer: true),
            Mux("S1", 5, 2, ids: [0], selector: "S0"),
            Mux("S2", 5, 1, ids: [1], selector: "S0"),
            Mux("S3", 3, 1),
            Mux("S4", 2, 2, isMultiplexer: true),
            Mux("S5", 0, 2, ids: [0], selector: "S4"),
            Mux("S6", 0, 2, isMultiplexer: true, ids: [1], selector: "S4"),
            Mux("S7", 14, 1, ids: [0], selector: "S6"),
        ]);
    }

    // ported from test_database.py::test_strict_multiplexer (overlapping)
    public static TheoryData<Signal[], string> OverlappingMultiplexedSignals() => new()
    {
        {
            [
                Mux("S0", 7, 2, isMultiplexer: true),
                Mux("S1", 5, 2, ids: [0], selector: "S0"),
                Mux("S2", 5, 1, ids: [1], selector: "S0"),
                Mux("S3", 4, 1),
            ],
            "The signals S3 and S1 are overlapping in message M."
        },
        {
            [
                Mux("S0", 7, 2, isMultiplexer: true),
                Mux("S1", 5, 2, ids: [0], selector: "S0"),
                Mux("S2", 5, 1, ids: [1], selector: "S0"),
                Mux("S3", 3, 1),
                Mux("S4", 2, 2, isMultiplexer: true),
                Mux("S5", 0, 2, ids: [0], selector: "S4"),
                Mux("S6", 0, 2, isMultiplexer: true, ids: [1], selector: "S4"),
                Mux("S7", 7, 1, ids: [0], selector: "S6"),
            ],
            "The signals S7 and S0 are overlapping in message M."
        },
        {
            [
                Mux("S0", 7, 2, isMultiplexer: true),
                Mux("S1", 5, 2, ids: [0], selector: "S0"),
                Mux("S2", 5, 1, ids: [1], selector: "S0"),
                Mux("S3", 3, 1),
                Mux("S4", 2, 2, isMultiplexer: true),
                Mux("S5", 0, 2, ids: [0], selector: "S4"),
                Mux("S6", 1, 2, isMultiplexer: true, ids: [1], selector: "S4"),
                Mux("S7", 14, 1, ids: [0], selector: "S6"),
            ],
            "The signals S6 and S4 are overlapping in message M."
        },
    };

    [Theory]
    [MemberData(nameof(OverlappingMultiplexedSignals))]
    public void Strict_mode_rejects_multiplexed_signals_overlapping_their_parents(
        Signal[] signals, string expectedError)
    {
        var error = Assert.Throws<CanToolsException>(() => new Message(1, "M", 7, signals));

        Assert.Equal(expectedError, error.Message);
    }

    // upstream: message.py raises for zero-length signals during refresh()
    [Fact]
    public void Zero_length_signal_is_rejected()
    {
        var error = Assert.Throws<CanToolsException>(
            () => new Message(1, "Status", 8, [new Signal("M", 0, 0)]));

        Assert.Equal("The signal M length 0 is not greater than 0 in message Status.", error.Message);
    }

    // upstream: message.py frame id validation in the constructor
    [Fact]
    public void Frame_id_must_fit_the_frame_type()
    {
        var standard = Assert.Throws<CanToolsException>(
            () => new Message(0x800, "M", 8, []));
        Assert.Equal("Standard frame id 0x800 is more than 11 bits in message M.", standard.Message);

        var extended = Assert.Throws<CanToolsException>(
            () => new Message(0x20000000, "M", 8, [], isExtendedFrame: true));
        Assert.Equal("Extended frame id 0x20000000 is more than 29 bits in message M.", extended.Message);

        _ = new Message(0x1fffffff, "M", 8, [], isExtendedFrame: true);
    }

    // ported from test_database.py::test_encode_signal_strict
    [Fact]
    public void Strict_encode_checks_signal_ranges()
    {
        var message = new Message(1, "Message1", 1,
            [new Signal("Signal1", 0, 2, minimum: 1, maximum: 2)]);

        message.Encode(Values(("Signal1", 1)));
        message.Encode(Values(("Signal1", 2)));

        var error = Assert.Throws<EncodeException>(() => message.Encode(Values(("Signal1", 0))));
        Assert.Equal(
            "Expected signal \"Signal1\" value greater than or equal to 1 in message "
            + "\"Message1\", but got 0.",
            error.Message);

        error = Assert.Throws<EncodeException>(() => message.Encode(Values(("Signal1", 3))));
        Assert.Equal(
            "Expected signal \"Signal1\" value smaller than or equal to 2 in message "
            + "\"Message1\", but got 3.",
            error.Message);

        // Out of range, but range checks disabled.
        message.Encode(Values(("Signal1", 0)), strict: false);
        message.Encode(Values(("Signal1", 3)), strict: false);

        // Missing value.
        error = Assert.Throws<EncodeException>(() => message.Encode(Values(("Foo", 1))));
        Assert.Equal("The signal \"Signal1\" is required for encoding.", error.Message);
    }

    // ported from test_database.py::test_encode_signal_strict_negative_scaling
    [Theory]
    [InlineData("Error", true, new byte[] { 0x0f, 0xff })]
    [InlineData("Error", false, new byte[] { 0x0f, 0xff })]
    [InlineData("Init", true, new byte[] { 0x0f, 0xfe })]
    [InlineData("Init", false, new byte[] { 0x0f, 0xfe })]
    [InlineData(4070.00, true, new byte[] { 0x0b, 0xb8 })]
    [InlineData(4069.99, true, null)] // scaled value < minimum
    [InlineData(4059.06, true, new byte[] { 0x0f, 0xfe })] // corresponds to enum value "Init"
    [InlineData(4059.05, true, new byte[] { 0x0f, 0xff })] // corresponds to enum value "Error"
    [InlineData(3000, false, new byte[] { 0x0b, 0xb8 })]
    [InlineData(3001, false, null)] // raw value < minimum
    [InlineData(4100, true, new byte[] { 0x00, 0x00 })]
    [InlineData(4100.01, true, null)] // scaled value > maximum
    [InlineData(4095, true, new byte[] { 0x01, 0xf4 })]
    [InlineData(4095, false, new byte[] { 0x0f, 0xff })]
    [InlineData(4094, true, new byte[] { 0x02, 0x58 })]
    [InlineData(4094, false, new byte[] { 0x0f, 0xfe })]
    [InlineData(0, false, new byte[] { 0x00, 0x00 })]
    [InlineData(-1, false, null)] // raw value outside unsigned 12 bit range
    [InlineData(4096, false, null)] // raw value outside unsigned 12 bit range
    public void Strict_encode_with_negative_scaling_and_value_table(
        object value, bool scaling, byte[]? expected)
    {
        var message = new Message(496, "ExampleMessage", 2,
        [
            new Signal("Temperature", 3, 12, ByteOrder.BigEndian,
                conversion: Conversion.Create(
                    scale: -0.01,
                    offset: 4100,
                    choices: Choices((4095, "Error"), (4094, "Init"))),
                minimum: 4070,
                maximum: 4100,
                unit: "degK"),
        ]);

        SignalValue signalValue = value switch
        {
            string label => label,
            int number => number,
            double number => number,
            _ => throw new ArgumentException(null, nameof(value)),
        };
        var data = Values(("Temperature", signalValue));

        if (expected is null)
        {
            Assert.Throws<EncodeException>(() => message.Encode(data, scaling));
        }
        else
        {
            Assert.Equal(expected, message.Encode(data, scaling));
        }
    }

    // verified against upstream: truncated multiplexed frames decode the available
    // signals of the selected layer and skip the rest
    [Fact]
    public void Truncated_multiplexed_frame_decodes_partially()
    {
        var message = new Message(1, "Message1", 8, MultiplexDbcSignals());

        var decoded = message.Decode([0x60, 0x00, 0x8c], allowTruncated: true);

        Assert.Equal(4, decoded.Count);
        Assert.True(decoded["Multiplexor"] == 24);
        Assert.True(decoded["BIT_J"] == 1);
        Assert.True(decoded["BIT_C"] == 1);
        Assert.True(decoded["BIT_G"] == 1);

        Assert.Empty(message.Decode([], allowTruncated: true));

        var error = Assert.Throws<DecodeException>(() => message.Decode([0x60, 0x00, 0x8c]));
        Assert.Equal("Wrong data size: 3 instead of 8 bytes", error.Message);
    }

    // upstream: Signal computes scaled initial/invalid values from the raw ones
    [Fact]
    public void Initial_and_invalid_values_are_scaled()
    {
        var signal = new Signal("S", 0, 8,
            rawInitial: 4,
            rawInvalid: 255,
            conversion: Conversion.Create(scale: 0.5, offset: 10));

        Assert.True(signal.Initial == 12.0);
        Assert.True(signal.Invalid == 137.5);
    }

    // upstream: Message.receivers is the union of all signal receivers
    [Fact]
    public void Receivers_are_collected_from_all_signals()
    {
        var message = new Message(1, "M", 8,
        [
            new Signal("A", 0, 8, receivers: ["N1", "N2"]),
            new Signal("B", 8, 8, receivers: ["N2", "N3"]),
        ]);

        Assert.Equal(["N1", "N2", "N3"], message.Receivers.Order());
    }
}
