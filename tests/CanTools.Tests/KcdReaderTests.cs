using CanTools.Formats;
using CanTools.Formats.Kcd;
using CanTools.Model;

namespace CanTools.Tests;

// Ported from the KCD-reader parts of tests/test_database.py.
public class KcdReaderTests
{
    private static Database TheHomer() => KcdReader.LoadFile(TestFiles.Kcd("the_homer.kcd"));

    private static Dictionary<string, SignalValue> Values(params (string Name, SignalValue Value)[] entries) =>
        entries.ToDictionary(e => e.Name, e => e.Value);

    // ported from test_database.py::test_the_homer
    [Fact]
    public void The_homer_loads_completely()
    {
        var db = TheHomer();

        Assert.Equal("1.23", db.Version);
        Assert.Equal(18, db.Nodes.Count);
        Assert.Equal("Motor ACME", db.Nodes[0].Name);
        Assert.Equal("Motor alternative supplier", db.Nodes[1].Name);

        Assert.Equal(3, db.Buses.Count);
        Assert.Equal("Motor", db.Buses[0].Name);
        Assert.Equal("Instrumentation", db.Buses[1].Name);
        Assert.Equal("Comfort", db.Buses[2].Name);
        Assert.Null(db.Buses[0].Comment);
        Assert.Equal(500000, db.Buses[0].Baudrate);
        Assert.Equal(125000, db.Buses[1].Baudrate);

        Assert.Equal(33, db.Messages.Count);

        var airbag = db.Messages[0];
        Assert.Equal(0xAu, airbag.FrameId);
        Assert.False(airbag.IsExtendedFrame);
        Assert.False(airbag.IsFd);
        Assert.Equal("Airbag", airbag.Name);
        Assert.Equal(3, airbag.Length);
        Assert.Equal(["Brake ACME"], airbag.Senders);
        Assert.Equal(8, airbag.Signals.Count);
        Assert.Null(airbag.Comment);
        Assert.Null(airbag.SendType);
        Assert.Null(airbag.CycleTime);
        Assert.Equal("Motor", airbag.BusName);

        var seatConfiguration = airbag.Signals[^1];
        Assert.Equal("SeatConfiguration", seatConfiguration.Name);
        Assert.Equal(16, seatConfiguration.StartBit);
        Assert.Equal(8, seatConfiguration.Length);
        Assert.Empty(seatConfiguration.Receivers);
        Assert.Equal(ByteOrder.LittleEndian, seatConfiguration.ByteOrder);
        Assert.False(seatConfiguration.IsSigned);
        Assert.False(seatConfiguration.IsFloat);
        Assert.Equal(1, seatConfiguration.Scale);
        Assert.Equal(0, seatConfiguration.Offset);
        Assert.Null(seatConfiguration.Minimum);
        Assert.Null(seatConfiguration.Maximum);
        Assert.Null(seatConfiguration.Unit);
        Assert.Null(seatConfiguration.Choices);
        Assert.Null(seatConfiguration.Comment);

        var abs = db.Messages[1];
        Assert.Equal(0xB2u, abs.FrameId);
        Assert.Equal("ABS", abs.Name);
        Assert.Equal(100, abs.CycleTime);
        Assert.Equal(["Brake ACME", "Brake alternative supplier"], abs.Senders);

        var emission = db.Messages[3];
        Assert.Equal(0x400u, emission.FrameId);
        Assert.Equal(5, emission.Length);
        Assert.Empty(emission.Senders);

        Assert.Equal("Comfort", db.Messages[^1].BusName);

        var tankTemperature = db.Messages[10].Signals[1];
        Assert.Equal("TankTemperature", tankTemperature.Name);
        Assert.Equal(16, tankTemperature.StartBit);
        Assert.Equal(16, tankTemperature.Length);
        Assert.True(tankTemperature.IsSigned);
        Assert.Equal("Cel", tankTemperature.Unit);
    }

    // ported from test_database.py::test_the_homer (ABS multiplexing)
    [Fact]
    public void The_homer_multiplexed_message()
    {
        var abs = TheHomer().GetMessageByName("ABS");

        var info0 = abs.Signals[0];
        Assert.Equal("Info0", info0.Name);
        Assert.Equal(0, info0.StartBit);
        Assert.Equal(8, info0.Length);
        Assert.False(info0.IsMultiplexer);
        Assert.Equal([0L], info0.MultiplexerIds!);
        Assert.Equal("ABS_InfoMux", info0.MultiplexerSignal);

        Assert.Equal(
            ["Info0", "Info2", "Info4", "Info6", "Info1", "Info3", "Info5", "Info7"],
            abs.Signals.Take(8).Select(s => s.Name));

        var selector = abs.Signals[8];
        Assert.Equal("ABS_InfoMux", selector.Name);
        Assert.Equal(16, selector.StartBit);
        Assert.Equal(2, selector.Length);
        Assert.True(selector.IsMultiplexer);
        Assert.Null(selector.MultiplexerIds);
        Assert.Null(selector.MultiplexerSignal);

        var outsideTemp = abs.Signals[9];
        Assert.Equal("OutsideTemp", outsideTemp.Name);
        Assert.Equal(18, outsideTemp.StartBit);
        Assert.Equal(12, outsideTemp.Length);
        Assert.Equal(["BodyComputer"], outsideTemp.Receivers);
        Assert.Equal(0.05, outsideTemp.Scale);
        Assert.Equal(-40, outsideTemp.Offset);
        Assert.Equal(0, outsideTemp.Minimum);
        Assert.Equal(100, outsideTemp.Maximum);
        Assert.Equal("Cel", outsideTemp.Unit);
        Assert.True(outsideTemp.Choices![0] == "init");
        Assert.Equal("Outside temperature.", outsideTemp.Comment);

        var speed = abs.Signals[10];
        Assert.Equal("SpeedKm", speed.Name);
        Assert.Equal(0.2, speed.Scale);
        Assert.True(speed.Choices![16777215] == "invalid");
        Assert.Equal("Middle speed of front wheels in kilometers per hour.", speed.Comment);

        var mux = Assert.Single(abs.SignalTree.Where(node => node.Multiplexed is not null));
        Assert.Equal("ABS_InfoMux", mux.Name);
        Assert.Equal([0L, 1L, 2L, 3L], mux.Multiplexed!.Keys.Order());
        Assert.Equal(["Info0", "Info1"], mux.Multiplexed[0].Select(n => n.Name));
        Assert.Equal(["Info6", "Info7"], mux.Multiplexed[3].Select(n => n.Name));
    }

    // ported from test_database.py::test_the_homer (floats, labels, big endian, auto length)
    [Fact]
    public void The_homer_special_signals()
    {
        var db = TheHomer();

        var lux = db.Messages[24].Signals[0];
        Assert.Equal("AmbientLux", lux.Name);
        Assert.Equal(64, lux.Length);
        Assert.True(lux.IsFloat);
        Assert.Equal("Lux", lux.Unit);

        var windshield = db.Messages[25].Signals[0];
        Assert.Equal("Windshield", windshield.Name);
        Assert.Equal(32, windshield.Length);
        Assert.True(windshield.IsFloat);
        Assert.Equal("% RH", windshield.Unit);

        var wheelAngle = db.Messages[4].Signals[1];
        Assert.Equal("WheelAngle", wheelAngle.Name);
        Assert.Equal(1, wheelAngle.StartBit);
        Assert.Equal(14, wheelAngle.Length);
        Assert.Equal(0.1, wheelAngle.Scale);
        Assert.Equal(-800, wheelAngle.Offset);
        var choices = wheelAngle.Choices!;
        Assert.True(choices[0] == "left");
        Assert.True(choices[8000] == "straight");
        Assert.True(choices[16000] == "right");
        Assert.True(choices[16382] == "init");
        Assert.True(choices[16383] == "sensor ");

        var bigEndianA = db.GetMessageByName("BigEndian").Signals[0];
        Assert.Equal("A", bigEndianA.Name);
        Assert.Equal(7, bigEndianA.StartBit);
        Assert.Equal(17, bigEndianA.Length);
        Assert.Equal(ByteOrder.BigEndian, bigEndianA.ByteOrder);

        Assert.Equal(1, db.GetMessageByName("LittleEndianAuto").Length);
        Assert.Equal(1, db.GetMessageByName("BigEndianAuto").Length);
        Assert.Equal(1, db.GetMessageByName("LittleBigEndianAuto").Length);
    }

    // ported from test_database.py::test_the_homer_encode_length
    [Fact]
    public void The_homer_encodes_with_the_message_length()
    {
        var db = TheHomer();
        var encoded = db.EncodeMessage(0x400u, Values(
            ("MIL", 0), ("Enginespeed", 127), ("NoxSensor", 127)));

        Assert.Equal(new byte[] { 0xfe, 0x00, 0xfe, 0x00, 0x00 }, encoded);
    }

    // ported from test_database.py::test_the_homer_float
    [Fact]
    public void The_homer_float_signals_round_trip()
    {
        var db = TheHomer();

        var encoded = db.EncodeMessage(0x732u, Values(("AmbientLux", Math.PI)));
        Assert.Equal(new byte[] { 0x18, 0x2d, 0x44, 0x54, 0xfb, 0x21, 0x09, 0x40 }, encoded);
        Assert.True(db.DecodeMessage(0x732u, encoded)["AmbientLux"] == Math.PI);

        var single = (double)(float)Math.PI;
        encoded = db.EncodeMessage(0x745u, Values(("Windshield", single)));
        Assert.Equal(new byte[] { 0xdb, 0x0f, 0x49, 0x40 }, encoded);
        Assert.True(db.DecodeMessage(0x745u, encoded)["Windshield"] == single);
    }

    // ported from test_database.py::test_the_homer_encode_decode_choices
    [Theory]
    [InlineData("disengaged", new byte[] { 0x00, 0x00 })]
    [InlineData("1", new byte[] { 0x00, 0x10 })]
    [InlineData("2", new byte[] { 0x00, 0x20 })]
    [InlineData("3", new byte[] { 0x00, 0x30 })]
    [InlineData("4", new byte[] { 0x00, 0x40 })]
    [InlineData("5", new byte[] { 0x00, 0x50 })]
    [InlineData("6", new byte[] { 0x00, 0x60 })]
    [InlineData(7, new byte[] { 0x00, 0x70 })]
    [InlineData(8, new byte[] { 0x00, 0x80 })]
    [InlineData(9, new byte[] { 0x00, 0x90 })]
    [InlineData("reverse", new byte[] { 0x00, 0xa0 })]
    [InlineData("Unspecific error", new byte[] { 0x00, 0xf0 })]
    public void The_homer_choices_encode_and_decode(object value, byte[] expected)
    {
        var db = TheHomer();
        SignalValue signalValue = value switch
        {
            string label => label,
            int number => number,
            _ => throw new ArgumentException(null, nameof(value)),
        };

        var encoded = db.EncodeMessage("Gear", Values(("EngagedGear", signalValue)));
        Assert.Equal(expected, encoded);
        Assert.True(db.DecodeMessage("Gear", encoded)["EngagedGear"] == signalValue);
    }

    // ported from test_database.py::test_the_homer_encode_decode_choice_scaling
    [Theory]
    [InlineData(0x700u, "one", new byte[] { 0x80, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(0x700u, "two", new byte[] { 0xff, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(0x701u, "one", new byte[] { 0x00, 0x00, 0x00, 0x43, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(0x701u, "two", new byte[] { 0x00, 0x00, 0x7f, 0x43, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(0x700u, 4, new byte[] { 0x02, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 })]
    [InlineData(0x701u, 4, new byte[] { 0x00, 0x00, 0x00, 0x40, 0x00, 0x00, 0x00, 0x00 })]
    public void The_homer_choices_are_raw_values(uint frameId, object value, byte[] expected)
    {
        var db = TheHomer();
        SignalValue signalValue = value switch
        {
            string label => label,
            int number => number,
            _ => throw new ArgumentException(null, nameof(value)),
        };
        var signalName = frameId == 0x700u ? "EnumTest" : "EnumTestFloat";

        var encoded = db.EncodeMessage(frameId, Values((signalName, signalValue)));
        Assert.Equal(expected, encoded);
        Assert.True(db.DecodeMessage(frameId, encoded)[signalName] == signalValue);
    }

    // ported from test_database.py::test_the_homer_encode_decode_big_endian
    [Fact]
    public void The_homer_big_endian_message_round_trips()
    {
        var db = TheHomer();
        var decoded = Values(("A", 0x140fa), ("B", 1), ("C", 18));

        var encoded = db.EncodeMessage("BigEndian", decoded);
        Assert.Equal(new byte[] { 0xa0, 0x7d, 0x64 }, encoded);

        var actual = db.DecodeMessage("BigEndian", encoded);
        Assert.True(actual["A"] == 0x140fa);
        Assert.True(actual["B"] == 1);
        Assert.True(actual["C"] == 18);
    }

    // ported from test_database.py::test_the_homer_encode_decode_signed
    [Theory]
    [InlineData(0, -32768, 0, new byte[] { 0x00, 0x00, 0x00, 0x80, 0x00 })]
    [InlineData(65535, 32767, 15, new byte[] { 0xff, 0xff, 0xff, 0x7f, 0x0f })]
    public void The_homer_signed_message_round_trips(
        int level, int temperature, int status, byte[] expected)
    {
        var db = TheHomer();
        var decoded = Values(
            ("TankLevel", level), ("TankTemperature", temperature), ("FillingStatus", status));

        var encoded = db.EncodeMessage("TankController", decoded);
        Assert.Equal(expected, encoded);

        var actual = db.DecodeMessage("TankController", encoded);
        Assert.True(actual["TankLevel"] == level);
        Assert.True(actual["TankTemperature"] == temperature);
        Assert.True(actual["FillingStatus"] == status);
    }

    // ported from test_database.py::test_encode_signal_strict (kcd ranges)
    [Fact]
    public void Signal_ranges_from_kcd_are_enforced()
    {
        var db = KcdReader.LoadFile(TestFiles.Kcd("signal_range.kcd"));

        db.EncodeMessage("Message1", Values(("Signal1", 1)));
        db.EncodeMessage("Message1", Values(("Signal1", 2)));
        db.EncodeMessage("Message2", Values(("Signal1", 3)));
        db.EncodeMessage("Message3", Values(("Signal1", 0)));

        var error = Assert.Throws<EncodeException>(
            () => db.EncodeMessage("Message1", Values(("Signal1", 0))));
        Assert.Equal(
            "Expected signal \"Signal1\" value greater than or equal to 1 in message "
            + "\"Message1\", but got 0.",
            error.Message);

        error = Assert.Throws<EncodeException>(
            () => db.EncodeMessage("Message4", Values(("Signal1", 1.9))));
        Assert.Equal(
            "Expected signal \"Signal1\" value greater than or equal to 2 in message "
            + "\"Message4\", but got 1.9.",
            error.Message);

        error = Assert.Throws<EncodeException>(
            () => db.EncodeMessage("Message4", Values(("Signal1", 8.1))));
        Assert.Equal(
            "Expected signal \"Signal1\" value smaller than or equal to 8 in message "
            + "\"Message4\", but got 8.1.",
            error.Message);

        db.EncodeMessage("Message1", Values(("Signal1", 3)), strict: false);
    }

    // ported from test_database.py::test_strict_load (kcd part)
    [Fact]
    public void Strict_load_rejects_signals_outside_their_message()
    {
        var error = Assert.Throws<CanToolsException>(
            () => KcdReader.LoadFile(TestFiles.Kcd("bad_message_length.kcd")));
        Assert.Equal("The signal Signal1 does not fit in message Message1.", error.Message);

        var db = KcdReader.LoadFile(TestFiles.Kcd("bad_message_length.kcd"), strict: false);
        var message = db.GetMessageByFrameId(1);
        Assert.Equal(1, message.Length);
        Assert.Equal(8, message.Signals[0].StartBit);
        Assert.Equal(1, message.Signals[0].Length);
    }

    // ported from test_database.py::test_empty_kcd
    [Fact]
    public void Empty_network_definitions_load()
    {
        var db = KcdReader.LoadFile(TestFiles.Kcd("empty.kcd"));

        Assert.Null(db.Version);
        Assert.Empty(db.Nodes);
        Assert.Empty(db.Messages);
    }

    // ported from test_database.py::test_invalid_kcd
    [Fact]
    public void Wrong_root_elements_are_rejected()
    {
        var error = Assert.Throws<ParseException>(
            () => KcdReader.LoadString("<WrongRootElement/>"));
        Assert.Equal(
            "Expected root element tag {http://kayak.2codeornot2code.org/1.0}NetworkDefinition, "
            + "but got WrongRootElement.",
            error.Message);
    }

    // ported from test_database.py::test_get_node_by_name / test_get_bus_by_name
    [Fact]
    public void Nodes_and_buses_are_found_by_name()
    {
        var db = TheHomer();

        Assert.Same(db.Nodes[1], db.GetNodeByName("Motor alternative supplier"));
        Assert.Same(db.Buses[2], db.GetBusByName("Comfort"));
        Assert.Throws<KeyNotFoundException>(() => db.GetNodeByName("Missing"));
        Assert.Throws<KeyNotFoundException>(() => db.GetBusByName("Missing"));
    }
}
