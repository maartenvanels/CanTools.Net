namespace CanTools.Tests;

// Ported from the j1939 utility tests in tests/test_database.py.
public class J1939Tests
{
    // ported from test_database.py::test_j1939_frame_id_pack_unpack
    [Theory]
    [InlineData(0x7, 0x1, 0x1, 0xff, 0xff, 0xff, 0x1fffffffu)]
    [InlineData(0x5, 0x0, 0x0, 0x12, 0x34, 0x56, 0x14123456u)]
    public void Frame_ids_pack_and_unpack(
        int priority, int reserved, int dataPage,
        int pduFormat, int pduSpecific, int sourceAddress, uint packed)
    {
        Assert.Equal(packed, J1939.FrameIdPack(
            priority, reserved, dataPage, pduFormat, pduSpecific, sourceAddress));
        Assert.Equal(
            new J1939FrameId(priority, reserved, dataPage, pduFormat, pduSpecific, sourceAddress),
            J1939.FrameIdUnpack(packed));
    }

    // ported from test_database.py::test_j1939_frame_id_pack_bad_data
    [Theory]
    [InlineData(8, 0, 0, 0, 0, 0, "Expected priority 0..7, but got 8.")]
    [InlineData(0, 2, 0, 0, 0, 0, "Expected reserved 0..1, but got 2.")]
    [InlineData(0, 0, 2, 0, 0, 0, "Expected data page 0..1, but got 2.")]
    [InlineData(0, 0, 0, 0x100, 0, 0, "Expected PDU format 0..255, but got 256.")]
    [InlineData(0, 0, 0, 0, 0x100, 0, "Expected PDU specific 0..255, but got 256.")]
    [InlineData(0, 0, 0, 0, 0, 256, "Expected source address 0..255, but got 256.")]
    public void Out_of_range_frame_id_fields_throw(
        int priority, int reserved, int dataPage,
        int pduFormat, int pduSpecific, int sourceAddress, string message)
    {
        var error = Assert.Throws<CanToolsException>(() => J1939.FrameIdPack(
            priority, reserved, dataPage, pduFormat, pduSpecific, sourceAddress));
        Assert.Equal(message, error.Message);
    }

    // ported from test_database.py::test_j1939_frame_id_unpack_bad_data
    [Fact]
    public void Too_wide_frame_ids_throw_at_unpack()
    {
        var error = Assert.Throws<CanToolsException>(() => J1939.FrameIdUnpack(0x100000000));
        Assert.Equal("Expected a frame id 0..0x1fffffff, but got 0x100000000.", error.Message);
    }

    // ported from test_database.py::test_j1939_pgn_pack_unpack
    [Theory]
    [InlineData(1, 1, 0xff, 0xff, 0x3ffffu)]
    [InlineData(0, 1, 0xef, 0, 0x1ef00u)]
    [InlineData(0, 0, 0xf0, 0x34, 0xf034u)]
    public void Pgns_pack_and_unpack(
        int reserved, int dataPage, int pduFormat, int pduSpecific, uint packed)
    {
        Assert.Equal(packed, J1939.PgnPack(reserved, dataPage, pduFormat, pduSpecific));
        Assert.Equal(
            new J1939Pgn(reserved, dataPage, pduFormat, pduSpecific),
            J1939.PgnUnpack(packed));
    }

    // ported from test_database.py::test_j1939_pgn_pack_bad_data
    [Theory]
    [InlineData(2, 0, 0, 0, "Expected reserved 0..1, but got 2.")]
    [InlineData(0, 2, 0, 0, "Expected data page 0..1, but got 2.")]
    [InlineData(0, 0, 0x100, 0, "Expected PDU format 0..255, but got 256.")]
    [InlineData(0, 0, 0xf0, 0x100, "Expected PDU specific 0..255, but got 256.")]
    [InlineData(0, 0, 0xef, 1, "Expected PDU specific 0 when PDU format is 0..239, but got 1.")]
    public void Out_of_range_pgn_fields_throw(
        int reserved, int dataPage, int pduFormat, int pduSpecific, string message)
    {
        var error = Assert.Throws<CanToolsException>(
            () => J1939.PgnPack(reserved, dataPage, pduFormat, pduSpecific));
        Assert.Equal(message, error.Message);
    }

    // ported from test_database.py::test_j1939_pgn_unpack_bad_data
    [Fact]
    public void Too_wide_pgns_throw_at_unpack()
    {
        var error = Assert.Throws<CanToolsException>(() => J1939.PgnUnpack(0x40000));
        Assert.Equal("Expected a parameter group number 0..0x3ffff, but got 0x40000.", error.Message);
    }
}
