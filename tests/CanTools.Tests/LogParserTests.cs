using CanTools.Logs;

namespace CanTools.Tests;

// Ported from tests/test_logreader.py. Absolute unix timestamps are asserted
// against the local-time conversion computed the same way, so the tests are
// timezone independent.
public class LogParserTests
{
    private static DateTime LocalTimeOfUnixMicroseconds(long seconds, long microseconds) =>
        DateTimeOffset.UnixEpoch.AddSeconds(seconds).AddTicks(microseconds * 10)
            .ToLocalTime().DateTime;

    // ported from test_logreader.py::test_empty_line
    [Fact]
    public void Empty_lines_parse_to_nothing()
    {
        Assert.Null(new LogParser().Parse(""));
    }

    // ported from test_logreader.py::test_candump
    [Fact]
    public void Plain_candump_lines_parse()
    {
        var parser = new LogParser();

        var entry = parser.Parse("vcan0  0C8   [8]  F0 00 00 00 00 00 00 00")!;
        Assert.Equal("vcan0", entry.Channel);
        Assert.Equal(0xc8u, entry.FrameId);
        Assert.False(entry.IsExtendedFrame);
        Assert.Equal(new byte[] { 0xf0, 0, 0, 0, 0, 0, 0, 0 }, entry.Data);
        Assert.False(entry.IsRemoteFrame);
        Assert.Equal(TimestampFormat.Missing, entry.TimestampFormat);

        entry = parser.Parse("  vcan1  064   [10]  F0 01 FF FF FF FF FF FF FF FF")!;
        Assert.Equal("vcan1", entry.Channel);
        Assert.Equal(0x64u, entry.FrameId);
        Assert.Equal(10, entry.Data.Length);

        Assert.Null(parser.Parse("  vcan0  ERROR"));

        entry = parser.Parse("  vcan0  1F3   [3]  01 02 03")!;
        Assert.Equal(0x1f3u, entry.FrameId);
        Assert.Equal(new byte[] { 1, 2, 3 }, entry.Data);

        entry = parser.Parse("  vcan0  00000123   [8]  12 34 56 78 90 AB CD EF")!;
        Assert.Equal(0x123u, entry.FrameId);
        Assert.True(entry.IsExtendedFrame);

        entry = parser.Parse("  vcan0  00000123   [0]  remote request")!;
        Assert.Equal(0x123u, entry.FrameId);
        Assert.True(entry.IsExtendedFrame);
        Assert.Empty(entry.Data);
        Assert.True(entry.IsRemoteFrame);

        entry = parser.Parse("  vcan-0  123   [8]  00 01 02 03 04 05 06 07")!;
        Assert.Equal("vcan-0", entry.Channel);
        entry = parser.Parse("  vcan.0  123   [8]  00 01 02 03 04 05 06 07")!;
        Assert.Equal("vcan.0", entry.Channel);
    }

    // ported from test_logreader.py::test_timestamped_candump
    [Fact]
    public void Timestamped_candump_lines_parse()
    {
        var parser = new LogParser();

        var entry = parser.Parse("(000.000000)  vcan0  0C8   [8]  F0 00 00 00 00 00 00 00")!;
        Assert.Equal(TimestampFormat.Relative, entry.TimestampFormat);
        Assert.Equal(TimeSpan.Zero, entry.TimeOffset);

        entry = parser.Parse("(002.047817)  vcan0  064   [8]  F0 01 FF FF FF FF FF FF")!;
        Assert.Equal(TimestampFormat.Relative, entry.TimestampFormat);
        Assert.Equal(TimeSpan.FromTicks(2 * TimeSpan.TicksPerSecond + 47817 * 10), entry.TimeOffset);

        // Values from 1991 onward are wall-clock times.
        entry = parser.Parse("(1613749650.388103)  can1       0AD  [08]  A6 55 3B CF 3F 1A F5 2A")!;
        Assert.Equal("can1", entry.Channel);
        Assert.Equal(0xadu, entry.FrameId);
        Assert.Equal(TimestampFormat.Absolute, entry.TimestampFormat);
        Assert.Equal(LocalTimeOfUnixMicroseconds(1613749650, 388103), entry.Timestamp);

        entry = parser.Parse(" (015.052211)  vcan0  00000123   [0]  remote request")!;
        Assert.True(entry.IsRemoteFrame);
        Assert.True(entry.IsExtendedFrame);
        Assert.Equal(TimeSpan.FromTicks(15 * TimeSpan.TicksPerSecond + 52211 * 10), entry.TimeOffset);
    }

    // ported from test_logreader.py::test_candump_log
    [Fact]
    public void Candump_log_lines_parse()
    {
        var parser = new LogParser();

        var entry = parser.Parse("(1579857014.345944) can2 486#82967A6B006B07F8")!;
        Assert.Equal("can2", entry.Channel);
        Assert.Equal(0x486u, entry.FrameId);
        Assert.False(entry.IsExtendedFrame);
        Assert.Equal(new byte[] { 0x82, 0x96, 0x7a, 0x6b, 0x00, 0x6b, 0x07, 0xf8 }, entry.Data);
        Assert.Equal(TimestampFormat.Absolute, entry.TimestampFormat);
        Assert.Equal(LocalTimeOfUnixMicroseconds(1579857014, 345944), entry.Timestamp);

        // CAN FD with flags nibble after the double hash.
        entry = parser.Parse(
            "(1613656104.501098) can3 14C##155B53476F7B82EEEB8E97236AC252B8BBB5B80A6A7734B2F675C6D2CEEC869D3")!;
        Assert.Equal("can3", entry.Channel);
        Assert.Equal(0x14cu, entry.FrameId);
        Assert.Equal(32, entry.Data.Length);
        Assert.Equal(0x55, entry.Data[0]);
        Assert.Equal(0xd3, entry.Data[^1]);

        entry = parser.Parse("(1780715436.846782) vcan-0 123#0001020304050607")!;
        Assert.Equal("vcan-0", entry.Channel);
        Assert.Equal(Enumerable.Range(0, 8).Select(i => (byte)i).ToArray(), entry.Data);

        entry = parser.Parse("(1752923603.673608) vcan0 00000123#R")!;
        Assert.Equal(0x123u, entry.FrameId);
        Assert.True(entry.IsExtendedFrame);
        Assert.True(entry.IsRemoteFrame);
        Assert.Empty(entry.Data);
    }

    // ported from test_logreader.py::test_candump_log_absolute_timestamp
    [Fact]
    public void Wall_clock_candump_lines_parse()
    {
        var parser = new LogParser();

        var entry = parser.Parse("(2020-12-19 12:04:45.485261)  vcan0  0C8   [8]  F0 00 00 00 00 00 00 00")!;
        Assert.Equal(TimestampFormat.Absolute, entry.TimestampFormat);
        Assert.Equal(new DateTime(2020, 12, 19, 12, 4, 45).AddTicks(485261 * 10), entry.Timestamp);

        entry = parser.Parse(" (2025-07-19 13:13:23.673608)  vcan0  00000123   [0]  remote request")!;
        Assert.True(entry.IsRemoteFrame);
        Assert.True(entry.IsExtendedFrame);
        Assert.Equal(new DateTime(2025, 7, 19, 13, 13, 23).AddTicks(673608 * 10), entry.Timestamp);
    }

    // ported from test_logreader.py::test_candump_log_ascii*
    [Fact]
    public void Ascii_decorations_after_the_data_are_ignored()
    {
        var parser = new LogParser();
        var entry = parser.Parse(" can1  123   [8]  31 30 30 2E 35 20 46 4D   '100.5 FM'")!;

        Assert.Equal(new byte[] { 0x31, 0x30, 0x30, 0x2e, 0x35, 0x20, 0x46, 0x4d }, entry.Data);
        Assert.Null(entry.Timestamp);
        Assert.Equal(TimestampFormat.Missing, entry.TimestampFormat);

        entry = new LogParser().Parse(
            "  (1621271100.919019)  can1  123   [8]  31 30 30 2E 35 20 46 4D   '100.5 FM'")!;
        Assert.Equal(TimestampFormat.Absolute, entry.TimestampFormat);

        entry = new LogParser().Parse(
            "(2020-12-19 12:04:45.485261)  can1  123   [8]  31 30 30 2E 35 20 46 4D   '100.5 FM'")!;
        Assert.Equal(TimestampFormat.Absolute, entry.TimestampFormat);
        Assert.Equal(new DateTime(2020, 12, 19, 12, 4, 45).AddTicks(485261 * 10), entry.Timestamp);
    }

    // ported from test_logreader.py::test_pcan_traceV10 .. V21
    [Theory]
    [InlineData("1) 1841 0001 8 F0 00 00 00 00 00 00 00", "pcanx", 0x1u, false, 1841000)]
    [InlineData("     4)      1844      0100  3  RTR", "pcanx", 0x100u, true, 1844000)]
    [InlineData("1)      6357.2 Rx        0401  8    F0 00 00 00 00 00 00 00", "pcanx", 0x401u, false, 6357200)]
    [InlineData("     7)      1352.7  Rx         0100  3  RTR", "pcanx", 0x100u, true, 1352700)]
    [InlineData("1)      6357.213 1  Rx        0401  8    F0 00 00 00 00 00 00 00", "pcan1", 0x401u, false, 6357213)]
    [InlineData("     7)      1352.743  1  Rx         0100  3  RTR", "pcan1", 0x100u, true, 1352743)]
    [InlineData("1)      6357.213 1  Rx        0401 -  8    F0 00 00 00 00 00 00 00", "pcan1", 0x401u, false, 6357213)]
    [InlineData("     7)      1352.743 1  Rx        0100 -  3    RTR", "pcan1", 0x100u, true, 1352743)]
    [InlineData(" 1      1059.900 DT 0300 Rx 7 00 00 00 00 04 00 00", "pcanx", 0x300u, false, 1059900)]
    [InlineData("   10      1336.543 RR     0100 Rx 3 ", "pcanx", 0x100u, true, 1336543)]
    [InlineData(" 1      1059.900 DT 1 0300 Rx - 7 00 00 00 00 04 00 00", "pcan1", 0x300u, false, 1059900)]
    [InlineData("     13      1336.543 RR 1      0100 Rx -  3 ", "pcan1", 0x100u, true, 1336543)]
    public void Pcan_trace_lines_parse(
        string line, string channel, uint frameId, bool isRemote, long microseconds)
    {
        var entry = new LogParser().Parse(line)!;

        Assert.Equal(channel, entry.Channel);
        Assert.Equal(frameId, entry.FrameId);
        Assert.Equal(isRemote, entry.IsRemoteFrame);
        Assert.Equal(TimestampFormat.Relative, entry.TimestampFormat);
        Assert.Equal(TimeSpan.FromTicks(microseconds * 10), entry.TimeOffset);
    }

    // ported from test_logreader.py::test_pcan_traceV21 (extended id)
    [Fact]
    public void Pcan_extended_frames_parse()
    {
        var entry = new LogParser().Parse("12 1335.156 DT 1 18EFC034 Tx - 8 01 02 03 04 05 06 07 08")!;

        Assert.Equal("pcan1", entry.Channel);
        Assert.Equal(0x18EFC034u, entry.FrameId);
        Assert.True(entry.IsExtendedFrame);
        Assert.Equal(new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 }, entry.Data);
    }

    // ported from test_logreader.py::TestLogreaderStreams::test_candump
    [Fact]
    public void Streams_skip_unparseable_lines()
    {
        var reader = new StringReader(
            "  vcan0  0C8   [8]  F0 00 00 00 00 00 00 00\n"
            + "  vcan0  064   [10]  F0 01 FF FF FF FF FF FF FF FF\n"
            + "  vcan0  ERROR\n"
            + "\n"
            + "  vcan0  1F4   [4]  01 02 03 04\n"
            + "  vcan0  1F3   [3]  01 02 03\n");

        var entries = new LogParser(reader).ReadEntries().ToList();

        Assert.Equal([0xc8u, 0x64u, 0x1f4u, 0x1f3u], entries.Select(e => e.FrameId));
    }

    // ported from test_logreader.py::TestLogreaderStreams::test_pcan_traceV11
    [Fact]
    public void Pcan_streams_skip_headers_errors_and_warnings()
    {
        var reader = new StringReader(
            ";$FILEVERSION=1.1\n"
            + ";$STARTTIME=37704.5364870833\n"
            + ";---+-- ----+---- --+-- ----+--- + -+ -- -- -- -- -- -- --\n"
            + "1) 1059.9 Rx 0300 7 00 00 00 00 04 00 00\n"
            + "2) 1283.2 Rx 0300 7 00 00 00 00 04 00 00\n"
            + "3) 1298.9 Tx 0400 2 00 00\n"
            + "4) 1323.0 Rx 0300 7 00 00 00 00 06 00 00\n"
            + "5) 1346.8 Warng FFFFFFFF 4 00 00 00 04 BUSLIGHT\n"
            + "6) 1349.2 Error 0008 4 00 19 08 08\n"
            + "7) 1352.7 Rx 0100 3 RTR\n");

        var entries = new LogParser(reader).ReadEntries().ToList();

        Assert.Equal([0x300u, 0x300u, 0x400u, 0x300u, 0x100u], entries.Select(e => e.FrameId));
        Assert.True(entries[^1].IsRemoteFrame);
    }

    // ported from test_logreader.py::TestLogreaderStreams::test_pcan_traceV20
    [Fact]
    public void Pcan_v20_streams_skip_status_records()
    {
        var reader = new StringReader(
            ";$FILEVERSION=2.0\n"
            + "1 1059.900 DT 0300 Rx 7 00 00 00 00 04 00 00\n"
            + "2 1283.231 DT 0300 Rx 7 00 00 00 00 04 00 00\n"
            + "3 1298.945 DT 0400 Tx 2 00 00\n"
            + "4 1323.201 DT 0300 Rx 7 00 00 00 00 06 00 00\n"
            + "5 1334.416 FD 0500 Tx 12 01 02 03 04 05 06 07 08 09 0A 0B 0C\n"
            + "6 1334.522 ER Rx 04 00 02 00 00\n"
            + "7 1334.531 ST Rx 00 00 00 08\n"
            + "8 1334.643 EC Rx 02 02\n"
            + "9 1335.156 DT 18EFC034 Tx 8 01 02 03 04 05 06 07 08\n"
            + "10 1336.543 RR 0100 Rx 3 \n");

        var entries = new LogParser(reader).ReadEntries().ToList();

        Assert.Equal(
            [0x300u, 0x300u, 0x400u, 0x300u, 0x500u, 0x18EFC034u, 0x100u],
            entries.Select(e => e.FrameId));
    }

    // ported from test_logreader.py::TestLogreaderStreams::test_candump_log_fd_absolute_time
    [Fact]
    public void Fd_frames_in_log_files_parse()
    {
        var reader = new StringReader(
            "  (1613656104.493702) can2 102##1150B7F0102010010000064A0020000100000000000E41F0000"
            + "00000090D1FF000020A600000000210100000000000000\n");

        var entry = Assert.Single(new LogParser(reader).ReadEntries());
        Assert.Equal(0x102u, entry.FrameId);
    }
}
