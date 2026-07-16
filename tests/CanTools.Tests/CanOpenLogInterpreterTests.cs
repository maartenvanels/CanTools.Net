using System.Text;
using CanTools.CanOpen;
using CanTools.Formats.Eds;
using CanTools.Logs;
using CanTools.Model;

namespace CanTools.Tests;

// The interpreter folds candump streams into typed events. SDO conversations are
// the golden ones from python-canopen's test_sdo.py (node 2); the rest exercises
// the CiA 301 connection set routing.
public class CanOpenLogInterpreterTests
{
    private static List<CanOpenEvent> Interpret(params string[] frames) => Interpret(null, frames);

    private static List<CanOpenEvent> Interpret(Database? pdoDatabase, string[] frames)
    {
        var parser = new LogParser();
        var interpreter = new CanOpenLogInterpreter(pdoDatabase);
        var entries = frames.Select(frame =>
        {
            var entry = parser.Parse($"(1594113208.376) can0 {frame}");
            Assert.NotNull(entry);
            return entry;
        });

        return interpreter.Interpret(entries).ToList();
    }

    [Fact]
    public void Broadcast_frames_become_typed_events()
    {
        var events = Interpret("000#0105", "080#", "080#02", "100#B0A429043143");

        var nmt = Assert.IsType<NmtCommandEvent>(events[0]);
        Assert.Equal(NmtCommand.Start, nmt.Nmt.Command);
        Assert.Equal(5, nmt.Nmt.NodeId);

        Assert.Null(Assert.IsType<SyncEvent>(events[1]).Sync.Counter);
        Assert.Equal((byte)2, Assert.IsType<SyncEvent>(events[2]).Sync.Counter);

        var time = Assert.IsType<TimeEvent>(events[3]);
        Assert.Equal(DateTimeOffset.FromUnixTimeSeconds(1927999438), time.Time.Timestamp);
        Assert.NotNull(time.Entry.Timestamp);
    }

    [Fact]
    public void Emergencies_carry_the_node_and_the_error()
    {
        var emergency = Assert.IsType<EmergencyEvent>(
            Interpret("085#0120020001020304").Single());

        Assert.Equal(5, emergency.NodeId);
        Assert.Equal(0x2001, emergency.Emergency.ErrorCode);
        Assert.Equal("Current", emergency.Emergency.Description);
    }

    [Fact]
    public void Heartbeats_track_the_state_per_node()
    {
        var events = Interpret("705#00", "705#05", "705#05", "706#05", "705#7F");

        Assert.Equal(5, Assert.IsType<BootUpEvent>(events[0]).NodeId);

        var first = Assert.IsType<HeartbeatEvent>(events[1]);
        Assert.Equal(NmtState.Operational, first.Heartbeat.State);
        Assert.Equal(NmtState.Initialising, first.PreviousState);
        Assert.True(first.IsStateChange);

        Assert.False(Assert.IsType<HeartbeatEvent>(events[2]).IsStateChange);

        var otherNode = Assert.IsType<HeartbeatEvent>(events[3]);
        Assert.Equal(6, otherNode.NodeId);
        Assert.Null(otherNode.PreviousState);

        var change = Assert.IsType<HeartbeatEvent>(events[4]);
        Assert.Equal(NmtState.PreOperational, change.Heartbeat.State);
        Assert.Equal(NmtState.Operational, change.PreviousState);
        Assert.True(change.IsStateChange);
    }

    [Fact]
    public void Pdos_decode_through_the_projected_database()
    {
        var database = PdoDatabase.Create(EdsReader.LoadFile(TestFiles.Eds("pdo_test.eds")));
        var events = Interpret(database, ["185#FDFFEF0403020102", "385#00"]);

        // 0x385 is TPDO3, disabled in the fixture, so only TPDO1 decodes
        var pdo = Assert.IsType<PdoEvent>(Assert.Single(events));

        Assert.Equal("TxPDO1_node5", pdo.Message.Name);
        Assert.True(pdo.Signals["INTEGER16 value"] == -3);
        Assert.True(pdo.Signals["INTEGER32 value"] == 0x01020304);
        Assert.True(pdo.Signals["BOOLEAN value 2"] == 1);
    }

    [Fact]
    public void Expedited_sdo_transfers_complete_on_the_response()
    {
        // ported from test_sdo.py::test_expedited_upload / test_expedited_download
        var events = Interpret(
            "605#4018100100000000",
            "585#4318100104000000",
            "605#2B171000A00F0000",
            "585#6017100000000000");

        var upload = Assert.IsType<SdoUploadEvent>(events[0]);
        Assert.Equal(5, upload.NodeId);
        Assert.Equal(0x1018, upload.Index);
        Assert.Equal(1, upload.Subindex);
        Assert.Equal(new byte[] { 0x04, 0x00, 0x00, 0x00 }, upload.Data);

        var download = Assert.IsType<SdoDownloadEvent>(events[1]);
        Assert.Equal(0x1017, download.Index);
        Assert.Equal(new byte[] { 0xA0, 0x0F }, download.Data);
        Assert.Equal(2, events.Count);
    }

    [Fact]
    public void A_segmented_download_reassembles_on_the_final_ack()
    {
        // ported from test_sdo.py::test_segmented_download ("A long string")
        var events = Interpret(
            "605#210020000D000000",
            "585#6000200000000000",
            "605#0041206C6F6E6720",
            "585#2000000000000000",
            "605#13737472696E6700",
            "585#3000000000000000");

        var download = Assert.IsType<SdoDownloadEvent>(Assert.Single(events));

        Assert.Equal(0x2000, download.Index);
        Assert.Equal("A long string", Encoding.ASCII.GetString(download.Data));
    }

    [Fact]
    public void A_segmented_upload_reassembles_on_the_last_segment()
    {
        // ported from test_sdo.py::test_segmented_upload ("Tiny Node - Mega Domains !")
        var events = Interpret(
            "605#4008100000000000",
            "585#410810001A000000",
            "605#6000000000000000",
            "585#0054696E79204E6F",
            "605#7000000000000000",
            "585#106465202D204D65",
            "605#6000000000000000",
            "585#00676120446F6D61",
            "605#7000000000000000",
            "585#15696E7320210000");

        var upload = Assert.IsType<SdoUploadEvent>(Assert.Single(events));

        Assert.Equal(0x1008, upload.Index);
        Assert.Equal(0, upload.Subindex);
        Assert.Equal("Tiny Node - Mega Domains !", Encoding.ASCII.GetString(upload.Data));
    }

    [Fact]
    public void An_abort_ends_the_pending_transfer()
    {
        // ported from test_sdo.py::test_abort
        var events = Interpret(
            "605#4018100100000000",
            "585#8018100111000906",
            "585#0054696E79204E6F");   // stray segment after the abort: no transfer left

        var abort = Assert.IsType<SdoAbortEvent>(Assert.Single(events));

        Assert.Equal(5, abort.NodeId);
        Assert.Equal(SdoDirection.Response, abort.Direction);
        Assert.Equal(SdoAbortCode.SubindexDoesNotExist, abort.Abort.Code);
    }

    [Fact]
    public void A_block_download_reassembles_and_completes_on_the_end_ack()
    {
        // ported from test_sdo.py::test_block_download (30 bytes to 0x2000:00)
        var events = Interpret(
            "602#C60020001E000000",
            "582#A40020007F000000",
            "602#0141207265616C6C",
            "602#0279207265616C6C",
            "602#0379206C6F6E6720",
            "602#04737472696E672E",
            "602#852E2E0000000000",
            "582#A2057F0000000000",
            "602#D545690000000000",
            "582#A100000000000000");

        var download = Assert.IsType<SdoDownloadEvent>(Assert.Single(events));

        Assert.Equal(2, download.NodeId);
        Assert.Equal(0x2000, download.Index);
        Assert.Equal("A really really long string...", Encoding.ASCII.GetString(download.Data));
    }

    [Fact]
    public void A_block_upload_reassembles_and_completes_on_the_end_frame()
    {
        // ported from test_sdo.py::test_block_upload (26 bytes from 0x1008:00)
        var events = Interpret(
            "602#A40810007F000000",
            "582#C60810001A000000",
            "602#A300000000000000",
            "582#0154696E79204E6F",
            "582#026465202D204D65",
            "582#03676120446F6D61",
            "582#84696E7320210000",
            "602#A2047F0000000000",
            "582#C940E10000000000",
            "602#A100000000000000");

        var upload = Assert.IsType<SdoUploadEvent>(Assert.Single(events));

        Assert.Equal(2, upload.NodeId);
        Assert.Equal(0x1008, upload.Index);
        Assert.Equal("Tiny Node - Mega Domains !", Encoding.ASCII.GetString(upload.Data));
    }

    [Fact]
    public void A_partially_acknowledged_block_is_retransmitted()
    {
        // the retransmission mechanism of test_sdo.py::test_sdo_block_upload_retransmit:
        // a sequence number gap makes the client ack only the good prefix, and the
        // server restarts the round from the last acknowledged position
        var events = Interpret(
            "602#A40810007F000000",
            "582#C408100000000000",
            "602#A300000000000000",
            "582#0141424344454647",   // seq 1: "ABCDEFG"
            "582#834F505152535455",   // seq 3 arrives instead of 2: ignored past the gap
            "602#A2017F0000000000",   // client acks the good prefix only
            "582#0148494A4B4C4D4E",   // retransmit restarts at seq 1: "HIJKLMN"
            "582#824F505152535455",   // seq 2, last: "OPQRSTU"
            "602#A2027F0000000000",
            "582#C100000000000000",
            "602#A100000000000000");

        var upload = Assert.IsType<SdoUploadEvent>(Assert.Single(events));

        Assert.Equal("ABCDEFGHIJKLMNOPQRSTU", Encoding.ASCII.GetString(upload.Data));
    }

    [Fact]
    public void Non_canopen_frames_are_skipped()
    {
        Assert.Empty(Interpret(
            "705#R",                    // node guarding poll (remote frame)
            "7E4#0000000000000000",     // LSS
            "180#00",                   // TPDO1 without a node id is not a connection-set object
            "605#E000000000000000",     // reserved SDO command specifier
            "000#01",                   // truncated NMT command
            "18DAF110#0102030405060708"));   // extended id, no database
    }
}
