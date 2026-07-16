using CanTools.Formats.Sym;
using CanTools.Model;

namespace CanTools.Tests;

// Ported from the SYM-reader parts of tests/test_database.py.
public class SymReaderTests
{
    private static Database Load(string name, bool strict = true) =>
        SymReader.LoadFile(TestFiles.Sym(name), strict: strict);

    private static Dictionary<string, SignalValue> Values(params (string Name, SignalValue Value)[] entries) =>
        entries.ToDictionary(e => e.Name, e => e.Value);

    // ported from test_database.py::test_jopp_5_0_sym
    [Fact]
    public void Only_version_6_is_supported()
    {
        var error = Assert.Throws<ParseException>(() => Load("jopp-5.0.sym"));
        Assert.Equal("Only SYM version 6.0 is supported.", error.Message);
    }

    // ported from test_database.py::test_jopp_6_0_sym
    [Fact]
    public void Jopp_database_loads_completely()
    {
        var db = Load("jopp-6.0.sym");

        Assert.Equal("6.0", db.Version);
        Assert.Equal(7, db.Messages.Count);
        Assert.Empty(db.Messages[0].Signals);

        var message1 = db.GetMessageByName("Message1");
        Assert.Equal(0u, message1.FrameId);
        Assert.False(message1.IsExtendedFrame);
        Assert.False(message1.IsFd);
        Assert.Equal(8, message1.Length);
        Assert.Equal(["ECU", "Peripherals"], message1.Senders);
        Assert.Null(message1.SendType);
        Assert.Equal(30, message1.CycleTime);
        Assert.Equal(2, message1.Signals.Count);
        Assert.Equal("apa", message1.Comment);
        Assert.Null(message1.BusName);

        var signal1 = message1.Signals[0];
        Assert.Equal("Signal1", signal1.Name);
        Assert.Equal(0, signal1.StartBit);
        Assert.Equal(11, signal1.Length);
        Assert.Empty(signal1.Receivers);
        Assert.Equal(ByteOrder.LittleEndian, signal1.ByteOrder);
        Assert.False(signal1.IsSigned);
        Assert.False(signal1.IsFloat);
        Assert.Equal(1, signal1.Scale);
        Assert.Equal(0, signal1.Offset);
        Assert.Null(signal1.Minimum);
        Assert.Equal(255, signal1.Maximum);
        Assert.Equal("A", signal1.Unit);
        Assert.Null(signal1.Choices);
        Assert.Null(signal1.Comment);
        Assert.False(signal1.IsMultiplexer);
        Assert.Null(signal1.MultiplexerIds);
        Assert.Equal(1234, signal1.Spn);

        var signal2 = message1.Signals[1];
        Assert.Equal("Signal2", signal2.Name);
        Assert.Equal(32, signal2.StartBit);
        Assert.Equal(32, signal2.Length);
        Assert.True(signal2.IsFloat);
        Assert.Equal(1, signal2.Scale);
        Assert.Equal(48, signal2.Offset);
        Assert.Equal(16, signal2.Minimum);
        Assert.Equal(130, signal2.Maximum);
        Assert.Equal("V", signal2.Unit);
        Assert.Equal("bbb", signal2.Comment);
        Assert.Null(signal2.Spn);

        var message2 = db.Messages[1];
        Assert.Equal(0x22u, message2.FrameId);
        Assert.True(message2.IsExtendedFrame);
        Assert.Equal("Message2", message2.Name);
        Assert.Equal(["Peripherals"], message2.Senders);
        Assert.Null(message2.CycleTime);
        Assert.Equal(0x23u, db.Messages[2].FrameId);

        var signal3 = message2.Signals[0];
        Assert.Equal("Signal3", signal3.Name);
        Assert.Equal(5, signal3.StartBit);
        Assert.Equal(11, signal3.Length);
        Assert.Equal(ByteOrder.BigEndian, signal3.ByteOrder);
        Assert.True(signal3.IsSigned);
        Assert.Equal(0, signal3.Minimum);
        Assert.Equal(1, signal3.Maximum);
        Assert.True(signal3.Choices![0] == "foo");
        Assert.True(signal3.Choices![1] == "bar");

        var signal4 = db.GetMessageByName("Symbol2").Signals[0];
        Assert.Equal("Signal4", signal4.Name);
        Assert.Equal(0, signal4.StartBit);
        Assert.Equal(64, signal4.Length);
        Assert.True(signal4.IsFloat);
        Assert.Equal(6, signal4.Scale);
        Assert.Equal(5, signal4.Offset);
        Assert.Equal(-1.7e308, signal4.Minimum);
        Assert.Equal(1.7e308, signal4.Maximum);
        Assert.Equal("*UU", signal4.Unit);

        var symbol3 = db.GetMessageByName("Symbol3");
        Assert.Equal(0x33u, symbol3.FrameId);
        Assert.True(symbol3.IsMultiplexed);
        Assert.Equal(4, symbol3.Signals.Count);

        var multiplexer = symbol3.Signals[0];
        Assert.Equal("Multiplexer1", multiplexer.Name);
        Assert.Equal(0, multiplexer.StartBit);
        Assert.Equal(3, multiplexer.Length);
        Assert.True(multiplexer.IsMultiplexer);
        Assert.Null(multiplexer.MultiplexerIds);

        Assert.Equal("Signal1", symbol3.Signals[1].Name);
        Assert.Equal(3, symbol3.Signals[1].StartBit);
        Assert.Equal([0L], symbol3.Signals[1].MultiplexerIds!);
        Assert.Equal("Signal2", symbol3.Signals[2].Name);
        Assert.Equal(6, symbol3.Signals[2].StartBit);
        Assert.Equal([1L], symbol3.Signals[2].MultiplexerIds!);
        Assert.Equal("Signal3", symbol3.Signals[3].Name);
        Assert.Equal(14, symbol3.Signals[3].StartBit);
        Assert.Equal([2L], symbol3.Signals[3].MultiplexerIds!);

        var message3 = db.GetMessageByName("Message3");
        Assert.Equal(0xAu, message3.FrameId);
        Assert.Equal(7, message3.Signals[0].StartBit);

        // Encode/decode smoke tests through the loaded database.
        Assert.Equal(new byte[8], db.EncodeMessage(0x009u, Values()));
        var encoded = db.EncodeMessage(0x022u, Values(("Signal3", "bar")), forceExtendedId: true);
        Assert.Equal(new byte[] { 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, encoded);
        Assert.True(db.DecodeMessage(0x022u, encoded, forceExtendedId: true)["Signal3"] == "bar");
    }

    // ported from test_database.py::test_empty_6_0_sym and friends
    [Theory]
    [InlineData("empty-6.0.sym", 0, null)]
    [InlineData("send-6.0.sym", 1, "ECU")]
    [InlineData("receive-6.0.sym", 1, "Peripherals")]
    [InlineData("sendreceive-6.0.sym", 1, null)]
    public void Section_membership_determines_senders(string file, int messageCount, string? sender)
    {
        var db = Load(file);

        Assert.Equal("6.0", db.Version);
        Assert.Equal(messageCount, db.Messages.Count);

        if (messageCount > 0)
        {
            Assert.Equal("Symbol1", db.Messages[0].Name);

            if (sender is not null)
            {
                Assert.Equal([sender], db.Messages[0].Senders);
            }
            else
            {
                Assert.Equal(["ECU", "Peripherals"], db.Messages[0].Senders);
            }
        }
    }

    // ported from test_database.py::test_signal_types_6_0_sym
    [Fact]
    public void All_signal_types_map_to_the_model()
    {
        var db = Load("signal-types-6.0.sym");
        var signals = db.Messages.Single().Signals;

        var expected = new (string Name, int Start, int Length, bool Signed, bool Float,
            double? Min, double? Max, bool EmptyChoices)[]
        {
            ("Bit", 0, 1, false, false, 0, 1, false),
            ("Char", 1, 8, false, false, null, null, false),
            ("Enum", 9, 4, false, false, null, null, true),
            ("Signed", 13, 3, true, false, null, null, false),
            ("String", 16, 16, false, false, null, null, false),
            ("Raw", 32, 16, false, false, null, null, false),
            ("Unsigned", 48, 2, false, false, null, null, false),
            ("Enum2", 50, 3, false, false, null, null, false),
        };

        Assert.Equal(expected.Length, signals.Count);

        for (var i = 0; i < expected.Length; i++)
        {
            var (name, start, length, signed, isFloat, min, max, emptyChoices) = expected[i];
            Assert.Equal(name, signals[i].Name);
            Assert.Equal(start, signals[i].StartBit);
            Assert.Equal(length, signals[i].Length);
            Assert.Equal(signed, signals[i].IsSigned);
            Assert.Equal(isFloat, signals[i].IsFloat);
            Assert.Equal(min, signals[i].Minimum);
            Assert.Equal(max, signals[i].Maximum);

            if (emptyChoices)
            {
                Assert.NotNull(signals[i].Choices);
                Assert.Empty(signals[i].Choices!);
            }
            else if (name != "Enum")
            {
                Assert.Null(signals[i].Choices);
            }
        }
    }

    // ported from test_database.py::test_variables_color_enum_6_0_sym
    [Fact]
    public void Variables_and_enum_references_load()
    {
        var db = Load("variables-color-enum-6.0.sym");
        var signals = db.Messages.Single().Signals;

        var enumSignal = signals[0];
        Assert.Equal("Enum", enumSignal.Name);
        Assert.Equal(5, enumSignal.StartBit);
        Assert.Equal(8, enumSignal.Length);
        Assert.Equal(ByteOrder.BigEndian, enumSignal.ByteOrder);
        Assert.True(enumSignal.Choices![0] == "Foo");

        var variable2 = signals[1];
        Assert.Equal("Variable2", variable2.Name);
        Assert.Equal(11, variable2.StartBit);
        Assert.Equal(10, variable2.Length);
        Assert.Equal(ByteOrder.BigEndian, variable2.ByteOrder);
        Assert.True(variable2.IsSigned);
        Assert.True(variable2.Choices![0] == "Foo");

        var variable1 = signals[2];
        Assert.Equal("Variable1", variable1.Name);
        Assert.Equal(12, variable1.StartBit);
        Assert.Equal(1, variable1.Length);
        Assert.Equal(ByteOrder.LittleEndian, variable1.ByteOrder);
        Assert.NotNull(variable1.Choices);
        Assert.Empty(variable1.Choices!);

        var color = signals[3];
        Assert.Equal("Color", color.Name);
        Assert.Equal(17, color.StartBit);
        Assert.Equal(32, color.Length);
        Assert.Equal(ByteOrder.BigEndian, color.ByteOrder);
        Assert.True(color.IsFloat);
        Assert.Equal(2, color.Offset);
        Assert.Equal("A", color.Unit);
    }

    // ported from test_database.py::test_empty_enum_6_0_sym
    [Fact]
    public void Empty_enums_become_empty_choices()
    {
        var db = Load("empty-enum-6.0.sym");
        var signal = db.Messages.Single().Signals[0];

        Assert.Equal("Signal1", signal.Name);
        Assert.NotNull(signal.Choices);
        Assert.Empty(signal.Choices!);
    }

    // ported from test_database.py::test_special_chars_6_0_sym
    [Fact]
    public void Special_characters_in_names_units_and_comments()
    {
        var db = Load("special-chars-6.0.sym");
        var message = db.Messages.Single();

        Assert.Equal(1u, message.FrameId);
        Assert.False(message.IsExtendedFrame);
        Assert.Equal("A/=*", message.Name);
        Assert.Equal(8, message.Length);
        Assert.Equal(["ECU", "Peripherals"], message.Senders);
        Assert.Equal(5, message.CycleTime);
        Assert.Equal(6, message.Signals.Count);
        Assert.Equal("dd", message.Comment);

        var signals = message.Signals;
        Assert.Equal("A B", signals[0].Name);
        Assert.Equal(0, signals[0].StartBit);
        Assert.Equal("A B", signals[0].Unit);
        Assert.Equal("A B", signals[0].Comment);

        Assert.Equal("S/", signals[1].Name);
        Assert.Equal(15, signals[1].StartBit);
        Assert.Equal(ByteOrder.BigEndian, signals[1].ByteOrder);
        Assert.Equal("/", signals[1].Unit);
        Assert.Equal("/", signals[1].Comment);

        Assert.Equal("S=", signals[2].Name);
        Assert.Equal(23, signals[2].StartBit);
        Assert.Equal(-55, signals[2].Offset);
        Assert.Equal("=", signals[2].Unit);
        Assert.Equal("=", signals[2].Comment);

        Assert.Equal("S{SEND}", signals[3].Name);
        Assert.Equal(24, signals[3].StartBit);
        Assert.Equal("{SEND}", signals[3].Unit);
        Assert.Equal("]", signals[3].Comment);

        Assert.Equal("a/b", signals[4].Name);
        Assert.Equal(32, signals[4].StartBit);
        Assert.Equal("][", signals[4].Unit);
        Assert.Equal("ö", signals[4].Comment);

        Assert.Equal("Variable1", signals[5].Name);
        Assert.Equal(40, signals[5].StartBit);
        Assert.Equal(0, signals[5].Minimum);
        Assert.Equal(1, signals[5].Maximum);
        Assert.Equal("m/s", signals[5].Unit);
        Assert.Equal("comment", signals[5].Comment);
    }

    // ported from test_database.py::test_add_bad_sym_string
    [Fact]
    public void Grammar_errors_point_at_the_offending_token()
    {
        var error = Assert.Throws<ParseException>(
            () => SymReader.LoadString("FormatVersion=6.0\nFoo=\"Jopp\""));
        Assert.Equal("Invalid syntax at line 2, column 1: \">>!<<Foo=\"Jopp\"\"", error.Message);
    }

    // ported from test_database.py::test_multiplexed_variables_sym
    [Fact]
    public void Continuation_sections_extend_the_multiplexed_message()
    {
        var db = Load("multiplexed_variables.sym");
        var message = db.GetMessageByName("TestAlert");

        var mux = Assert.Single(message.SignalTree.Where(node => node.Multiplexed is not null));
        Assert.Equal("FSM", mux.Name);
        Assert.Equal(
            ["alert1", "alert2", "alert3"],
            mux.Multiplexed![1].Select(node => node.Name));
        Assert.Equal(
            ["alert4", "alert5", "alert6"],
            mux.Multiplexed![2].Select(node => node.Name));
    }

    // ported from test_database.py::test_type_parameter_overrides_is_extended_sym
    [Fact]
    public void Type_parameter_forces_extended_frames()
    {
        var db = Load("type-extended-cycle-dash-p.sym");
        var message = db.GetMessageByName("CAN-Tx Query");

        Assert.True(message.IsExtendedFrame);
        Assert.False(message.IsFd);

        var encoded = db.EncodeMessage(message.FrameId, Values(
            ("Switch Outputs", 984), ("Switch Ch 02", 1), ("Switch Ch 01", 1)),
            forceExtendedId: true);
        Assert.Equal(0x03, encoded[0]);
        Assert.Equal(0xd8, encoded[1]);
    }

    // ported from test_database.py::test_comments_hex_and_motorola_sym
    [Fact]
    public void Hex_mux_ids_and_motorola_signals()
    {
        var db = Load("comments_hex_and_motorola.sym");

        var message1 = db.GetMessageByName("Msg1");
        Assert.Equal(0x620u, message1.FrameId);
        Assert.Equal(2, message1.Length);
        Assert.Equal(3, message1.Signals.Count);
        Assert.Null(message1.Comment);

        var selector = message1.Signals[0];
        Assert.Equal("sig1", selector.Name);
        Assert.Equal(7, selector.StartBit);
        Assert.Equal(ByteOrder.BigEndian, selector.ByteOrder);
        Assert.True(selector.IsMultiplexer);
        Assert.Equal("a comment", selector.Comment);

        var layer1 = message1.Signals[1];
        Assert.Equal("sig12", layer1.Name);
        Assert.Equal(15, layer1.StartBit);
        Assert.Equal([1L], layer1.MultiplexerIds!);
        Assert.Equal("sig1", layer1.MultiplexerSignal);
        Assert.Equal("another comment for sig1=1", layer1.Comment);

        var layer2 = message1.Signals[2];
        Assert.Equal("sig22", layer2.Name);
        Assert.Equal([2L], layer2.MultiplexerIds!);
        Assert.Equal("sig1", layer2.MultiplexerSignal);
        Assert.Equal("another comment for sig1=2", layer2.Comment);

        var message2 = db.GetMessageByName("Msg2");
        Assert.Equal(0x555u, message2.FrameId);
        Assert.Equal("test", message2.Comment);
        Assert.Equal(8, message2.Signals.Count);

        for (var i = 0; i < 8; i++)
        {
            Assert.Equal($"Test{i}", message2.Signals[i].Name);
            Assert.Equal(i * 8 + 7, message2.Signals[i].StartBit);
            Assert.Equal(ByteOrder.BigEndian, message2.Signals[i].ByteOrder);
        }

        var test7 = message2.Signals[7].Choices!;
        Assert.Equal([1L, 2L, 3L, 4L], test7.Keys);
        Assert.True(test7[1] == "A");
        Assert.True(test7[4] == "D");
    }

    // ported from test_database.py::test_big_endian (sym)
    [Fact]
    public void Big_endian_start_bits_convert_from_sawtooth()
    {
        var db = Load("big-endian.sym");
        var signals = db.GetMessageByName("Msg1").Signals;

        var expected = new (string Name, ByteOrder Order, int StartBit, int Length, double? Max)[]
        {
            ("m1h", ByteOrder.BigEndian, 7, 1, null),
            ("m1d", ByteOrder.BigEndian, 6, 1, null),
            ("m12d", ByteOrder.BigEndian, 5, 12, 1),
            ("m7d", ByteOrder.BigEndian, 9, 7, 1),
            ("m42d", ByteOrder.BigEndian, 18, 42, 1),
            ("i1h", ByteOrder.LittleEndian, 56, 1, null),
        };

        Assert.Equal(expected.Length, signals.Count);

        for (var i = 0; i < expected.Length; i++)
        {
            Assert.Equal(expected[i].Name, signals[i].Name);
            Assert.Equal(expected[i].Order, signals[i].ByteOrder);
            Assert.Equal(expected[i].StartBit, signals[i].StartBit);
            Assert.Equal(expected[i].Length, signals[i].Length);
            Assert.Equal(expected[i].Max, signals[i].Maximum);
        }
    }

    // ported from test_database.py::test_strict_load (sym part) and test_issue_138
    [Fact]
    public void Strict_load_rejects_invalid_layouts()
    {
        var error = Assert.Throws<CanToolsException>(() => Load("bad_message_length.sym"));
        Assert.Equal("The signal Signal1 does not fit in message Message1.", error.Message);

        _ = Load("bad_message_length.sym", strict: false);

        error = Assert.Throws<CanToolsException>(() => Load("issue_138.sym"));
        Assert.Equal("The signal M length 0 is not greater than 0 in message Status.", error.Message);
    }
}
