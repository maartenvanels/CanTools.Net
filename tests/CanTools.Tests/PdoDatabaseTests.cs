using CanTools.CanOpen;
using CanTools.Formats.Eds;
using CanTools.Model;

namespace CanTools.Tests;

// The PDO projection follows python-canopen's read-from-OD semantics; the TPDO1 bit
// layout replicates test_pdo.py::test_pdo_map_bit_mapping through the projection.
public class PdoDatabaseTests
{
    private static Database Load(string name, int? nodeId = null) =>
        PdoDatabase.Create(EdsReader.LoadFile(TestFiles.Eds(name), nodeId));

    private static Dictionary<string, SignalValue> Values(params (string Name, SignalValue Value)[] entries) =>
        entries.ToDictionary(e => e.Name, e => e.Value);

    // replicates python-canopen test_pdo.py::test_pdo_map_bit_mapping
    [Fact]
    public void Mapped_signals_pack_like_python_canopen()
    {
        var db = Load("pdo_test.eds");
        var tpdo1 = db.GetMessageByFrameId(0x185);

        Assert.Equal("TxPDO1_node5", tpdo1.Name);
        Assert.Equal(8, tpdo1.Length);   // 58 mapped bits
        Assert.Equal(
            ["INTEGER16 value", "UNSIGNED8 value", "INTEGER8 value",
             "INTEGER32 value", "BOOLEAN value", "BOOLEAN value 2"],
            tpdo1.Signals.Select(s => s.Name));

        var encoded = tpdo1.Encode(Values(
            ("INTEGER16 value", -3),
            ("UNSIGNED8 value", 0xF),
            ("INTEGER8 value", -2),
            ("INTEGER32 value", 0x01020304),
            ("BOOLEAN value", 0),
            ("BOOLEAN value 2", 1)));

        Assert.Equal(new byte[] { 0xfd, 0xff, 0xef, 0x04, 0x03, 0x02, 0x01, 0x02 }, encoded);

        var decoded = tpdo1.Decode(encoded);
        Assert.True(decoded["INTEGER16 value"] == -3);
        Assert.True(decoded["UNSIGNED8 value"] == 0xF);
        Assert.True(decoded["INTEGER8 value"] == -2);
        Assert.True(decoded["INTEGER32 value"] == 0x01020304);
        Assert.True(decoded["BOOLEAN value"] == 0);
        Assert.True(decoded["BOOLEAN value 2"] == 1);
    }

    // upstream semantics: dummies map as ordinary variables, record members get
    // dotted names, and missing targets are skipped without advancing the offset
    [Fact]
    public void Dummies_records_and_missing_targets_follow_upstream()
    {
        var db = Load("pdo_test.eds");
        var tpdo2 = db.GetMessageByFrameId(0x285);

        Assert.Equal("TxPDO2_node5", tpdo2.Name);
        Assert.Equal(5, tpdo2.Length);   // 40 bits; the missing 0x7FFF entry is dropped

        var statusword = tpdo2.GetSignalByName("Statusword");
        Assert.Equal(0, statusword.StartBit);
        Assert.Equal(16, statusword.Length);
        Assert.False(statusword.IsSigned);

        var padding = tpdo2.GetSignalByName("Dummy0003");
        Assert.Equal(16, padding.StartBit);
        Assert.Equal(16, padding.Length);
        Assert.True(padding.IsSigned);   // INTEGER16

        var temperature = tpdo2.GetSignalByName("Motor Status.Temperature");
        Assert.Equal(32, temperature.StartBit);
        Assert.Equal(8, temperature.Length);
        Assert.True(temperature.IsSigned);
    }

    [Fact]
    public void Disabled_pdos_are_skipped()
    {
        var db = Load("pdo_test.eds");

        Assert.False(db.TryGetMessageByFrameId(0x385, out _));
        Assert.Equal(3, db.Messages.Count);   // RPDO1, TPDO1, TPDO2
    }

    [Fact]
    public void Receive_pdos_project_with_units()
    {
        var rpdo1 = Load("pdo_test.eds").GetMessageByFrameId(0x205);

        Assert.Equal("RxPDO1_node5", rpdo1.Name);
        Assert.Equal(6, rpdo1.Length);

        var position = rpdo1.GetSignalByName("Position actual value");
        Assert.Equal(16, position.StartBit);
        Assert.Equal(32, position.Length);
        Assert.True(position.IsSigned);
        Assert.Equal("counts", position.Unit);
    }

    [Fact]
    public void An_explicit_node_id_moves_the_cob_ids()
    {
        var db = Load("pdo_test.eds", nodeId: 0x21);

        Assert.Equal("TxPDO1_node33", db.GetMessageByFrameId(0x1A1).Name);
    }

    // sample.eds maps CiA 402 objects that the file itself does not define;
    // python-canopen then yields PDOs without any variables.
    [Fact]
    public void Sample_eds_yields_the_eight_empty_pdos()
    {
        var db = PdoDatabase.Create(
            EdsReader.LoadFile(TestFiles.Eds("sample.eds")));   // NodeID=0x10

        Assert.Equal(8, db.Messages.Count);
        Assert.All(db.Messages, message => Assert.Empty(message.Signals));
        Assert.Equal(
            new uint[] { 0x190, 0x210, 0x290, 0x310, 0x390, 0x410, 0x490, 0x510 },
            db.Messages.Select(m => m.FrameId).Order());
        Assert.Equal("RxPDO1_node16", db.GetMessageByFrameId(0x210).Name);
        Assert.Equal("TxPDO1_node16", db.GetMessageByFrameId(0x190).Name);
    }
}

// The CiA 301 predefined connection set.
public class CobIdTests
{
    [Theory]
    [InlineData(0x000u, CanOpenFunction.Nmt, 0)]
    [InlineData(0x080u, CanOpenFunction.Sync, 0)]
    [InlineData(0x085u, CanOpenFunction.Emergency, 5)]
    [InlineData(0x100u, CanOpenFunction.Time, 0)]
    [InlineData(0x185u, CanOpenFunction.Tpdo1, 5)]
    [InlineData(0x205u, CanOpenFunction.Rpdo1, 5)]
    [InlineData(0x285u, CanOpenFunction.Tpdo2, 5)]
    [InlineData(0x305u, CanOpenFunction.Rpdo2, 5)]
    [InlineData(0x385u, CanOpenFunction.Tpdo3, 5)]
    [InlineData(0x405u, CanOpenFunction.Rpdo3, 5)]
    [InlineData(0x485u, CanOpenFunction.Tpdo4, 5)]
    [InlineData(0x505u, CanOpenFunction.Rpdo4, 5)]
    [InlineData(0x585u, CanOpenFunction.SdoTransmit, 5)]
    [InlineData(0x605u, CanOpenFunction.SdoReceive, 5)]
    [InlineData(0x705u, CanOpenFunction.Heartbeat, 5)]
    [InlineData(0x7E4u, CanOpenFunction.LssTransmit, 100)]
    [InlineData(0x7E5u, CanOpenFunction.LssReceive, 101)]
    public void Cob_ids_classify_and_compose(uint raw, CanOpenFunction function, int nodeId)
    {
        var cobId = new CobId(raw);

        Assert.Equal(function, cobId.Function);

        if (function is not (CanOpenFunction.LssTransmit or CanOpenFunction.LssReceive))
        {
            Assert.Equal(nodeId, cobId.NodeId);
            Assert.Equal(raw, CobId.For(function, nodeId).Raw);
        }
    }

    [Fact]
    public void Unassigned_ids_classify_as_unknown()
    {
        Assert.Equal(CanOpenFunction.Unknown, new CobId(0x005).Function);   // NMT with node bits
        Assert.Equal(CanOpenFunction.Unknown, new CobId(0x180).Function);   // TPDO1 without node
        Assert.Equal(CanOpenFunction.Unknown, new CobId(0x680).Function);   // reserved block
    }
}
