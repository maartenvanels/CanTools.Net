using CanTools.CanOpen;
using CanTools.Formats.Eds;

namespace CanTools.Tests;

// Ported from python-canopen's test/test_eds.py (MIT). Warning-log assertions are
// ported as "the invalid field stays unset".
public class EdsReaderTests
{
    private static ObjectDictionary Sample(int? nodeId = 2) =>
        EdsReader.LoadFile(Path.Combine(TestFiles.Eds("sample.eds")), nodeId);

    // ported from test_eds.py::test_load_file_object / test_load_implicit_nodeid
    [Fact]
    public void Node_id_comes_from_the_file_unless_given()
    {
        Assert.True(Sample().Entries.Count > 0);

        Assert.Equal(16, Sample(nodeId: null).NodeId);   // NodeID=0x10 in the file
        Assert.Equal(3, Sample(nodeId: 3).NodeId);       // explicit wins
        Assert.Null(EdsReader.LoadFile(TestFiles.Eds("datatypes.eds")).NodeId);
    }

    // ported from test_eds.py::test_load_baudrate
    [Fact]
    public void Bitrate_comes_from_device_commissioning()
    {
        Assert.Equal(500_000, Sample().Bitrate);
        Assert.Null(EdsReader.LoadFile(TestFiles.Eds("datatypes.eds")).Bitrate);
    }

    // ported from test_eds.py::test_variable
    [Fact]
    public void Plain_variables_load()
    {
        var variable = Assert.IsType<OdVariable>(Sample()["Producer heartbeat time"]);

        Assert.Equal(0x1017, variable.Index);
        Assert.Equal(0, variable.Subindex);
        Assert.Equal("Producer heartbeat time", variable.Name);
        Assert.Equal(CanOpenDataType.Unsigned16, variable.DataType);
        Assert.Equal("rw", variable.AccessType);
        Assert.False(variable.IsDomain);
        Assert.True(variable.Default == 0);
        Assert.False(variable.IsRelative);
    }

    // ported from test_eds.py::test_relative_variable
    [Fact]
    public void Node_id_expressions_are_evaluated()
    {
        var od = Sample();
        var record = Assert.IsType<OdRecord>(od["Receive PDO 0 Communication Parameter"]);
        var cobId = record["COB-ID use by RPDO 1"];

        Assert.True(cobId.IsRelative);
        Assert.True(cobId.Default == 512 + od.NodeId!.Value);
    }

    // ported from test_eds.py::test_record
    [Fact]
    public void Records_load_their_members()
    {
        var record = Assert.IsType<OdRecord>(Sample()["Identity object"]);

        Assert.Equal(0x1018, record.Index);
        Assert.Equal("Identity object", record.Name);
        Assert.Equal(4, record.Members.Count);   // sub3 is commented out in the file

        var vendorId = record["Vendor-ID"];
        Assert.Equal("Vendor-ID", vendorId.Name);
        Assert.Equal(0x1018, vendorId.Index);
        Assert.Equal(1, vendorId.Subindex);
        Assert.Equal(CanOpenDataType.Unsigned32, vendorId.DataType);
        Assert.Equal("ro", vendorId.AccessType);
        Assert.False(vendorId.IsDomain);
    }

    // ported from test_eds.py::test_record_with_limits
    [Theory]
    [InlineData(0x3020, 0, 127)]
    [InlineData(0x3021, 2, 10)]
    [InlineData(0x3022, 100, 1000)]
    [InlineData(0x3023, -100, 100)]
    [InlineData(0x3030, -2147483648, -1)]
    [InlineData(0x3031, -1, 0)]
    [InlineData(0x3032, -1, 0)]
    [InlineData(0x3033, -1, 0)]
    [InlineData(0x3034, -1, 0)]
    [InlineData(0x3040, -10, 10)]
    public void Limits_reinterpret_signed_hex(int index, long minimum, long maximum)
    {
        var variable = Assert.IsType<OdVariable>(Sample()[index]);

        Assert.Equal(minimum, variable.Minimum);
        Assert.Equal(maximum, variable.Maximum);
    }

    // ported from test_eds.py::test_signed_int_from_hex and friends
    [Theory]
    [InlineData("0x7F", 8, 127)]
    [InlineData("0x80", 8, -128)]
    [InlineData("0xFF", 8, -1)]
    [InlineData("0x00", 8, 0)]
    [InlineData("0x7FFF", 16, 32767)]
    [InlineData("0x8000", 16, -32768)]
    [InlineData("0xFFFF", 16, -1)]
    [InlineData("0x7FFFFFFFFFFFFFFF", 64, long.MaxValue)]
    [InlineData("0x8000000000000000", 64, long.MinValue)]
    [InlineData("0xFFFFFFFFFFFFFFFF", 64, -1)]
    [InlineData("-1", 8, -1)]
    [InlineData("-128", 8, -128)]
    [InlineData("-2147483648", 32, -2147483648)]
    public void Signed_values_reinterpret_twos_complement(string text, int bits, long expected)
    {
        Assert.Equal(expected, EdsReader.SignedFromEds(text, bits));
    }

    // ported from test_eds.py::test_signed_int_from_hex_rejects_out_of_range
    [Theory]
    [InlineData("0xFFFF", 8)]
    [InlineData("-129", 8)]
    public void Out_of_range_signed_values_throw(string text, int bits)
    {
        Assert.Throws<OverflowException>(() => EdsReader.SignedFromEds(text, bits));
    }

    // ported from test_eds.py::test_build_variable_range_warnings (observable side)
    [Fact]
    public void Invalid_limits_and_defaults_are_skipped()
    {
        var od = EdsReader.LoadString("""
            [2003]
            ParameterName=INTEGER16 value
            ObjectType=0x7
            DataType=0x0003
            AccessType=rw
            LowLimit=-65535
            HighLimit=0x10000
            DefaultValue=SOMETHING
            """);

        var variable = Assert.IsType<OdVariable>(od[0x2003]);
        Assert.Null(variable.Minimum);
        Assert.Null(variable.Maximum);
        Assert.Null(variable.Default);
        Assert.Equal("SOMETHING", variable.DefaultRaw);
    }

    // ported from test_eds.py::test_array_compact_subobj (explicit array subs)
    [Fact]
    public void Arrays_synthesize_missing_members_from_the_template()
    {
        var array = Assert.IsType<OdArray>(Sample()[0x1003]);
        Assert.Equal("Pre-defined error field", array.Name);

        var explicitMember = array[5];
        Assert.Equal("Pre-defined error field_5", explicitMember.Name);
        Assert.Equal(0x1003, explicitMember.Index);
        Assert.Equal(5, explicitMember.Subindex);
        Assert.Equal(CanOpenDataType.Unsigned32, explicitMember.DataType);
        Assert.Equal("ro", explicitMember.AccessType);

        // Subindex 2 is not in the file and is synthesized from subindex 1.
        var synthesized = array[2];
        Assert.Equal(2, synthesized.Subindex);
        Assert.Equal(CanOpenDataType.Unsigned32, synthesized.DataType);
        Assert.EndsWith("_2", synthesized.Name);
    }

    // ported from test_eds.py::test_explicit_name_subobj
    [Fact]
    public void Name_sections_rename_compact_array_members()
    {
        var array = Assert.IsType<OdArray>(Sample()[0x3004]);

        Assert.Equal("Sensor Status", array.Name);
        Assert.Equal("Sensor Status 1", array[1].Name);
        Assert.Equal("Sensor Status 3", array[3].Name);
        Assert.True(array[3].Default == 3);
    }

    // ported from test_eds.py::test_parameter_name_with_percent
    [Fact]
    public void Percent_signs_are_literal()
    {
        Assert.Equal("Valve % open", Sample()[0x3003].Name);
        Assert.Equal("Valve 1 % Open", Sample()[0x3006].Name);
    }

    // ported from test_eds.py::test_sub_index_w_capital_s
    [Fact]
    public void Subindex_sections_accept_a_capital_S()
    {
        var record = Assert.IsType<OdRecord>(Sample()[0x3010]);

        Assert.Equal("Temperature", record[0].Name);
    }

    // ported from test_eds.py::test_dummy_variable
    [Fact]
    public void Enabled_dummies_become_variables()
    {
        var od = Sample();
        var dummy = Assert.IsType<OdVariable>(od["Dummy0003"]);

        Assert.Equal(0x0003, dummy.Index);
        Assert.Equal(0, dummy.Subindex);
        Assert.Equal(CanOpenDataType.Integer16, dummy.DataType);
        Assert.Equal("const", dummy.AccessType);
        Assert.Equal(16, dummy.BitLength);

        Assert.Throws<KeyNotFoundException>(() => od["Dummy0001"]);
    }

    // ported from test_eds.py::test_reading_factor
    [Fact]
    public void Factor_description_and_unit_extensions_load()
    {
        var record = Assert.IsType<OdRecord>(Sample()[0x3050]);

        var withFactor = record[1];
        Assert.Equal(0.1, withFactor.Factor);
        Assert.Equal("This is the a test description", withFactor.Description);
        Assert.Equal("mV", withFactor.Unit);

        // Factor=ERROR falls back to 1; empty description/unit stay empty.
        var withError = record[2];
        Assert.Equal(1, withError.Factor);
        Assert.Equal("", withError.Description);
        Assert.Equal("", withError.Unit);
    }

    // ported from test_eds.py::test_read_domain_object / test_read_domain_subobject
    [Fact]
    public void Domain_object_type_is_tracked_separately_from_data_type()
    {
        var od = Sample();

        var domainObject = Assert.IsType<OdVariable>(od[0x3063]);
        Assert.Equal("DOMAIN object", domainObject.Name);
        Assert.Equal(CanOpenDataType.Unsigned32, domainObject.DataType);
        Assert.Equal("rw", domainObject.AccessType);
        Assert.True(domainObject.IsDomain);

        var domainMember = Assert.IsType<OdRecord>(od[0x3064])[1];
        Assert.Equal("DOMAIN sub-object", domainMember.Name);
        Assert.Equal(CanOpenDataType.Unsigned32, domainMember.DataType);
        Assert.True(domainMember.IsDomain);

        Assert.False(Assert.IsType<OdVariable>(od["Producer heartbeat time"]).IsDomain);
    }

    // ported from test_eds.py::test_reading_custom_options and friends
    [Fact]
    public void Non_standard_options_are_collected()
    {
        var od = Sample();

        var variable = od[0x3061];
        Assert.Equal(2, variable.CustomOptions.Count);
        Assert.Equal("Motor", variable.CustomOptions["Category"]);
        Assert.Equal("100", variable.CustomOptions["Offset"]);

        Assert.Empty(od["Producer heartbeat time"].CustomOptions);

        var record = Assert.IsType<OdRecord>(od[0x3062]);
        Assert.Equal("vendor_specific", record.CustomOptions["RecordTag"]);
        Assert.Single(record.CustomOptions);
        Assert.Empty(record[1].CustomOptions);
    }

    // ported from test_eds.py::test_comments
    [Fact]
    public void Comment_lines_are_joined()
    {
        Assert.Equal("|-------------|\n| Don't panic |\n|-------------|", Sample().Comments);
    }

    // ported from test_eds.py::datatypes.eds default-value coverage
    [Fact]
    public void Defaults_decode_per_data_type()
    {
        var od = EdsReader.LoadFile(TestFiles.Eds("datatypes.eds"));

        OdVariable Variable(int index) => Assert.IsType<OdVariable>(od[index]);

        Assert.True(Variable(0x2001).Default == 0);                    // BOOLEAN
        Assert.True(Variable(0x2002).Default == 12);                   // INTEGER8
        Assert.True(Variable(0x2003).Default == 34);                   // INTEGER16
        Assert.True(Variable(0x2008).Default == 1.2);                  // REAL32
        Assert.True(Variable(0x2009).Default == "ABCD");               // VISIBLE_STRING
        Assert.True(Variable(0x200A).Default == new byte[] { 0xAB, 0xCD }); // OCTET_STRING
        Assert.True(Variable(0x2011).Default == 1.6);                  // REAL64
        Assert.True(Variable(0x2010).Default == -1);                   // INTEGER24
        Assert.True(Variable(0x2015).Default == -64);                  // INTEGER64
        Assert.True(Variable(0x201B).Default == 64);                   // UNSIGNED64

        // DOMAIN default "@ABCD" is not valid hex; only the raw text survives.
        Assert.Null(Variable(0x200F).Default);
        Assert.Equal("@ABCD", Variable(0x200F).DefaultRaw);

        Assert.Equal("", od.Comments);   // Lines=0
    }

    // ported from test_eds.py::test_load_implicit_nodeid ($NODEID without a node id)
    [Fact]
    public void Node_id_expressions_without_a_node_id_stay_unresolved()
    {
        var od = EdsReader.LoadString("""
            [2000]
            ParameterName=Some COB-ID
            DataType=0x0007
            AccessType=rw
            DefaultValue=$NODEID+0x180
            """);

        var variable = Assert.IsType<OdVariable>(od[0x2000]);
        Assert.True(variable.IsRelative);
        Assert.Null(variable.Default);
        Assert.Equal("$NODEID+0x180", variable.DefaultRaw);
    }

    // GetVariable resolves plain variables, record members and array members.
    [Fact]
    public void Variables_are_found_by_index_and_subindex()
    {
        var od = Sample();

        Assert.Equal("Producer heartbeat time", od.GetVariable(0x1017)!.Name);
        Assert.Equal("Vendor-ID", od.GetVariable(0x1018, 1)!.Name);
        Assert.Equal(5, od.GetVariable(0x1003, 5)!.Subindex);
        Assert.Null(od.GetVariable(0x9999));
    }
}
