using CanTools.Transport;
using CanTools.CanOpen;
using CanTools.Tests.Transport;

namespace CanTools.Tests.CanOpen;

// Block SDO vectors. Learned from lely-core's block SDO tests (Apache 2.0;
// mirrored, not copied) and cross-checked against CiA 301. Payload is 10 bytes
// "1234567890" sent as one block of two segments.
public class SdoClientBlockTests
{
    private const byte NodeId = 0x0A;

    [Fact]
    public async Task Block_download_sends_segments_and_end()
    {
        var channel = new InMemoryCanChannel();
        channel.Enqueue(
            // server block-download initiate ack, blksize=127 in byte 4
            CanFrame.Classic(0x58A, Convert.FromHexString("A00020007F000000")),
            // server segment ack: ackseq=2 (byte1), blksize=127 (byte2)
            CanFrame.Classic(0x58A, Convert.FromHexString("A2027F0000000000")),
            // server end ack
            CanFrame.Classic(0x58A, Convert.FromHexString("A100000000000000")));
        var client = new SdoClient(channel, NodeId, new SdoClientOptions { EnableBlockTransfer = true });

        await client.DownloadAsync(0x2000, 0, Convert.FromHexString("31323334353637383930"));

        // initiate: 0xC0 | s(0x02) = 0xC2, size = 10 in bytes 4..7
        Assert.Equal(0xC2, channel.Sent[0].Data[0]);
        Assert.Equal(0x0A, channel.Sent[0].Data[4]);
        // segment 1: seq 1, not last
        Assert.Equal(0x01, channel.Sent[1].Data[0]);
        Assert.Equal(Convert.FromHexString("31323334353637"), channel.Sent[1].Data[1..8]);
        // segment 2: seq 2, last (bit 7 set) -> 0x82
        Assert.Equal(0x82, channel.Sent[2].Data[0]);
        // end: 0xC0 | (pad<<2) | 0x01; 3 bytes in last segment -> pad 4 -> 0xC0|0x10|0x01 = 0xD1
        Assert.Equal(0xD1, channel.Sent[3].Data[0]);
    }

    [Fact]
    public async Task Block_upload_reassembles_via_the_reassembler()
    {
        var channel = new InMemoryCanChannel();
        channel.Enqueue(
            // server block-upload initiate response: 0xC0 | s(0x02), index/sub, size 10
            CanFrame.Classic(0x58A, Convert.FromHexString("C20020000A000000")),
            // data segment 1 (seq 1)
            CanFrame.Classic(0x58A, Convert.FromHexString("0131323334353637")),
            // data segment 2 (seq 2, last) - 3 bytes + padding
            CanFrame.Classic(0x58A, Convert.FromHexString("8238393000000000")),
            // server block-upload end: 0xC0 | (pad 4 << 2) | 0x01 = 0xD1
            CanFrame.Classic(0x58A, Convert.FromHexString("D100000000000000")));
        var client = new SdoClient(channel, NodeId, new SdoClientOptions { EnableBlockTransfer = true });

        var value = await client.UploadAsync(0x2000, 0);

        Assert.Equal(Convert.FromHexString("31323334353637383930"), value);
        // client initiate: block upload request 0xA0
        Assert.Equal(0xA0, channel.Sent[0].Data[0]);
        // client start: 0xA3
        Assert.Equal(0xA3, channel.Sent[1].Data[0]);
        // client segment ack after last: 0xA2, ackseq 2
        Assert.Equal(0xA2, channel.Sent[2].Data[0]);
        Assert.Equal(0x02, channel.Sent[2].Data[1]);
        // client end ack: 0xA1
        Assert.Equal(0xA1, channel.Sent[3].Data[0]);
    }

    [Fact]
    public async Task Block_download_falls_back_to_segmented_on_initiate_abort()
    {
        var channel = new InMemoryCanChannel();
        channel.Enqueue(
            // server aborts the block initiate (block not supported)
            CanFrame.Classic(0x58A, Convert.FromHexString("8000200001000405")),
            // then the segmented fallback succeeds: initiate ack + two segment acks
            CanFrame.Classic(0x58A, Convert.FromHexString("6000200000000000")),
            CanFrame.Classic(0x58A, Convert.FromHexString("2000000000000000")),
            CanFrame.Classic(0x58A, Convert.FromHexString("3000000000000000")));
        var client = new SdoClient(channel, NodeId, new SdoClientOptions { EnableBlockTransfer = true });

        await client.DownloadAsync(0x2000, 0, Convert.FromHexString("31323334353637383930"));

        Assert.Equal(0xC2, channel.Sent[0].Data[0]);   // tried block first
        Assert.Equal(0x21, channel.Sent[1].Data[0]);   // fell back to segmented initiate
    }
}
