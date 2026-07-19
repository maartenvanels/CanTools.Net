using CanTools.Transport;
using CanTools.CanOpen;
using CanTools.Tests.Transport;

namespace CanTools.Tests.CanOpen;

// Segmented SDO vectors. Learned from lely-core's SDO tests (Apache 2.0; mirrored,
// not copied) and cross-checked against CiA 301. Payload "1234567890" (0x31..0x30)
// is 10 bytes: segment 1 carries 7 bytes, segment 2 the last 3.
public class SdoClientSegmentedTests
{
    private const byte NodeId = 0x0A;

    [Fact]
    public async Task Upload_reassembles_two_segments()
    {
        var channel = new InMemoryCanChannel();
        channel.Enqueue(
            // initiate upload response, segmented, size = 10
            CanFrame.Classic(0x58A, Convert.FromHexString("410010000A000000")),
            // segment 1 (toggle 0, 7 bytes, not last): cmd 0x00
            CanFrame.Classic(0x58A, Convert.FromHexString("0031323334353637")),
            // segment 2 (toggle 1, 3 bytes, last): cmd 0x19 = 0x10|((7-3)<<1)|0x01
            CanFrame.Classic(0x58A, Convert.FromHexString("1938393000000000")));
        var client = new SdoClient(channel, NodeId);

        var value = await client.UploadAsync(0x1000, 0);

        Assert.Equal(Convert.FromHexString("31323334353637383930"), value);
        // requests: initiate (0x40), segment req toggle 0 (0x60), segment req toggle 1 (0x70)
        Assert.Equal(0x40, channel.Sent[0].Data[0]);
        Assert.Equal(0x60, channel.Sent[1].Data[0]);
        Assert.Equal(0x70, channel.Sent[2].Data[0]);
    }

    [Fact]
    public async Task Download_sends_initiate_then_segments()
    {
        var channel = new InMemoryCanChannel();
        channel.Enqueue(
            CanFrame.Classic(0x58A, Convert.FromHexString("6000200000000000")),   // initiate ack
            CanFrame.Classic(0x58A, Convert.FromHexString("2000000000000000")),   // segment ack toggle 0
            CanFrame.Classic(0x58A, Convert.FromHexString("3000000000000000")));  // segment ack toggle 1
        var client = new SdoClient(channel, NodeId);

        await client.DownloadAsync(0x2000, 0, Convert.FromHexString("31323334353637383930"));

        // initiate download request: segmented (size specified), size = 10
        Assert.Equal(0x21, channel.Sent[0].Data[0]);   // 0x20 | 0x01 (size)
        Assert.Equal(0x0A, channel.Sent[0].Data[4]);
        // segment 1: toggle 0, 7 bytes, not last -> 0x00
        Assert.Equal(0x00, channel.Sent[1].Data[0]);
        // segment 2: toggle 1, 3 bytes, last -> 0x19 = 0x10 | ((7-3)<<1) | 0x01
        Assert.Equal(0x19, channel.Sent[2].Data[0]);
    }
}
