using CanTools.CanOpen;
using CanTools.Formats.Eds;

namespace CanTools.Tests;

public class DcfWriterTests
{
    private const string Eds =
        "[FileInfo]\n" +
        "FileName=device.eds\n" +
        "\n" +
        "[DeviceInfo]\n" +
        "VendorName=Acme\n" +
        "\n" +
        "[1000]\n" +
        "ParameterName=Device type\n" +
        "DataType=0x0007\n" +
        "AccessType=ro\n" +
        "; keep this comment\n" +
        "\n" +
        "[1017]\n" +
        "ParameterName=Producer heartbeat time\n" +
        "DataType=0x0006\n" +
        "AccessType=rw\n" +
        "\n" +
        "[1400]\n" +
        "ParameterName=RPDO communication\n" +
        "ObjectType=0x9\n" +
        "SubNumber=2\n" +
        "\n" +
        "[1400sub1]\n" +
        "ParameterName=COB-ID\n" +
        "DataType=0x0007\n" +
        "AccessType=rw\n" +
        "DefaultValue=$NODEID+512\n";

    [Fact]
    public void Writes_commissioning_and_a_configured_value_read_back_by_the_reader()
    {
        var od = EdsReader.LoadString(Eds, nodeId: 0x0A);
        od.NodeId = 0x0A;
        od.Bitrate = 500_000;
        od.SetValue("Producer heartbeat time", (OdValue)1000UL);

        var dcf = DcfWriter.WriteString(od);
        var reread = EdsReader.LoadString(dcf);

        Assert.Equal(0x0A, reread.NodeId);
        Assert.Equal(500_000, reread.Bitrate);
        Assert.Equal((OdValue)1000UL, reread.GetVariable(0x1017)!.Value);
    }

    [Fact]
    public void Preserves_comments_and_untouched_relative_values_verbatim()
    {
        var od = EdsReader.LoadString(Eds, nodeId: 0x0A);
        od.SetValue("Producer heartbeat time", (OdValue)1000UL);

        var dcf = DcfWriter.WriteString(od);

        Assert.Contains("; keep this comment", dcf);
        Assert.Contains("VendorName=Acme", dcf);
        // The 0x1400sub1 DefaultValue was read as a $NODEID expression; it must NOT
        // reappear as a ParameterValue with the node id baked in — we did not set it.
        Assert.DoesNotContain("ParameterValue", dcf.Split("[1400sub1]")[1]);
    }

    [Fact]
    public void Writes_a_subindex_parameter_value()
    {
        var od = EdsReader.LoadString(Eds, nodeId: 0x0A);
        od.SetValue(0x1400, 1, (OdValue)0x40AUL);   // a record member, by index/subindex

        var dcf = DcfWriter.WriteString(od);
        var reread = EdsReader.LoadString(dcf, nodeId: 0x0A);

        Assert.Equal((OdValue)0x40AUL, reread.GetVariable(0x1400, 1)!.Value);
    }

    [Fact]
    public void Node_id_format_is_hex_by_default_and_decimal_on_request()
    {
        var od = EdsReader.LoadString(Eds);
        od.NodeId = 0x0A;

        Assert.Contains("NodeID=0x0A", DcfWriter.WriteString(od));
        Assert.Contains(
            "NodeID=10",
            DcfWriter.WriteString(od, new DcfWriterOptions { NodeIdFormat = NodeIdFormat.Decimal }));
    }

    [Fact]
    public void An_unchanged_dictionary_round_trips_verbatim()
    {
        var od = EdsReader.LoadString(Eds);

        Assert.Equal(Eds, DcfWriter.WriteString(od));
    }

    [Fact]
    public void Writing_a_dictionary_without_a_source_throws()
    {
        var od = new ObjectDictionary();

        Assert.Throws<InvalidOperationException>(() => DcfWriter.WriteString(od));
    }

    [Fact]
    public void Setting_an_unknown_entry_throws()
    {
        var od = EdsReader.LoadString(Eds);

        Assert.Throws<KeyNotFoundException>(() => od.SetValue("No such parameter", (OdValue)1UL));
    }
}
