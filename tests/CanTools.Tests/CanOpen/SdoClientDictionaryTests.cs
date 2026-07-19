using CanTools.CanOpen;
using CanTools.Formats.Eds;
using CanTools.Tests.Transport;
using CanTools.Transport;

namespace CanTools.Tests.CanOpen;

// The dictionary-aware SDO overloads: index, subindex and data type come from an
// ObjectDictionary (loaded from an EDS), so callers name an entry instead of
// spelling out raw indices and CiA 301 type codes.
public class SdoClientDictionaryTests
{
    private const byte NodeId = 0x0A;   // request 0x60A, response 0x58A

    // A tiny dictionary with one entry of each shape the tests exercise.
    private static ObjectDictionary Dictionary() => EdsReader.LoadString("""
        [1000]
        ParameterName=Device type
        DataType=0x0007
        AccessType=ro

        [1008]
        ParameterName=Manufacturer device name
        DataType=0x0009
        AccessType=const

        [2000]
        ParameterName=Sample parameter
        DataType=0x0006
        AccessType=rw

        [2001]
        ParameterName=Signed parameter
        DataType=0x0003
        AccessType=rw
        """);

    [Fact]
    public async Task Upload_by_name_resolves_index_and_type()
    {
        var channel = new InMemoryCanChannel();
        // expedited upload response for 0x1000, U32 = 0x000F0191 (LE 91 01 0F 00)
        channel.Enqueue(CanFrame.Classic(0x58A, Convert.FromHexString("4300100091010F00")));
        var client = new SdoClient(channel, NodeId);

        var value = await client.UploadAsync(Dictionary(), "Device type");

        Assert.Equal(0x000F0191u, value.ToUInt64());
        // it addressed 0x1000sub0 (initiate upload request 0x40)
        Assert.Equal(Convert.FromHexString("4000100000000000"), channel.Sent[0].Data);
    }

    [Fact]
    public async Task Upload_by_name_decodes_with_the_dictionary_type()
    {
        var channel = new InMemoryCanChannel();
        // 0x2001 is INTEGER16; bytes FF FF are -1 as signed, 65535 as unsigned.
        // cmd 0x4B = expedited, size specified, 2 data bytes.
        channel.Enqueue(CanFrame.Classic(0x58A, Convert.FromHexString("4B012000FFFF0000")));
        var client = new SdoClient(channel, NodeId);

        var value = await client.UploadAsync(Dictionary(), "Signed parameter");

        Assert.Equal(-1, value.ToInt64());   // the OD's INTEGER16 type drives the decode
    }

    [Fact]
    public async Task Upload_by_index_resolves_type()
    {
        var channel = new InMemoryCanChannel();
        channel.Enqueue(CanFrame.Classic(0x58A, Convert.FromHexString("4B002000D2040000")));
        var client = new SdoClient(channel, NodeId);

        var value = await client.UploadAsync(Dictionary(), 0x2000);

        Assert.Equal(1234u, value.ToUInt64());
    }

    [Fact]
    public async Task Upload_by_variable_resolves_index_subindex_and_type()
    {
        var od = Dictionary();
        var variable = od.GetVariable(0x2000)!;
        var channel = new InMemoryCanChannel();
        channel.Enqueue(CanFrame.Classic(0x58A, Convert.FromHexString("4B002000D2040000")));
        var client = new SdoClient(channel, NodeId);

        var value = await client.UploadAsync(variable);

        Assert.Equal(1234u, value.ToUInt64());
        Assert.Equal(Convert.FromHexString("4000200000000000"), channel.Sent[0].Data);
    }

    [Fact]
    public async Task Upload_by_name_reads_a_segmented_string()
    {
        var channel = new InMemoryCanChannel();
        channel.Enqueue(
            // initiate: segmented upload, size specified = 8
            CanFrame.Classic(0x58A, Convert.FromHexString("4108100008000000")),
            // segment 1: toggle 0, 7 bytes "CanTool", not last
            CanFrame.Classic(0x58A, Convert.FromHexString("0043616E546F6F6C")),
            // segment 2: toggle 1, 1 byte "s", last (cmd 0x1D)
            CanFrame.Classic(0x58A, Convert.FromHexString("1D73000000000000")));
        var client = new SdoClient(channel, NodeId);

        var value = await client.UploadAsync(Dictionary(), "Manufacturer device name");

        Assert.Equal("CanTools", value.Text);
    }

    [Fact]
    public async Task Download_by_name_encodes_with_the_dictionary_type()
    {
        var channel = new InMemoryCanChannel();
        // server accepts the write for 0x2000sub0
        channel.Enqueue(CanFrame.Classic(0x58A, Convert.FromHexString("6000200000000000")));
        var client = new SdoClient(channel, NodeId);

        await client.DownloadAsync(Dictionary(), "Sample parameter", (OdValue)1234UL);

        // expedited download of a 2-byte U16 value 1234 (LE D2 04), cmd 0x2B
        Assert.Equal(Convert.FromHexString("2B002000D2040000"), channel.Sent[0].Data);
    }

    [Fact]
    public async Task Download_by_variable_encodes_with_the_dictionary_type()
    {
        var od = Dictionary();
        var channel = new InMemoryCanChannel();
        channel.Enqueue(CanFrame.Classic(0x58A, Convert.FromHexString("6001200000000000")));
        var client = new SdoClient(channel, NodeId);

        await client.DownloadAsync(od.GetVariable(0x2001)!, (OdValue)(-1));

        // INTEGER16 -1 encodes to FF FF, cmd 0x2B
        Assert.Equal(Convert.FromHexString("2B012000FFFF0000"), channel.Sent[0].Data);
    }

    [Fact]
    public async Task Upload_by_unknown_name_throws()
    {
        var client = new SdoClient(new InMemoryCanChannel(), NodeId);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => client.UploadAsync(Dictionary(), "No such parameter"));
    }

    [Fact]
    public async Task Upload_by_unknown_index_throws()
    {
        var client = new SdoClient(new InMemoryCanChannel(), NodeId);

        await Assert.ThrowsAsync<KeyNotFoundException>(
            () => client.UploadAsync(Dictionary(), 0x1234));
    }
}
