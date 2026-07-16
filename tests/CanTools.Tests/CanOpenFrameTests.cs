using CanTools.CanOpen;

namespace CanTools.Tests;

// The frame codecs follow python-canopen's nmt.py; golden vectors from test_nmt.py.
public class NmtFrameTests
{
    [Fact]
    public void Broadcast_stop_targets_every_node()
    {
        // ported from test_nmt.py::test_nmt_master_send_nmt_broadcast
        var message = NmtMessage.Parse([2, 0]);

        Assert.Equal(NmtCommand.Stop, message.Command);
        Assert.True(message.IsBroadcast);
        Assert.True(message.Targets(2));
        Assert.True(message.Targets(125));
    }

    [Fact]
    public void Addressed_command_targets_one_node()
    {
        var message = new NmtMessage(NmtCommand.Start, 5);

        Assert.Equal(new byte[] { 0x01, 0x05 }, message.ToBytes());
        Assert.True(message.Targets(5));
        Assert.False(message.Targets(6));
        Assert.Equal(message, NmtMessage.Parse(message.ToBytes()));
    }

    // ported from test_nmt.py::test_nmt_master_send_command (COMMAND_TO_STATE)
    [Theory]
    [InlineData(NmtCommand.Start, NmtState.Operational)]
    [InlineData(NmtCommand.Stop, NmtState.Stopped)]
    [InlineData(NmtCommand.Sleep, NmtState.Sleep)]
    [InlineData(NmtCommand.Standby, NmtState.Standby)]
    [InlineData(NmtCommand.EnterPreOperational, NmtState.PreOperational)]
    [InlineData(NmtCommand.ResetNode, NmtState.Initialising)]
    [InlineData(NmtCommand.ResetCommunication, NmtState.Initialising)]
    public void Commands_map_to_their_target_state(NmtCommand command, NmtState state)
    {
        Assert.Equal(state, command.TargetState());
    }

    [Fact]
    public void Unknown_commands_have_no_target_state()
    {
        Assert.Null(((NmtCommand)0x7F).TargetState());
    }

    [Fact]
    public void A_short_frame_does_not_parse()
    {
        Assert.Throws<DecodeException>(() => NmtMessage.Parse([2]));
    }

    // ported from test_nmt.py::test_nmt_master_on_heartbeat
    [Theory]
    [InlineData(0x04, NmtState.Stopped)]
    [InlineData(0x05, NmtState.Operational)]
    [InlineData(0x50, NmtState.Sleep)]
    [InlineData(0x60, NmtState.Standby)]
    [InlineData(0x7F, NmtState.PreOperational)]
    public void Heartbeats_report_the_node_state(byte raw, NmtState state)
    {
        var heartbeat = Heartbeat.Parse([raw]);

        Assert.Equal(state, heartbeat.State);
        Assert.False(heartbeat.Toggle);
        Assert.False(heartbeat.IsBootUp);
    }

    [Fact]
    public void A_zero_state_is_a_boot_up()
    {
        // ported from test_nmt.py::test_nmt_master_wait_for_bootup
        Assert.True(Heartbeat.Parse([0x00]).IsBootUp);
    }

    [Fact]
    public void The_node_guarding_toggle_bit_is_split_off()
    {
        // ported from test_nmt.py::test_nmt_master_on_heartbeat_unknown_state (0xCB)
        var heartbeat = Heartbeat.Parse([0xCB]);

        Assert.True(heartbeat.Toggle);
        Assert.Equal((NmtState)0x4B, heartbeat.State);
        Assert.False(Enum.IsDefined(heartbeat.State));
    }

    [Fact]
    public void Heartbeats_build_with_and_without_toggle()
    {
        Assert.Equal(new byte[] { 0x05 }, new Heartbeat(NmtState.Operational).ToBytes());
        Assert.Equal(new byte[] { 0x85 }, new Heartbeat(NmtState.Operational, Toggle: true).ToBytes());
    }
}

// Layout and error classes follow python-canopen's emcy.py.
public class EmergencyMessageTests
{
    [Fact]
    public void Frames_parse_into_code_register_and_vendor_data()
    {
        // ported from test_emcy.py::test_emcy_consumer_on_emcy
        var message = EmergencyMessage.Parse(Convert.FromHexString("0120020001020304"));

        Assert.Equal(0x2001, message.ErrorCode);
        Assert.Equal(2, message.ErrorRegister);
        Assert.Equal(new byte[] { 0x00, 0x01, 0x02, 0x03, 0x04 }, message.VendorData.ToArray());
        Assert.False(message.IsErrorReset);
    }

    [Fact]
    public void Codes_below_0x100_reset_previous_errors()
    {
        // ported from test_emcy.py::test_emcy_consumer_reset (0x0099)
        Assert.True(EmergencyMessage.Parse(Convert.FromHexString("9900010000000000")).IsErrorReset);
        Assert.True(new EmergencyMessage(0x0000).IsErrorReset);
    }

    // ported from test_emcy.py::test_emcy_producer_send / _reset
    [Theory]
    [InlineData(0x2001, 0x00, "", "0120000000000000")]
    [InlineData(0x2001, 0x02, "", "0120020000000000")]
    [InlineData(0x2001, 0x02, "2a", "0120022a00000000")]
    [InlineData(0x0000, 0x00, "", "0000000000000000")]
    [InlineData(0x0000, 0x03, "", "0000030000000000")]
    [InlineData(0x0000, 0x03, "aabb", "000003aabb000000")]
    [InlineData(0xFFFF, 0xFF, "ffffffffff", "ffffffffffffffff")]
    [InlineData(0x1234, 0x56, "abcd", "341256abcd000000")]
    [InlineData(0x1234, 0x56, "abcdef1234", "341256abcdef1234")]
    public void Frames_build_with_zero_padded_vendor_data(
        int code, byte register, string vendorHex, string expectedHex)
    {
        var message = new EmergencyMessage((ushort)code, register, Convert.FromHexString(vendorHex));

        Assert.Equal(Convert.FromHexString(expectedHex), message.ToBytes());
    }

    // ported from test_emcy.py::test_emcy_error_str
    [Theory]
    [InlineData(0x2001, "Code 0x2001, Current")]
    [InlineData(0x3ABC, "Code 0x3ABC, Voltage")]
    [InlineData(0x0234, "Code 0x0234")]
    [InlineData(0xBEEF, "Code 0xBEEF")]
    public void Messages_format_like_python_canopen(int code, string expected)
    {
        Assert.Equal(expected, new EmergencyMessage((ushort)code).ToString());
    }

    // ported from test_emcy.py::test_emcy_error_get_desc (mask boundaries)
    [Theory]
    [InlineData(0x0000, "Error Reset / No Error")]
    [InlineData(0x00FF, "Error Reset / No Error")]
    [InlineData(0x0100, "")]
    [InlineData(0x1000, "Generic Error")]
    [InlineData(0x10FF, "Generic Error")]
    [InlineData(0x1100, "")]
    [InlineData(0x2000, "Current")]
    [InlineData(0x2FFF, "Current")]
    [InlineData(0x3000, "Voltage")]
    [InlineData(0x3FFF, "Voltage")]
    [InlineData(0x4000, "Temperature")]
    [InlineData(0x4FFF, "Temperature")]
    [InlineData(0x5000, "Device Hardware")]
    [InlineData(0x50FF, "Device Hardware")]
    [InlineData(0x5100, "")]
    [InlineData(0x6000, "Device Software")]
    [InlineData(0x6FFF, "Device Software")]
    [InlineData(0x7000, "Additional Modules")]
    [InlineData(0x70FF, "Additional Modules")]
    [InlineData(0x7100, "")]
    [InlineData(0x8000, "Monitoring")]
    [InlineData(0x8FFF, "Monitoring")]
    [InlineData(0x9000, "External Error")]
    [InlineData(0x90FF, "External Error")]
    [InlineData(0x9100, "")]
    [InlineData(0xF000, "Additional Functions")]
    [InlineData(0xF0FF, "Additional Functions")]
    [InlineData(0xF100, "")]
    [InlineData(0xFF00, "Device Specific")]
    [InlineData(0xFFFF, "Device Specific")]
    public void Error_classes_match_the_cia301_masks(int code, string description)
    {
        Assert.Equal(description, EmergencyMessage.DescriptionOf((ushort)code));
    }

    [Fact]
    public void A_short_frame_does_not_parse()
    {
        Assert.Throws<DecodeException>(
            () => EmergencyMessage.Parse(Convert.FromHexString("0120020001")));
    }

    [Fact]
    public void Vendor_data_is_at_most_five_bytes()
    {
        Assert.Throws<ArgumentException>(() => new EmergencyMessage(0x2001, 0, new byte[6]));
    }
}

public class SyncMessageTests
{
    // ported from test_sync.py::test_sync_producer_transmit(_count)
    [Fact]
    public void Sync_frames_are_empty_or_carry_a_counter()
    {
        Assert.Null(SyncMessage.Parse([]).Counter);
        Assert.Equal((byte)2, SyncMessage.Parse([0x02]).Counter);

        Assert.Empty(new SyncMessage().ToBytes());
        Assert.Equal(new byte[] { 0x02 }, new SyncMessage(2).ToBytes());
    }

    [Fact]
    public void A_long_frame_does_not_parse()
    {
        Assert.Throws<DecodeException>(() => SyncMessage.Parse([1, 2]));
    }
}

public class TimeMessageTests
{
    [Fact]
    public void The_epoch_is_1984()
    {
        // ported from test_time.py::test_time_producer (OFFSET == 441763200)
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(441763200), TimeMessage.Epoch);
    }

    [Fact]
    public void Timestamps_build_like_python_canopen()
    {
        // ported from test_time.py::test_time_producer (unix 1927999438)
        var message = TimeMessage.From(DateTimeOffset.FromUnixTimeSeconds(1927999438));

        Assert.Equal(Convert.FromHexString("b0a429043143"), message.ToBytes());
    }

    [Fact]
    public void Frames_parse_back_into_a_timestamp()
    {
        var message = TimeMessage.Parse(Convert.FromHexString("b0a429043143"));

        Assert.Equal(17201, message.Days);
        Assert.Equal(69838000u, message.MillisecondsOfDay);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1927999438), message.Timestamp);
    }

    [Fact]
    public void Times_before_the_epoch_do_not_build()
    {
        Assert.Throws<ArgumentOutOfRangeException>(
            () => TimeMessage.From(new DateTimeOffset(1983, 12, 31, 23, 59, 59, TimeSpan.Zero)));
    }

    [Fact]
    public void A_short_frame_does_not_parse()
    {
        Assert.Throws<DecodeException>(() => TimeMessage.Parse(Convert.FromHexString("b0a4290431")));
    }
}
