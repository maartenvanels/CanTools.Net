using CanTools.Formats.Dbc;
using CanTools.Model;

namespace CanTools.Tests;

// Ported from the DBC-writer parts of tests/test_database.py. Like upstream's
// assert_dbc_dump, dumps are compared semantically: the dump is reloaded and
// checked against the (reloaded) golden file.
public class DbcWriterTests
{
    private static Database Load(string name) => DbcReader.LoadFile(TestFiles.Dbc(name));

    private static void AssertDump(Database db, string goldenFileName)
    {
        var reloaded = DbcReader.LoadString(db.ToDbcString());
        var golden = Load(goldenFileName);

        DatabaseComparer.AssertEquivalent(golden, reloaded);
    }

    // ported from test_database.py::assert_dbc_dump usages with golden files
    [Theory]
    [InlineData("multiplex.dbc", "multiplex_dumped.dbc")]
    [InlineData("multiplex_choices.dbc", "multiplex_choices_dumped.dbc")]
    [InlineData("multiplex_2.dbc", "multiplex_2_dumped.dbc")]
    [InlineData("issue_184_extended_mux_multiple_values.dbc",
        "issue_184_extended_mux_multiple_values_dumped.dbc")]
    [InlineData("issue_184_extended_mux_independent_multiplexors.dbc",
        "issue_184_extended_mux_independent_multiplexors_dumped.dbc")]
    [InlineData("issue_184_extended_mux_cascaded.dbc",
        "issue_184_extended_mux_cascaded_dumped.dbc")]
    public void Dump_matches_the_golden_file(string input, string golden)
    {
        AssertDump(Load(input), golden);
    }

    // ported from test_database.py::test_dump_and_load_equivalence (dbc rows) and
    // the self-golden assert_dbc_dump usages
    [Theory]
    [InlineData("abs.dbc")]
    [InlineData("attributes.dbc")]
    [InlineData("choices.dbc")]
    [InlineData("emc32.dbc")]
    [InlineData("floating_point.dbc")]
    [InlineData("j1939.dbc")]
    [InlineData("long_names.dbc")]
    [InlineData("motohawk.dbc")]
    [InlineData("multiple_senders.dbc")]
    [InlineData("multiplex.dbc")]
    [InlineData("sig_groups.dbc")]
    [InlineData("socialledge.dbc")]
    [InlineData("timing.dbc")]
    [InlineData("val_table.dbc")]
    [InlineData("vehicle.dbc")]
    public void Dump_and_reload_is_equivalent(string file)
    {
        AssertDump(Load(file), file);
    }

    // ported from test_database.py::test_database_version
    [Fact]
    public void Version_is_written_first()
    {
        var db = new Database();
        Assert.Null(db.Version);
        Assert.StartsWith("VERSION \"\"", db.ToDbcString());

        db.Version = "my_version";
        Assert.StartsWith("VERSION \"my_version\"", db.ToDbcString());
    }

    // ported from test_database.py::test_issue_163_dbc_newlines
    [Fact]
    public void Output_uses_crlf_line_endings_only()
    {
        var text = Load("issue_163_newline.dbc").ToDbcString();

        Assert.Contains("\r\n", text);
        Assert.DoesNotContain("\r\r", text);
        Assert.DoesNotMatch("[^\r]\n", text.Replace("\r\n", "|"));
        Assert.DoesNotContain('\r', text.Replace("\r\n", "|"));
        Assert.DoesNotContain('\n', text.Replace("\r\n", "|"));
    }

    // ported from test_database.py::test_string_attribute_definition_dump
    [Fact]
    public void String_attribute_definitions_survive_the_round_trip()
    {
        var reloaded = DbcReader.LoadString(Load("test_multiplex_dump.dbc").ToDbcString());

        Assert.Equal("STRING", reloaded.Dbc!.AttributeDefinitions["BusType"].TypeName);
    }

    // ported from test_database.py::test_extended_id_dump
    [Fact]
    public void Extended_frame_flags_survive_the_round_trip()
    {
        var reloaded = DbcReader.LoadString(Load("test_extended_id_dump.dbc").ToDbcString());

        Assert.False(reloaded.GetMessageByFrameId(0x100).IsExtendedFrame);
        Assert.True(reloaded.GetMessageByFrameId(0x1c2a2a2a).IsExtendedFrame);
    }

    // ported from test_database.py::test_multiplex_dump
    [Fact]
    public void Multiplexer_indicators_survive_the_round_trip()
    {
        var reloaded = DbcReader.LoadString(Load("test_multiplex_dump.dbc").ToDbcString());
        var message = reloaded.GetMessageByFrameId(0x100);

        Assert.True(message.Signals[0].IsMultiplexer);
        Assert.Null(message.Signals[0].MultiplexerIds);
        Assert.Equal("MultiplexedSig", message.Signals[1].Name);
        Assert.False(message.Signals[1].IsMultiplexer);
        Assert.Equal(0x2a, message.Signals[1].MultiplexerIds![0]);
        Assert.Equal("UnmultiplexedSig", message.Signals[2].Name);
        Assert.False(message.Signals[2].IsMultiplexer);
        Assert.Null(message.Signals[2].MultiplexerIds);
    }

    // ported from test_database.py::test_missing_dbc_specifics
    [Fact]
    public void Databases_without_dbc_specifics_can_be_dumped()
    {
        var db = new Database(
            messages:
            [
                new Message(0x20, "D", 8, [new Signal("a", 0, 8)]),
            ],
            nodes: [new Node("example", comment: "example node")]);

        Assert.NotEmpty(db.ToDbcString());
    }

    // ported from test_database.py::test_long_names_converter
    [Fact]
    public void Long_names_are_truncated_and_suffixed_on_collision()
    {
        var prefix = new string('S', 27);
        var converter = new LongNamesConverter(
            [prefix + "XLLLLA", prefix + "XLLLLB", prefix + "YLLLLA", prefix + "YLLLLB"]);

        Assert.Equal(prefix + "XLLLL", converter.Shorten(prefix + "XLLLLA"));
        Assert.Equal(prefix + "_0000", converter.Shorten(prefix + "XLLLLB"));
        Assert.Equal(prefix + "YLLLL", converter.Shorten(prefix + "YLLLLA"));
        Assert.Equal(prefix + "_0001", converter.Shorten(prefix + "YLLLLB"));
    }

    // ported from test_database.py::test_dbc_gensigstartval_from_raw_initial
    [Fact]
    public void Raw_initial_values_are_written_as_GenSigStartValue()
    {
        var db = new Database(
            messages: [new Message(0x42, "m", 8, [new Signal("s", 0, 8, rawInitial: 47)])]);

        var reloaded = DbcReader.LoadString(db.ToDbcString());

        Assert.True(reloaded.GetMessageByName("m").GetSignalByName("s").RawInitial == 47);
    }

    // ported from test_database.py::test_dbc_shorten_long_names
    [Fact]
    public void Long_name_shortening_can_be_disabled()
    {
        var db = Load("long_names.dbc");

        Assert.Contains("BA_ \"SystemSignalLongSymbol\"", db.ToDbcString());
        Assert.DoesNotContain(
            "BA_ \"SystemSignalLongSymbol\"",
            db.ToDbcString(shortenLongNames: false));
    }

    // ported from test_database.py::test_dbc_remove_special_chars
    [Fact]
    public void Special_characters_are_sanitized_at_dump()
    {
        var db = new Database(
            messages:
            [
                new Message(1, "Invalid Message Name", 8,
                    [new Signal("Special Chars=\"$%&/'!?^", 0, 8)],
                    senders: ["Invalid Node Name"]),
                new Message(2, "([{Brackets}])", 8, [new Signal("13StartsWithNumber", 0, 8)]),
            ],
            nodes: [new Node("Invalid Node Name")]);

        var reloaded = DbcReader.LoadString(db.ToDbcString());

        Assert.Equal("Invalid_Node_Name", reloaded.Nodes[0].Name);
        Assert.Equal("Invalid_Message_Name", reloaded.Messages[0].Name);
        Assert.Equal(["Invalid_Node_Name"], reloaded.Messages[0].Senders);
        Assert.Equal("Special_Chars__________", reloaded.Messages[0].Signals[0].Name);
        Assert.Equal("___Brackets___", reloaded.Messages[1].Name);
        Assert.Equal("_13StartsWithNumber", reloaded.Messages[1].Signals[0].Name);
    }
}
