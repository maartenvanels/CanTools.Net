using CanTools.CanOpen;

namespace CanTools.Tests.CanOpen;

// Block-round reassembly semantics, verified against lely-core's block SDO tests
// (Apache 2.0; behaviour mirrored, not copied). See src test_sdo block cases.
public class SdoBlockReassemblerTests
{
    [Fact]
    public void An_acknowledged_round_commits_its_segments_in_order()
    {
        var reassembler = new SdoBlockReassembler();
        reassembler.AddSegment([0x01, 1, 2, 3, 4, 5, 6, 7]); // seq 1
        reassembler.AddSegment([0x82, 8, 9]);                // seq 2, last (bit 7), DLC 3: no trailing padding

        var finished = reassembler.Acknowledge(2);

        Assert.True(finished);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8, 9 }, reassembler.Data);
    }

    [Fact]
    public void A_partially_acknowledged_round_keeps_only_committed_segments()
    {
        var reassembler = new SdoBlockReassembler();
        reassembler.AddSegment([0x01, 1, 2, 3, 4, 5, 6, 7]);
        reassembler.AddSegment([0x02, 8, 9, 0, 0, 0, 0, 0]);

        var finished = reassembler.Acknowledge(1);   // only seq 1 committed

        Assert.False(finished);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7 }, reassembler.Data);
    }

    [Fact]
    public void TrimTail_removes_padding_from_the_last_segment()
    {
        var reassembler = new SdoBlockReassembler();
        reassembler.AddSegment([0x81, 1, 2, 3, 4, 5, 6, 7]);
        reassembler.Acknowledge(1);

        reassembler.TrimTail(3);

        Assert.Equal(new byte[] { 1, 2, 3, 4 }, reassembler.Data);
    }
}
