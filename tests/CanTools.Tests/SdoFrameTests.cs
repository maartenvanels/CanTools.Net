using CanTools.CanOpen;

namespace CanTools.Tests;

// Stateless SDO frame classification and the expedited/abort codecs; golden
// vectors come from python-canopen's test_sdo.py.
public class SdoFrameTests
{
    private static SdoFrame Request(string hex) =>
        SdoFrame.Parse(SdoDirection.Request, Convert.FromHexString(hex));

    private static SdoFrame Response(string hex) =>
        SdoFrame.Parse(SdoDirection.Response, Convert.FromHexString(hex));

    [Fact]
    public void An_upload_request_asks_for_an_entry()
    {
        // ported from test_sdo.py::test_expedited_upload
        var request = new SdoUploadRequest(0x1018, 1);

        Assert.Equal(Convert.FromHexString("4018100100000000"), request.ToBytes());
        Assert.Equal(request, Request("4018100100000000"));
    }

    [Fact]
    public void An_expedited_upload_response_carries_the_value()
    {
        // ported from test_sdo.py::test_expedited_upload
        var response = Assert.IsType<SdoUploadResponse>(Response("4318100104000000"));

        Assert.Equal(0x1018, response.Index);
        Assert.Equal(1, response.Subindex);
        Assert.True(response.IsExpedited);
        Assert.True(response.SizeSpecified);
        Assert.Equal(new byte[] { 0x04, 0x00, 0x00, 0x00 }, response.ExpeditedData);
    }

    [Fact]
    public void A_five_byte_frame_with_one_data_byte_parses()
    {
        // ported from test_sdo.py::test_expedited_upload (DLC 5 response)
        var response = Assert.IsType<SdoUploadResponse>(Response("4f001402fe"));

        Assert.Equal(new byte[] { 0xFE }, response.ExpeditedData);
    }

    [Fact]
    public void An_expedited_response_without_a_size_keeps_all_four_bytes()
    {
        // ported from test_sdo.py::test_size_not_specified
        var response = Assert.IsType<SdoUploadResponse>(Response("42001402fe000000"));

        Assert.False(response.SizeSpecified);
        Assert.Equal(new byte[] { 0xFE, 0x00, 0x00, 0x00 }, response.ExpeditedData);
    }

    // ported from test_sdo.py datatype uploads: n encodes 4 - size
    [Theory]
    [InlineData("4f", 1)]
    [InlineData("4b", 2)]
    [InlineData("47", 3)]
    [InlineData("43", 4)]
    public void The_command_byte_encodes_the_expedited_size(string command, int size)
    {
        var response = Assert.IsType<SdoUploadResponse>(
            Response(command + "001402fefdfcfb"));

        Assert.Equal(
            new byte[] { 0xFE, 0xFD, 0xFC, 0xFB }.Take(size).ToArray(),
            response.ExpeditedData);
    }

    [Fact]
    public void An_expedited_download_writes_a_value()
    {
        // ported from test_sdo.py::test_expedited_download (write 4000 to 0x1017)
        var request = new SdoDownloadRequest(0x1017, 0, [0xA0, 0x0F]);

        Assert.Equal(Convert.FromHexString("2b171000a00f0000"), request.ToBytes());

        var parsed = Assert.IsType<SdoDownloadRequest>(Request("2b171000a00f0000"));

        Assert.Equal(0x1017, parsed.Index);
        Assert.Equal(new byte[] { 0xA0, 0x0F }, parsed.ExpeditedData);
        Assert.True(parsed.SizeSpecified);

        var response = Assert.IsType<SdoDownloadResponse>(Response("6017100000000000"));

        Assert.Equal(0x1017, response.Index);
        Assert.Equal(Convert.FromHexString("6017100000000000"), response.ToBytes());
    }

    [Fact]
    public void A_segmented_upload_response_announces_its_size()
    {
        // ported from test_sdo.py::test_segmented_upload (0x41: size 26 follows in segments)
        var response = Assert.IsType<SdoUploadResponse>(Response("410810001a000000"));

        Assert.False(response.IsExpedited);
        Assert.True(response.SizeSpecified);
        Assert.Equal(26u, response.Size);
        Assert.Equal(Convert.FromHexString("410810001a000000"), response.ToBytes());
    }

    [Fact]
    public void A_segmented_download_request_announces_its_size()
    {
        // ported from test_sdo.py::test_segmented_download (0x21: size 13)
        var request = Assert.IsType<SdoDownloadRequest>(Request("210020000d000000"));

        Assert.False(request.IsExpedited);
        Assert.Equal(13u, request.Size);
        Assert.Equal(
            Convert.FromHexString("210020000d000000"),
            (new SdoDownloadRequest(0x2000, 0) { Size = 13, SizeSpecified = true }).ToBytes());
    }

    [Fact]
    public void Upload_segments_carry_toggle_length_and_last_flag()
    {
        // ported from test_sdo.py::test_segmented_upload (final segment 0x15)
        var request = Assert.IsType<SdoUploadSegmentRequest>(Request("6000000000000000"));

        Assert.False(request.Toggle);
        Assert.Equal(Convert.FromHexString("6000000000000000"), request.ToBytes());
        Assert.True(Assert.IsType<SdoUploadSegmentRequest>(Request("7000000000000000")).Toggle);

        var segment = Assert.IsType<SdoUploadSegmentResponse>(Response("15696e7320210000"));

        Assert.True(segment.Toggle);
        Assert.True(segment.IsLast);
        Assert.Equal("ins !"u8.ToArray(), segment.Data);
        Assert.Equal(Convert.FromHexString("15696e7320210000"), segment.ToBytes());
    }

    [Fact]
    public void Download_segments_carry_toggle_length_and_last_flag()
    {
        // ported from test_sdo.py::test_segmented_download ("A long string")
        var first = Assert.IsType<SdoDownloadSegmentRequest>(Request("0041206c6f6e6720"));

        Assert.False(first.Toggle);
        Assert.False(first.IsLast);
        Assert.Equal("A long "u8.ToArray(), first.Data);

        var last = Assert.IsType<SdoDownloadSegmentRequest>(Request("13737472696e6700"));

        Assert.True(last.IsLast);
        Assert.Equal("string"u8.ToArray(), last.Data);
        Assert.Equal(Convert.FromHexString("13737472696e6700"), last.ToBytes());

        var acknowledged = Assert.IsType<SdoDownloadSegmentResponse>(Response("2000000000000000"));

        Assert.False(acknowledged.Toggle);
        Assert.True(Assert.IsType<SdoDownloadSegmentResponse>(Response("3000000000000000")).Toggle);
        Assert.Equal(Convert.FromHexString("3000000000000000"),
            new SdoDownloadSegmentResponse(Toggle: true).ToBytes());
    }

    [Fact]
    public void An_empty_final_segment_closes_a_zero_length_download()
    {
        // ported from test_sdo.py::test_segmented_download_zero_length (0x0F)
        var segment = Assert.IsType<SdoDownloadSegmentRequest>(Request("0f00000000000000"));

        Assert.True(segment.IsLast);
        Assert.Empty(segment.Data);
    }

    [Fact]
    public void An_abort_names_the_entry_and_the_reason()
    {
        // ported from test_sdo.py::test_abort
        var abort = Assert.IsType<SdoAbort>(Response("8018100111000906"));

        Assert.Equal(0x1018, abort.Index);
        Assert.Equal(1, abort.Subindex);
        Assert.Equal(SdoAbortCode.SubindexDoesNotExist, abort.Code);
        Assert.Equal("Subindex does not exist", abort.Description);
        Assert.Equal("Code 0x06090011, Subindex does not exist", abort.ToString());
        Assert.Equal(Convert.FromHexString("8018100111000906"), abort.ToBytes());
    }

    [Fact]
    public void Aborts_parse_in_both_directions_and_tolerate_unknown_codes()
    {
        var abort = Assert.IsType<SdoAbort>(Request("8018100178563412"));

        Assert.Equal((SdoAbortCode)0x12345678, abort.Code);
        Assert.Equal("", abort.Description);
        Assert.Equal("Code 0x12345678", abort.ToString());
    }

    // block transfer frames are classified only; sequencing is stateful (phase E4)
    [Theory]
    [InlineData(SdoDirection.Request, "a0", SdoBlockTransfer.Upload)]
    [InlineData(SdoDirection.Request, "c6", SdoBlockTransfer.Download)]
    [InlineData(SdoDirection.Response, "a4", SdoBlockTransfer.Download)]
    [InlineData(SdoDirection.Response, "c0", SdoBlockTransfer.Upload)]
    public void Block_frames_are_classified_by_direction(
        SdoDirection direction, string command, SdoBlockTransfer transfer)
    {
        var data = Convert.FromHexString(command + "18100100000000");
        var frame = Assert.IsType<SdoBlockFrame>(SdoFrame.Parse(direction, data));

        Assert.Equal(transfer, frame.Transfer);
        Assert.Equal(data, frame.Data);
    }

    [Fact]
    public void Reserved_command_specifiers_do_not_parse()
    {
        // python-canopen's server aborts these with "Unknown SDO command specified"
        Assert.Throws<DecodeException>(() => Request("e000000000000000"));
        Assert.Throws<DecodeException>(() => Response("ff00000000000000"));
    }

    [Fact]
    public void Truncated_frames_do_not_parse()
    {
        Assert.Throws<DecodeException>(() => Response("431810"));
        Assert.Throws<DecodeException>(() => Response("4318100104"));
        Assert.Throws<DecodeException>(() => Response("80181001110009"));
    }

    [Fact]
    public void Oversized_payloads_do_not_build()
    {
        Assert.Throws<EncodeException>(
            () => new SdoDownloadRequest(0x2000, 0, new byte[5]).ToBytes());
        Assert.Throws<EncodeException>(
            () => new SdoUploadSegmentResponse(false, new byte[8], false).ToBytes());
        Assert.Throws<EncodeException>(
            () => (new SdoDownloadRequest(0x2000, 0) { ExpeditedData = new byte[2] }).ToBytes());
    }
}
