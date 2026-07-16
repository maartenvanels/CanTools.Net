using CanTools.Formats;
using CanTools.Formats.Dbc;
using CanTools.Model;

namespace CanTools.Tests;

// Ported from the DBC-reader parts of tests/test_database.py. Python repr-string
// assertions are ported as property assertions.
public class DbcReaderTests
{
    private static Database Load(string name, bool strict = true, bool pruneChoices = false) =>
        DbcReader.LoadFile(TestFiles.Dbc(name), strict: strict, pruneChoices: pruneChoices);

    private static Dictionary<string, SignalValue> Values(params (string Name, SignalValue Value)[] entries) =>
        entries.ToDictionary(e => e.Name, e => e.Value);

    private static void AssertDecoded(
        Dictionary<string, SignalValue> actual, params (string Name, SignalValue Value)[] expected)
    {
        Assert.Equal(expected.Length, actual.Count);
        foreach (var (name, value) in expected)
        {
            Assert.True(actual[name] == value, $"{name}: expected {value}, got {actual[name]}");
        }
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

    // ported from test_database.py::test_vehicle
    [Fact]
    public void Vehicle_database_loads_completely()
    {
        var db = Load("vehicle.dbc");

        Assert.Equal(1, db.Nodes.Count);
        Assert.Equal("UnusedNode", db.Nodes[0].Name);
        Assert.Null(db.Nodes[0].Comment);
        Assert.Equal(217, db.Messages.Count);

        var first = db.Messages[0];
        Assert.Equal("RT_SB_INS_Vel_Body_Axes", first.Name);
        Assert.Equal(0x9588322u, first.FrameId);
        Assert.True(first.IsExtendedFrame);
        Assert.Equal(8, first.Length);
        Assert.Null(first.Comment);
        Assert.Null(first.CycleTime);
        Assert.Null(first.SendType);

        var signal = first.Signals[0];
        Assert.Equal("Validity_INS_Vel_Forwards", signal.Name);
        Assert.True(signal.Initial == 0);
        Assert.Empty(signal.Receivers);
        Assert.Equal("Valid when bit is set, invalid when bit is clear.", signal.Comment);

        var last = db.Messages[216];
        Assert.Null(last.Protocol);
        Assert.Equal("RT_SB_Gyro_Rates", last.Name);
        Assert.Equal(155872546u, last.FrameId);
        Assert.Empty(last.Senders);

        var choiceCount = db.Messages
            .SelectMany(message => message.Signals)
            .Count(s => s.Choices is not null);
        Assert.Equal(15, choiceCount);
    }

    // ported from test_database.py::test_dbc_signal_initial_value
    [Fact]
    public void Initial_values_come_from_GenSigStartValue()
    {
        var db = Load("vehicle.dbc");
        var message = db.GetMessageByName("RT_IMU06_Accel");

        var longitudinalValidity = message.GetSignalByName("Validity_Accel_Longitudinal");
        Assert.Null(longitudinalValidity.Initial);
        Assert.Null(longitudinalValidity.RawInitial);

        Assert.True(message.GetSignalByName("Validity_Accel_Lateral").RawInitial == 1);
        Assert.True(message.GetSignalByName("Validity_Accel_Lateral").Initial == 1);
        Assert.True(message.GetSignalByName("Validity_Accel_Vertical").RawInitial == 0);
        Assert.True(message.GetSignalByName("Accuracy_Accel").RawInitial == 127);

        var longitudinal = message.GetSignalByName("Accel_Longitudinal");
        Assert.True(longitudinal.RawInitial == 32767);
        Assert.True(longitudinal.Initial == 32.767);
        Assert.True(longitudinal.IsSigned);
        Assert.Equal(0.001, longitudinal.Scale);
        Assert.Equal(-65, longitudinal.Minimum);
        Assert.Equal(65, longitudinal.Maximum);
        Assert.Equal("g", longitudinal.Unit);

        Assert.True(message.GetSignalByName("Accel_Lateral").RawInitial == -30000);
        Assert.True(message.GetSignalByName("Accel_Lateral").Initial == -30.0);
        Assert.True(message.GetSignalByName("Accel_Vertical").RawInitial == 16120);
        Assert.True(message.GetSignalByName("Accel_Vertical").Initial == 16.120);
    }

    // ported from test_database.py::test_motohawk
    [Fact]
    public void Motohawk_nodes_and_receivers()
    {
        var db = Load("motohawk.dbc");

        Assert.Empty(db.Buses);
        Assert.Equal(2, db.Nodes.Count);
        Assert.Equal("PCM1", db.Nodes[0].Name);
        Assert.Equal("FOO", db.Nodes[1].Name);
        Assert.Equal(1, db.Messages.Count);
        Assert.Null(db.Messages[0].BusName);

        Assert.Equal(["PCM1", "FOO"], db.Messages[0].Signals[2].Receivers);
        Assert.Empty(db.Messages[0].Signals[1].Receivers);
    }

    // ported from test_database.py::test_emc32
    [Fact]
    public void Environment_variables_are_loaded()
    {
        var db = Load("emc32.dbc");

        Assert.Equal(1, db.Nodes.Count);
        Assert.Equal("EMV_Statusmeldungen", db.Nodes[0].Name);
        Assert.Equal(1, db.Messages.Count);
        Assert.Equal("EMV_Aktion_Status_3", db.Messages[0].Signals[0].Name);
        Assert.Single(db.Messages[0].Signals[0].Receivers);
        Assert.Equal("EMV_Aktion_Status_4", db.Messages[0].Signals[1].Name);
        Assert.Empty(db.Messages[0].Signals[1].Receivers);

        Assert.Equal(17, db.Dbc!.EnvironmentVariables.Count);

        var azimuth = db.Dbc.EnvironmentVariables["EMC_Azimuth"];
        Assert.Equal("EMC_Azimuth", azimuth.Name);
        Assert.Equal(1, azimuth.EnvType);
        Assert.Equal(-180, azimuth.Minimum);
        Assert.Equal(400, azimuth.Maximum);
        Assert.Equal("deg", azimuth.Unit);
        Assert.Equal(0, azimuth.InitialValue);
        Assert.Equal(12, azimuth.EnvId);
        Assert.Equal("DUMMY_NODE_VECTOR0", azimuth.AccessType);
        Assert.Equal("Vector__XXX", azimuth.AccessNode);
        Assert.Equal("Elevation Head", azimuth.Comment);

        Assert.Null(db.Dbc.EnvironmentVariables["EMC_TrdPower"].Comment);
    }

    // ported from test_database.py::test_foobar (repr assertions as properties)
    [Fact]
    public void Foobar_database_model()
    {
        var db = Load("foobar.dbc");

        Assert.Equal(4, db.Nodes.Count);
        Assert.Equal("2.0", db.Version);
        Assert.Equal(["FOO", "BAR", "FIE", "FUM"], db.Nodes.Select(n => n.Name));
        Assert.Equal("fam \"1\"", db.Nodes[1].Comment);

        Assert.Equal(1, db.Buses.Count);
        Assert.Equal("TheBusName", db.Buses[0].Name);
        Assert.Null(db.Buses[0].Comment);
        Assert.Equal(125000, db.Buses[0].Baudrate);

        Assert.Equal(
            ["Foo", "Fum", "Bar", "CanFd", "FOOBAR"],
            db.Messages.Select(m => m.Name));

        var foo = db.GetMessageByName("Foo");
        Assert.Equal(0x12330u, foo.FrameId);
        Assert.True(foo.IsExtendedFrame);
        Assert.Equal(8, foo.Length);
        Assert.Equal("Foo.", foo.Comment);
        var fooSignal = foo.GetSignalByName("Foo");
        Assert.Equal(ByteOrder.BigEndian, fooSignal.ByteOrder);
        Assert.True(fooSignal.IsSigned);
        Assert.Equal(0.01, fooSignal.Scale);
        Assert.Equal(250, fooSignal.Offset);
        Assert.Equal(229.53, fooSignal.Minimum);
        Assert.Equal(270.47, fooSignal.Maximum);
        Assert.Equal("degK", fooSignal.Unit);
        Assert.Equal("Bar.", foo.GetSignalByName("Bar").Comment);

        var fum = db.GetMessageByFrameId(0x12331);
        Assert.Equal("TheBusName", fum.BusName);
        Assert.Equal(["FOO"], fum.Senders);
        Assert.False(fum.Signals[0].IsFloat);
        var fam = fum.GetSignalByName("Fam");
        Assert.NotNull(fam.Choices);
        Assert.True(fam.Choices[1] == "Enabled");
        Assert.True(fam.Choices[0] == "Disabled");

        var bar = db.GetMessageByFrameId(0x12332);
        Assert.Equal(["FOO", "BAR"], bar.Senders);
        Assert.Equal(["FUM"], bar.Signals[0].Receivers);
        Assert.True(bar.Signals[0].IsFloat);
        Assert.Equal(32, bar.Signals[0].Length);

        var canFd = db.GetMessageByFrameId(0x12333);
        Assert.Equal(["FOO"], canFd.Senders);
        Assert.Equal(["FUM"], canFd.Signals[0].Receivers);
        Assert.False(canFd.Signals[0].IsFloat);
        Assert.Equal(64, canFd.Signals[0].Length);
        Assert.Equal(64, canFd.Length);
    }

    // ported from test_database.py::test_foobar_encode_decode
    [Fact]
    public void Foobar_encodes_and_decodes_by_name()
    {
        var db = Load("foobar.dbc");

        var cases = new (string Name, (string, SignalValue)[] Decoded, byte[] Encoded)[]
        {
            ("Foo", [("Foo", 250.0), ("Bar", 0.0)],
             [0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00]),
            ("Fum", [("Fum", 9), ("Fam", 5)],
             [0x09, 0x50, 0x00, 0x00, 0x00]),
            ("Bar", [("Binary32", 1.0)],
             [0x00, 0x00, 0x80, 0x3f]),
            ("CanFd", [("Fie", 0x123456789abcdefL), ("Fas", 0xdeadbeefdeadbeefUL)],
             [
                 0xef, 0xcd, 0xab, 0x89, 0x67, 0x45, 0x23, 0x01,
                 0xef, 0xbe, 0xad, 0xde, 0xef, 0xbe, 0xad, 0xde,
                 .. new byte[48],
             ]),
        };

        foreach (var (name, decoded, encoded) in cases)
        {
            var values = Values(decoded);
            Assert.Equal(encoded, db.EncodeMessage(name, values, strict: false));
            AssertDecoded(db.DecodeMessage(name, encoded, decodeChoices: false), decoded);
        }
    }

    // ported from test_database.py::test_dbc_dump_val_table (reader half)
    [Fact]
    public void Value_tables_are_loaded()
    {
        var db = Load("val_table.dbc");
        var tables = db.Dbc!.ValueTables;

        Assert.Equal(["Table3", "Table2", "Table1"], tables.Keys);
        Assert.True(tables["Table1"][0] == "Zero");
        Assert.True(tables["Table1"][1] == "One");
        Assert.Empty(tables["Table2"]);
        Assert.True(tables["Table3"][16] == "16");
        Assert.True(tables["Table3"][0] == "0");
        Assert.True(tables["Table3"][2] == "2");
        Assert.True(tables["Table3"][7] == "7");
    }

    // ported from test_database.py::test_dbc_load_empty_choice
    [Fact]
    public void Empty_choice_lists_become_null()
    {
        var db = Load("empty_choice.dbc");
        var message = db.GetMessageByFrameId(10);

        Assert.Equal("non_empty_choice", message.Signals[0].Name);
        Assert.NotNull(message.Signals[0].Choices);
        Assert.True(message.Signals[0].Choices![254] == "Error");
        Assert.True(message.Signals[0].Choices![255] == "Not available");

        Assert.Equal("empty_choice", message.Signals[1].Name);
        Assert.Null(message.Signals[1].Choices);
        Assert.Equal("no_choice", message.Signals[2].Name);
        Assert.Null(message.Signals[2].Choices);
    }

    // ported from test_database.py::test_dbc_load_choices
    [Fact]
    public void Choices_keep_value_and_name()
    {
        var db = Load("choices.dbc");
        var choice = db.Messages[0].Signals[0].Choices![0];

        Assert.Equal(0, choice.Value);
        Assert.Equal("With space", choice.Name);
    }

    // ported from test_database.py::test_dbc_load_choices_issue_with_name
    [Fact]
    public void Choice_pruning_strips_common_prefixes()
    {
        var db = Load("choices_issue_with_name.dbc");
        var choices = db.Messages[0].Signals[0].Choices!;
        Assert.Equal("SignalWithChoices_CmdRespErr", choices[0].Name);
        Assert.Equal("SignalWithChoices_CmdRespOK", choices[1].Name);

        db = Load("choices_issue_with_name.dbc", pruneChoices: true);
        choices = db.Messages[0].Signals[0].Choices!;
        Assert.Equal(0, choices[0].Value);
        Assert.Equal("CmdRespErr", choices[0].Name);
        Assert.Equal(1, choices[1].Value);
        Assert.Equal("CmdRespOK", choices[1].Name);
    }

    // ported from test_database.py::test_socialledge
    [Fact]
    public void Socialledge_comments_choices_and_mux()
    {
        var db = Load("socialledge.dbc");

        Assert.Equal("", db.Version);
        Assert.Equal(5, db.Nodes.Count);
        Assert.Equal("DBG", db.Nodes[0].Name);
        Assert.Null(db.Nodes[0].Comment);
        Assert.Equal("DRIVER", db.Nodes[1].Name);
        Assert.Equal("// The driver controller driving the car //", db.Nodes[1].Comment);
        Assert.Equal("MOTOR", db.Nodes[3].Name);
        Assert.Equal("The motor controller of the car", db.Nodes[3].Comment);
        Assert.Equal("SENSOR", db.Nodes[4].Name);
        Assert.Equal("The sensor controller of the car", db.Nodes[4].Comment);

        var heartbeat = db.GetMessageByName("DRIVER_HEARTBEAT");
        Assert.Equal("Sync message used to synchronize the controllers", heartbeat.Comment);
        var command = heartbeat.Signals[0].Choices!;
        Assert.True(command[0] == "DRIVER_HEARTBEAT_cmd_NOOP");
        Assert.True(command[1] == "DRIVER_HEARTBEAT_cmd_SYNC");
        Assert.True(command[2] == "DRIVER_HEARTBEAT_cmd_REBOOT");
        Assert.False(heartbeat.IsMultiplexed);

        var sonars = db.GetMessageByName("SENSOR_SONARS");
        Assert.True(sonars.IsMultiplexed);
        Assert.Equal("SENSOR_SONARS_no_filt_rear", sonars.Signals[^1].Name);
        Assert.Equal([1L], sonars.Signals[^1].MultiplexerIds!);
        Assert.Equal("SENSOR_SONARS_left", sonars.Signals[2].Name);
        Assert.Equal([0L], sonars.Signals[2].MultiplexerIds!);
        Assert.Equal("SENSOR_SONARS_mux", sonars.Signals[0].Name);
        Assert.True(sonars.Signals[0].IsMultiplexer);

        Assert.Equal(2, sonars.SignalTree.Count);
        var mux = AssertMux(sonars.SignalTree[0], "SENSOR_SONARS_mux", 0, 1);
        AssertLeaves(mux[0],
            "SENSOR_SONARS_left", "SENSOR_SONARS_middle",
            "SENSOR_SONARS_right", "SENSOR_SONARS_rear");
        AssertLeaves(mux[1],
            "SENSOR_SONARS_no_filt_left", "SENSOR_SONARS_no_filt_middle",
            "SENSOR_SONARS_no_filt_right", "SENSOR_SONARS_no_filt_rear");
        Assert.Equal("SENSOR_SONARS_err_count", sonars.SignalTree[1].Name);
    }

    // ported from test_database.py::test_get_message_by_frame_id_and_name
    [Fact]
    public void Messages_are_found_by_name_and_frame_id()
    {
        var db = Load("motohawk.dbc");

        Assert.Equal("ExampleMessage", db.GetMessageByName("ExampleMessage").Name);
        Assert.Equal(496u, db.GetMessageByFrameId(496).FrameId);
    }

    // ported from test_database.py::test_get_signal_by_name
    [Fact]
    public void Signals_are_found_by_name()
    {
        var db = Load("foobar.dbc");
        var message = db.GetMessageByName("Foo");

        Assert.Equal("Foo", message.GetSignalByName("Foo").Name);
        Assert.Equal("Bar", message.GetSignalByName("Bar").Name);
        Assert.Throws<KeyNotFoundException>(() => message.GetSignalByName("Fum"));
    }

    // ported from test_database.py::test_timing
    [Fact]
    public void Cycle_time_and_send_type_come_from_attributes()
    {
        var db = Load("timing.dbc");

        var message1 = db.GetMessageByFrameId(1);
        Assert.Equal(200, message1.CycleTime);
        Assert.Equal("Cyclic", message1.SendType);

        var message2 = db.GetMessageByFrameId(2);
        Assert.Null(message2.CycleTime);
        Assert.Equal("NoMsgSendType", message2.SendType);
    }

    // ported from test_database.py::test_multiplex (reader half)
    [Fact]
    public void Multiplexed_message_loads_from_file()
    {
        var db = Load("multiplex.dbc");
        var message = db.Messages[0];

        Assert.True(message.IsMultiplexed);

        var mux = AssertMux(message.SignalTree.Single(), "Multiplexor", 8, 16, 24);
        AssertLeaves(mux[8], "BIT_J", "BIT_C", "BIT_G", "BIT_L");
        AssertLeaves(mux[16], "BIT_J", "BIT_C", "BIT_G", "BIT_L");
        AssertLeaves(mux[24],
            "BIT_J", "BIT_C", "BIT_G", "BIT_L", "BIT_A", "BIT_K",
            "BIT_E", "BIT_D", "BIT_B", "BIT_H", "BIT_F");

        Assert.True(message.Signals[0].IsMultiplexer);
        Assert.False(message.Signals[1].IsMultiplexer);
        Assert.Equal([8L, 16L, 24L], message.Signals[1].MultiplexerIds!);
        Assert.Equal([24L], message.Signals[5].MultiplexerIds!);

        AssertDecoded(
            message.Decode([0x20, 0x00, 0x8c, 0x01, 0x00, 0x00, 0x00, 0x00]),
            ("Multiplexor", 8), ("BIT_C", 1), ("BIT_G", 1), ("BIT_J", 1), ("BIT_L", 1));
    }

    // ported from test_database.py::test_multiplex_choices
    [Fact]
    public void Multiplexed_choices_load_from_file()
    {
        var db = Load("multiplex_choices.dbc");

        var message1 = db.Messages[0];
        var encoded = message1.Encode(Values(
            ("Multiplexor", "MULTIPLEXOR_8"),
            ("BIT_C", 1), ("BIT_G", 1), ("BIT_J", 1), ("BIT_L", "On")));
        Assert.Equal(new byte[] { 0x20, 0x00, 0x8c, 0x01, 0x00, 0x00, 0x00, 0x00 }, encoded);
        AssertDecoded(
            message1.Decode(encoded),
            ("Multiplexor", "MULTIPLEXOR_8"),
            ("BIT_C", 1), ("BIT_G", 1), ("BIT_J", 1), ("BIT_L", "On"));

        var message2 = db.Messages[1];
        var mux = AssertMux(message2.SignalTree.Single(), "Multiplexor", 4, 8, 16, 24);
        Assert.Empty(mux[4]);
        AssertDecoded(
            message2.Decode([0x10, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00], decodeChoices: false),
            ("Multiplexor", 4));
    }

    // ported from test_database.py::test_multiplex_2 (extended multiplexing)
    [Fact]
    public void Extended_multiplexing_loads_from_file()
    {
        var db = Load("multiplex_2.dbc");

        // Shared: one selector, several signals sharing multiplexer ids.
        var shared = AssertMux(db.Messages[0].SignalTree.Single(), "S0", 1, 2, 3, 4, 5);
        AssertLeaves(shared[1], "S1");
        AssertLeaves(shared[2], "S2");
        AssertLeaves(shared[3], "S1", "S2");
        AssertLeaves(shared[4], "S2");
        AssertLeaves(shared[5], "S2");

        var normal = AssertMux(db.Messages[1].SignalTree.Single(), "S0", 0, 1);
        AssertLeaves(normal[0], "S1");
        AssertLeaves(normal[1], "S2");

        var extended = db.Messages[2].SignalTree;
        Assert.Equal(2, extended.Count);
        var s0 = AssertMux(extended[0], "S0", 0, 1);
        var s1 = AssertMux(Assert.Single(s0[0]), "S1", 0, 2);
        AssertLeaves(s1[0], "S2", "S3");
        AssertLeaves(s1[2], "S4");
        AssertLeaves(s0[1], "S5");
        var s6 = AssertMux(extended[1], "S6", 1, 2);
        AssertLeaves(s6[1], "S7");
        AssertLeaves(s6[2], "S8");

        var extendedTypes = db.Messages[3].SignalTree;
        var s11 = AssertMux(Assert.Single(extendedTypes), "S11", 3, 5);
        var s0Nested = AssertMux(Assert.Single(s11[3]), "S0", 0);
        AssertLeaves(s0Nested[0], "S10");
        AssertLeaves(s11[5], "S9");
    }

    // ported from test_database.py::test_event_attributes
    [Fact]
    public void Send_type_from_enum_attribute()
    {
        var db = Load("attribute_Event.dbc");

        Assert.Equal("INV2EventMsg1", db.Messages[0].Name);
        Assert.Equal(1234u, db.Messages[0].FrameId);
        Assert.Equal("Event", db.Messages[0].SendType);
    }

    // ported from test_database.py::test_attributes
    [Fact]
    public void Attributes_on_every_level()
    {
        var db = Load("attributes.dbc");
        var definitions = db.Dbc!.AttributeDefinitions;

        // Signal attributes.
        var signalAttributes = db.Messages[0].Signals[0].Dbc!.Attributes;

        var stringAttribute = signalAttributes["TheSignalStringAttribute"];
        Assert.Equal("TheSignalStringAttribute", stringAttribute.Name);
        Assert.True(stringAttribute.Value == "TestString");
        Assert.Same(definitions["TheSignalStringAttribute"], stringAttribute.Definition);
        Assert.True(stringAttribute.Definition.DefaultValue == "100");
        Assert.Equal("SG_", stringAttribute.Definition.Kind);
        Assert.Equal("STRING", stringAttribute.Definition.TypeName);
        Assert.Null(stringAttribute.Definition.Minimum);
        Assert.Null(stringAttribute.Definition.Maximum);
        Assert.Empty(stringAttribute.Definition.Choices);

        var sendType = signalAttributes["GenSigSendType"];
        Assert.True(sendType.Value == 1);
        Assert.True(sendType.Definition.DefaultValue == "Cyclic");
        Assert.Equal("SG_", sendType.Definition.Kind);
        Assert.Equal("ENUM", sendType.Definition.TypeName);
        Assert.Equal(
            ["Cyclic", "OnWrite", "OnWriteWithRepetition", "OnChange",
             "OnChangeWithRepetition", "IfActive", "IfActiveWithRepetition",
             "NoSigSendType", "NotUsed", "NotUsed", "NotUsed", "NotUsed", "NotUsed"],
            sendType.Definition.Choices);

        // Message attributes.
        var messageAttributes = db.Messages[0].Dbc!.Attributes;
        Assert.Equal(4, messageAttributes.Count);

        var hexAttribute = messageAttributes["TheHexAttribute"];
        Assert.True(hexAttribute.Value == 5);
        Assert.True(hexAttribute.Definition.DefaultValue == 4);
        Assert.Equal("BO_", hexAttribute.Definition.Kind);
        Assert.Equal("HEX", hexAttribute.Definition.TypeName);
        Assert.Equal(0, hexAttribute.Definition.Minimum);
        Assert.Equal(8, hexAttribute.Definition.Maximum);

        var floatAttribute = messageAttributes["TheFloatAttribute"];
        Assert.True(floatAttribute.Value == 58.7);
        Assert.True(floatAttribute.Definition.DefaultValue == 55.0);
        Assert.Equal("FLOAT", floatAttribute.Definition.TypeName);
        Assert.Equal(5.0, floatAttribute.Definition.Minimum);
        Assert.Equal(87.0, floatAttribute.Definition.Maximum);

        // Node attributes.
        Assert.Equal("TheNode", db.Nodes[0].Name);
        Assert.Equal("TheNodeComment", db.Nodes[0].Comment);
        var nodeAttribute = db.Nodes[0].Dbc!.Attributes["TheNodeAttribute"];
        Assert.True(nodeAttribute.Value == 99);
        Assert.True(nodeAttribute.Definition.DefaultValue == 100);
        Assert.Equal("BU_", nodeAttribute.Definition.Kind);
        Assert.Equal("INT", nodeAttribute.Definition.TypeName);
        Assert.Equal(50, nodeAttribute.Definition.Minimum);
        Assert.Equal(150, nodeAttribute.Definition.Maximum);

        // Database attributes.
        var databaseAttributes = db.Dbc.Attributes;
        var busType = databaseAttributes["BusType"];
        Assert.True(busType.Value == "CAN");
        Assert.True(busType.Definition.DefaultValue == "");
        Assert.Null(busType.Definition.Kind);
        Assert.Equal("STRING", busType.Definition.TypeName);
        var networkAttribute = databaseAttributes["TheNetworkAttribute"];
        Assert.True(networkAttribute.Value == 51);
        Assert.True(networkAttribute.Definition.DefaultValue == 50);
        Assert.Equal("INT", networkAttribute.Definition.TypeName);
        Assert.Equal(0, networkAttribute.Definition.Minimum);
        Assert.Equal(100, networkAttribute.Definition.Maximum);

        // Attribute-driven message fields.
        var message = db.GetMessageByFrameId(0x39, forceExtendedId: true);
        Assert.Equal(1000, message.CycleTime);
        Assert.Equal("Cyclic", message.SendType);
    }

    // ported from test_database.py::test_j1939_dbc
    [Fact]
    public void J1939_protocol_and_spn()
    {
        var db = Load("j1939.dbc");

        Assert.Equal("Message1", db.Messages[0].Name);
        Assert.Equal(0x15340201u, db.Messages[0].FrameId);
        Assert.Equal("j1939", db.Messages[0].Protocol);
        Assert.Equal(500, db.Messages[0].Signals[0].Spn);
        Assert.Equal(0, db.Messages[1].Signals[0].Spn);
    }

    // ported from test_database.py::test_float_dbc
    [Fact]
    public void Sig_valtype_makes_signals_float()
    {
        var db = Load("floating_point.dbc");

        var message1 = db.GetMessageByFrameId(1024);
        Assert.True(message1.GetSignalByName("Signal1").IsFloat);
        Assert.Equal(64, message1.GetSignalByName("Signal1").Length);
        Assert.Equal(
            new byte[] { 0x75, 0x93, 0x18, 0x04, 0x56, 0x2e, 0x60, 0xc0 },
            message1.Encode(Values(("Signal1", -129.448))));

        var message2 = db.GetMessageByFrameId(1025);
        Assert.True(message2.GetSignalByName("Signal1").IsFloat);
        Assert.Equal(32, message2.GetSignalByName("Signal1").Length);
        Assert.Equal(
            new byte[] { 0x00, 0x80, 0x01, 0x43, 0x24, 0xb2, 0x96, 0x49 },
            message2.Encode(Values(("Signal1", 129.5), ("Signal2", 1234500.5))));
    }

    // ported from test_database.py::test_long_names_dbc
    [Fact]
    public void Long_names_are_resolved_from_attributes()
    {
        var db = Load("long_names.dbc");

        Assert.Equal(
            ["NN123456789012345678901234567890123",
             "N123456789012345678901234567890123",
             "N1234567890123456789012345678901",
             "N12345678901234567890123456789012"],
            db.Nodes.Select(n => n.Name));

        Assert.Equal(
            ["SS12345678901234567890123458789012345",
             "SS1234567890123456789012345778901",
             "SS1234567890123456789012345878901234",
             "SS123456789012345678901234577890",
             "SS12345678901234567890123456789012",
             "S12345678901234567890123456789012",
             "M123456789012345678901234567890123",
             "M1234567890123456789012345678901",
             "M12345678901234567890123456789012",
             "MM12345678901234567890123456789012"],
            db.Messages.Select(m => m.Name));

        Assert.Equal(["N1234567890123456789012345678901"], db.Messages[7].Senders);
        Assert.Equal(["N12345678901234567890123456789012"], db.Messages[8].Senders);

        Assert.Equal("SS12345678901234567890123456789012", db.Messages[5].Signals[0].Name);
        Assert.Equal("SSS12345678901234567890123456789012", db.Messages[6].Signals[0].Name);
        Assert.Equal(
            ["S1234567890123456789012345678901",
             "S123456789012345678901234567890123",
             "SS12345678901234567890123456789012",
             "SS1234567890123456789012345678901233",
             "SS12345678901234567890123456789012332"],
            db.Messages[7].Signals.Select(s => s.Name));
        Assert.Equal(
            ["N123456789012345678901234567890123"],
            db.Messages[7].Signals[2].Receivers);

        var environmentVariables = db.Dbc!.EnvironmentVariables;
        Assert.True(environmentVariables.ContainsKey("E1234567890123456789012345678901"));
        Assert.False(environmentVariables.ContainsKey("E12345678901234567890123456_0000"));
        Assert.True(environmentVariables.ContainsKey("E12345678901234567890123456789012"));

        var group = db.Messages[9].SignalGroups!.Single();
        Assert.Equal("SS1234_sg", group.Name);
        Assert.Equal("SS1234567890123456789012345678901233", group.SignalNames[0]);
        Assert.Equal("SSS12345678901234567890123456789012", group.SignalNames[1]);
    }

    // ported from test_database.py::test_multiple_senders
    [Fact]
    public void Senders_merge_from_BO_TX_BU()
    {
        var db = Load("multiple_senders.dbc");

        Assert.Equal(["FOO", "BAR", "FIE"], db.GetMessageByFrameId(1).Senders);
    }

    // ported from test_database.py::test_fd_detection
    [Fact]
    public void VFrameFormat_controls_fd_and_extended()
    {
        var db = Load("fd_test.dbc");

        Assert.True(db.GetMessageByName("TestMsg_FDEx").IsFd);
        Assert.True(db.GetMessageByName("TestMsg_FDEx").IsExtendedFrame);
        Assert.True(db.GetMessageByName("TestMsg_FDStd").IsFd);
        Assert.False(db.GetMessageByName("TestMsg_FDStd").IsExtendedFrame);
        Assert.False(db.GetMessageByName("TestMsg_Std").IsFd);
        Assert.False(db.GetMessageByName("TestMsg_Std").IsExtendedFrame);
        Assert.False(db.GetMessageByName("TestMsg_Ex").IsFd);
        Assert.True(db.GetMessageByName("TestMsg_Ex").IsExtendedFrame);
    }

    // ported from test_database.py::test_dbc_parse_error_messages
    [Fact]
    public void Parse_errors_point_at_the_offending_token()
    {
        var error = Assert.Throws<ParseException>(() => DbcReader.LoadString("abc"));
        Assert.Equal("Invalid syntax at line 1, column 1: \">>!<<abc\"", error.Message);

        error = Assert.Throws<ParseException>(
            () => DbcReader.LoadString("VERSION \"1.0\"\nBO_ dssd\n"));
        Assert.Equal("Invalid syntax at line 2, column 5: \"BO_ >>!<<dssd\"", error.Message);

        error = Assert.Throws<ParseException>(
            () => DbcReader.LoadString("VERSION \"1.0\"\ndd\n"));
        Assert.Equal("Invalid syntax at line 2, column 1: \">>!<<dd\"", error.Message);

        error = Assert.Throws<ParseException>(
            () => DbcReader.LoadString("VERSION \"1.0\"\nBO_ 546 EMV_Stati 8 EMV_Statusmeldungen\n"));
        Assert.Equal(
            "Invalid syntax at line 2, column 19: \"BO_ 546 EMV_Stati >>!<<8 EMV_Statusmeldungen\"",
            error.Message);

        error = Assert.Throws<ParseException>(
            () => DbcReader.LoadString("CM_ BO_ \"Foo.\";"));
        Assert.Equal(
            "Invalid syntax at line 1, column 9: \"CM_ BO_ >>!<<\"Foo.\";\"",
            error.Message);
    }

    // ported from test_database.py::test_strict_load (dbc part)
    [Fact]
    public void Strict_load_rejects_signals_outside_their_message()
    {
        var error = Assert.Throws<CanToolsException>(
            () => Load("bad_message_length.dbc"));
        Assert.Equal("The signal Signal1 does not fit in message Message1.", error.Message);

        var db = Load("bad_message_length.dbc", strict: false);
        var message = db.GetMessageByFrameId(1);
        Assert.Equal(1, message.Length);
        Assert.Equal(8, message.Signals[0].StartBit);
        Assert.Equal(1, message.Signals[0].Length);
    }

    // ported from test_database.py::test_issue_63
    [Fact]
    public void Overlapping_signals_fail_at_load()
    {
        var error = Assert.Throws<CanToolsException>(() => Load("issue_63.dbc"));
        Assert.Equal(
            "The signals HtrRes and MaxRes are overlapping in message AFT1PSI2.",
            error.Message);
    }

    // ported from test_database.py::test_dbc_issue_199_more_than_11_bits_standard_frame_id
    [Fact]
    public void Too_wide_frame_ids_fail_at_load()
    {
        var standard = Assert.Throws<CanToolsException>(() => Load("issue_199.dbc"));
        Assert.Equal(
            "Standard frame id 0x10630000 is more than 11 bits in message DriverDoorStatus.",
            standard.Message);

        var extended = Assert.Throws<CanToolsException>(() => Load("issue_199_extended.dbc"));
        Assert.Equal(
            "Extended frame id 0x7fffffff is more than 29 bits in message DriverDoorStatus.",
            extended.Message);
    }

    // ported from test_database.py::test_add_two_dbc_files
    [Fact]
    public void Two_dbc_files_merge_into_one_database()
    {
        var db = new Database();
        db.AddDbcFile(TestFiles.Dbc("add_two_dbc_files_1.dbc"));

        Assert.Equal(2, db.Messages.Count);
        Assert.Equal(1u, db.GetMessageByName("M1").FrameId);
        Assert.Equal("M1", db.GetMessageByFrameId(1).Name);

        db.AddDbcFile(TestFiles.Dbc("add_two_dbc_files_2.dbc"));

        Assert.Equal(3, db.Messages.Count);
        Assert.Equal(2u, db.GetMessageByName("M1").FrameId);
        Assert.Equal("M1", db.GetMessageByFrameId(2).Name);
    }

    // ported from test_database.py::test_empty_ns_dbc
    [Fact]
    public void Empty_NS_section_loads()
    {
        var db = Load("empty_ns.dbc");

        Assert.Empty(db.Nodes);
    }

    // ported from test_database.py::test_motohawk_encode_decode (smoke test via file)
    [Fact]
    public void Motohawk_round_trips_through_the_file()
    {
        var db = Load("motohawk.dbc");
        var values = Values(("Temperature", 250.55), ("AverageRadius", 3.2), ("Enable", "Enabled"));
        var encoded = db.EncodeMessage("ExampleMessage", values);

        Assert.Equal(new byte[] { 0xc0, 0x06, 0xe0, 0x00, 0x00, 0x00, 0x00, 0x00 }, encoded);
        AssertDecoded(
            db.DecodeMessage(496u, encoded),
            ("Temperature", 250.55), ("AverageRadius", 3.2), ("Enable", "Enabled"));
    }
}
