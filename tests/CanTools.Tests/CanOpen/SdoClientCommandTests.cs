using CanTools.CanOpen;
using CanTools.Tests.Transport;
using CanTools.Transport;

namespace CanTools.Tests.CanOpen;

// The standard store (0x1010) and restore (0x1011) SDO commands.
public class SdoClientCommandTests
{
    private const byte NodeId = 0x0A;   // request 0x60A, response 0x58A

    [Fact]
    public async Task Store_writes_the_save_signature_to_0x1010()
    {
        var channel = new InMemoryCanChannel();
        channel.Enqueue(CanFrame.Classic(0x58A, Convert.FromHexString("6010100100000000")));
        var client = new SdoClient(channel, NodeId);

        await client.StoreParametersAsync();

        // expedited download of "save" (73 61 76 65) to 0x1010 sub1, cmd 0x23
        Assert.Equal(Convert.FromHexString("2310100173617665"), channel.Sent[0].Data);
    }

    [Fact]
    public async Task Restore_writes_the_load_signature_to_0x1011()
    {
        var channel = new InMemoryCanChannel();
        channel.Enqueue(CanFrame.Classic(0x58A, Convert.FromHexString("6011100100000000")));
        var client = new SdoClient(channel, NodeId);

        await client.RestoreDefaultParametersAsync();

        // expedited download of "load" (6C 6F 61 64) to 0x1011 sub1, cmd 0x23
        Assert.Equal(Convert.FromHexString("231110016C6F6164"), channel.Sent[0].Data);
    }

    [Fact]
    public async Task Store_addresses_the_requested_group_subindex()
    {
        var channel = new InMemoryCanChannel();
        channel.Enqueue(CanFrame.Classic(0x58A, Convert.FromHexString("6010100200000000")));
        var client = new SdoClient(channel, NodeId);

        await client.StoreParametersAsync(CanOpenParameterGroup.Communication);

        // subindex 2 (communication parameters)
        Assert.Equal(0x02, channel.Sent[0].Data[3]);
        Assert.Equal(Convert.FromHexString("2310100273617665"), channel.Sent[0].Data);
    }
}
