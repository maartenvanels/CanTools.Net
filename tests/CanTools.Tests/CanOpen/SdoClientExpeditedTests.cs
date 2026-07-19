using CanTools.Transport;
using CanTools.CanOpen;
using CanTools.Tests.Transport;

namespace CanTools.Tests.CanOpen;

// Expedited SDO up/download vectors. Values learned from lely-core's SDO tests
// (Apache 2.0; behaviour mirrored, not copied) and cross-checked against CiA 301.
public class SdoClientExpeditedTests
{
    private const byte NodeId = 0x0A;   // request 0x60A, response 0x58A

    [Fact]
    public async Task Upload_reads_an_expedited_value()
    {
        var channel = new InMemoryCanChannel();
        // server: initiate upload response, expedited, size specified, 4 bytes = 0x04
        channel.Enqueue(CanFrame.Classic(0x58A, Convert.FromHexString("4318100104000000")));
        var client = new SdoClient(channel, NodeId);

        var value = await client.UploadAsync(0x1018, 1);

        Assert.Equal(new byte[] { 0x04, 0x00, 0x00, 0x00 }, value);
        // client sent the initiate upload request 0x40 ...
        Assert.Equal(0x60Au, channel.Sent[0].Id);
        Assert.Equal(Convert.FromHexString("4018100100000000"), channel.Sent[0].Data);
    }

    [Fact]
    public async Task Download_writes_an_expedited_value()
    {
        var channel = new InMemoryCanChannel();
        // server: initiate download response for 0x2000sub0
        channel.Enqueue(CanFrame.Classic(0x58A, Convert.FromHexString("6000200000000000")));
        var client = new SdoClient(channel, NodeId);

        await client.DownloadAsync(0x2000, 0, [0x2A, 0x00, 0x00, 0x00]);

        // client sent expedited download request 0x23 (size specified, n=0) with the value:
        // cmd 0x23, index 0x2000 (LE 00 20), sub 00, value 2A 00 00 00
        Assert.Equal(Convert.FromHexString("230020002A000000"), channel.Sent[0].Data);
    }

    [Fact]
    public async Task Upload_throws_on_server_abort()
    {
        var channel = new InMemoryCanChannel();
        // server abort: object does not exist (0x06020000)
        channel.Enqueue(CanFrame.Classic(0x58A, Convert.FromHexString("8018100100000206")));
        var client = new SdoClient(channel, NodeId);

        var ex = await Assert.ThrowsAsync<SdoAbortException>(
            () => client.UploadAsync(0x1018, 1));
        Assert.Equal(SdoAbortCode.ObjectDoesNotExist, ex.Code);
    }

    [Fact]
    public async Task Upload_times_out_on_a_silent_bus()
    {
        var channel = new InMemoryCanChannel();   // nothing enqueued
        var client = new SdoClient(channel, NodeId, new SdoClientOptions
        {
            Timeout = TimeSpan.FromMilliseconds(50),
        });

        await Assert.ThrowsAsync<SdoTimeoutException>(() => client.UploadAsync(0x1018, 1));
    }

    [Fact]
    public async Task Frames_from_other_ids_are_ignored()
    {
        var channel = new InMemoryCanChannel();
        channel.Enqueue(
            CanFrame.Classic(0x581, Convert.FromHexString("4318100104000000")),   // wrong node
            CanFrame.Classic(0x58A, Convert.FromHexString("4318100199000000")));   // our node, value 0x99
        var client = new SdoClient(channel, NodeId);

        var value = await client.UploadAsync(0x1018, 1);

        Assert.Equal(new byte[] { 0x99, 0x00, 0x00, 0x00 }, value);
    }
}
