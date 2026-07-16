using CanTools.Logs;
using CanTools.Model;

namespace CanTools.CanOpen;

/// <summary>
/// Folds a CAN log stream into typed CANopen events: NMT commands, boot-ups,
/// heartbeats, emergencies, SYNC, TIME, PDOs decoded through a projected
/// database, and completed SDO transfers — expedited, segmented and block
/// transfers are reassembled per node. Stateful but free of I/O; feed it
/// entries in log order.
/// </summary>
public sealed class CanOpenLogInterpreter
{
    private readonly Database? _pdoDatabase;
    private readonly Dictionary<int, NmtState> _nodeStates = [];
    private readonly Dictionary<int, SdoTransfer> _transfers = [];

    /// <param name="pdoDatabase">
    /// A database projected with <see cref="PdoDatabase.Create"/> (or any other
    /// database); frames matching one of its messages become <see cref="PdoEvent"/>s.
    /// </param>
    public CanOpenLogInterpreter(Database? pdoDatabase = null)
    {
        _pdoDatabase = pdoDatabase;
    }

    /// <summary>Interprets a whole stream, yielding the events it produces.</summary>
    public IEnumerable<CanOpenEvent> Interpret(IEnumerable<LogEntry> entries)
    {
        foreach (var entry in entries)
        {
            if (Interpret(entry) is { } canOpenEvent)
            {
                yield return canOpenEvent;
            }
        }
    }

    /// <summary>
    /// Interprets one frame. Returns null for frames that do not complete an
    /// event: intermediate SDO frames, unknown ids and malformed protocol frames.
    /// </summary>
    public CanOpenEvent? Interpret(LogEntry entry)
    {
        if (entry.IsRemoteFrame)
        {
            return null;
        }

        if (!entry.IsExtendedFrame && entry.FrameId <= 0x7FF)
        {
            try
            {
                var cobId = new CobId(entry.FrameId);

                switch (cobId.Function)
                {
                    case CanOpenFunction.Nmt:
                        return new NmtCommandEvent(entry, NmtMessage.Parse(entry.Data));
                    case CanOpenFunction.Sync:
                        return new SyncEvent(entry, SyncMessage.Parse(entry.Data));
                    case CanOpenFunction.Time:
                        return new TimeEvent(entry, TimeMessage.Parse(entry.Data));
                    case CanOpenFunction.Emergency:
                        return new EmergencyEvent(
                            entry, cobId.NodeId, EmergencyMessage.Parse(entry.Data));
                    case CanOpenFunction.Heartbeat:
                        return InterpretHeartbeat(entry, cobId.NodeId);
                    case CanOpenFunction.SdoReceive:
                        return InterpretSdo(entry, cobId.NodeId, SdoDirection.Request);
                    case CanOpenFunction.SdoTransmit:
                        return InterpretSdo(entry, cobId.NodeId, SdoDirection.Response);
                    case CanOpenFunction.LssTransmit:
                    case CanOpenFunction.LssReceive:
                        return null;
                }
            }
            catch (DecodeException)
            {
                // A malformed protocol frame is skipped like an unparseable log line.
                return null;
            }
        }

        return InterpretPdo(entry);
    }

    private CanOpenEvent InterpretHeartbeat(LogEntry entry, int nodeId)
    {
        var heartbeat = HeartbeatMessage.Parse(entry.Data);
        NmtState? previous = _nodeStates.TryGetValue(nodeId, out var state) ? state : null;
        _nodeStates[nodeId] = heartbeat.State;

        return heartbeat.IsBootUp
            ? new BootUpEvent(entry, nodeId)
            : new HeartbeatEvent(entry, nodeId, heartbeat, previous);
    }

    private CanOpenEvent? InterpretPdo(LogEntry entry)
    {
        if (_pdoDatabase is null
            || !_pdoDatabase.TryGetMessageByFrameId(entry.FrameId, out var message, entry.IsExtendedFrame))
        {
            return null;
        }

        try
        {
            return new PdoEvent(entry, message, message.Decode(entry.Data));
        }
        catch (DecodeException)
        {
            return null;
        }
    }

    private CanOpenEvent? InterpretSdo(LogEntry entry, int nodeId, SdoDirection direction)
    {
        var transfer = _transfers.GetValueOrDefault(nodeId);

        // Mid-block data frames carry a sequence number instead of a command
        // specifier, so they must be intercepted before frame parsing.
        if (transfer is { Phase: BlockPhase.Data } && direction == transfer.CarrierDirection)
        {
            transfer.AddBlockSegment(entry.Data);
            return null;
        }

        switch (SdoFrame.Parse(direction, entry.Data))
        {
            case SdoAbort abort:
                _transfers.Remove(nodeId);
                return new SdoAbortEvent(entry, nodeId, direction, abort);

            case SdoUploadRequest request:
                _transfers[nodeId] = new SdoTransfer(isDownload: false)
                {
                    Index = request.Index,
                    Subindex = request.Subindex,
                };
                return null;

            case SdoUploadResponse { IsExpedited: true } response:
                _transfers.Remove(nodeId);
                return new SdoUploadEvent(
                    entry, nodeId, response.Index, response.Subindex, response.ExpeditedData!);

            case SdoUploadResponse response:
                // segmented: the value follows in segments
                _transfers[nodeId] = new SdoTransfer(isDownload: false)
                {
                    Index = response.Index,
                    Subindex = response.Subindex,
                };
                return null;

            case SdoUploadSegmentResponse segment when transfer is { IsDownload: false }:
                transfer.Data.AddRange(segment.Data);

                if (!segment.IsLast)
                {
                    return null;
                }

                _transfers.Remove(nodeId);
                return new SdoUploadEvent(
                    entry, nodeId, transfer.Index, transfer.Subindex, transfer.Data.ToArray());

            case SdoDownloadRequest { IsExpedited: true } request:
                _transfers[nodeId] = new SdoTransfer(isDownload: true)
                {
                    Index = request.Index,
                    Subindex = request.Subindex,
                    Expedited = request.ExpeditedData,
                };
                return null;

            case SdoDownloadRequest request:
                _transfers[nodeId] = new SdoTransfer(isDownload: true)
                {
                    Index = request.Index,
                    Subindex = request.Subindex,
                };
                return null;

            case SdoDownloadSegmentRequest segment when transfer is { IsDownload: true }:
                transfer.Data.AddRange(segment.Data);
                transfer.LastSegmentSeen |= segment.IsLast;
                return null;

            case SdoDownloadResponse when transfer is { Expedited: not null }:
                _transfers.Remove(nodeId);
                return new SdoDownloadEvent(
                    entry, nodeId, transfer.Index, transfer.Subindex, transfer.Expedited);

            case SdoDownloadSegmentResponse when transfer is { LastSegmentSeen: true }:
                _transfers.Remove(nodeId);
                return new SdoDownloadEvent(
                    entry, nodeId, transfer.Index, transfer.Subindex, transfer.Data.ToArray());

            case SdoBlockFrame block:
                return InterpretBlock(entry, nodeId, transfer, block);

            default:
                return null;
        }
    }

    private CanOpenEvent? InterpretBlock(
        LogEntry entry, int nodeId, SdoTransfer? transfer, SdoBlockFrame block)
    {
        if (block.IsCarrierSide)
        {
            if (!block.IsEnd)
            {
                if (block.Direction == SdoDirection.Request)
                {
                    // block download initiate
                    var (index, subindex) = block.Multiplexer;
                    _transfers[nodeId] = new SdoTransfer(isDownload: true)
                    {
                        Index = index,
                        Subindex = subindex,
                        Phase = BlockPhase.Initiating,
                    };
                }

                // The block upload initiate response echoes index/subindex and may
                // announce a size; the transfer was already created by the request.
                return null;
            }

            // End frame: the CRC in bytes 1-2 is not verified; a log records what
            // happened either way.
            if (transfer is null)
            {
                return null;
            }

            transfer.TrimBlockTail(block.PaddingCount);

            if (block.Direction == SdoDirection.Response)
            {
                // A block upload is complete with the server's end frame.
                _transfers.Remove(nodeId);
                return new SdoUploadEvent(
                    entry, nodeId, transfer.Index, transfer.Subindex, transfer.Data.ToArray());
            }

            transfer.BlockEndSeen = true;
            return null;
        }

        switch (block.SubCommand)
        {
            case 0 when block.Direction == SdoDirection.Request:
                // block upload initiate (blksize in byte 4, pst in byte 5)
                var (uploadIndex, uploadSubindex) = block.Multiplexer;
                _transfers[nodeId] = new SdoTransfer(isDownload: false)
                {
                    Index = uploadIndex,
                    Subindex = uploadSubindex,
                    Phase = BlockPhase.Initiating,
                };
                return null;

            case 0:
                // block download initiate ack: the data phase begins
                if (transfer is { IsDownload: true, Phase: BlockPhase.Initiating })
                {
                    transfer.Phase = BlockPhase.Data;
                }

                return null;

            case 3:
                // block upload start: the server begins streaming
                if (transfer is { IsDownload: false, Phase: BlockPhase.Initiating })
                {
                    transfer.Phase = BlockPhase.Data;
                }

                return null;

            case 2:
                // segment ack: commits sequence numbers 1..ackseq of the round
                transfer?.AcknowledgeBlock(block.AckSequence);
                return null;

            default:
                // end ack: a block download is complete once the server confirms
                if (transfer is { IsDownload: true, BlockEndSeen: true })
                {
                    _transfers.Remove(nodeId);
                    return new SdoDownloadEvent(
                        entry, nodeId, transfer.Index, transfer.Subindex, transfer.Data.ToArray());
                }

                return null;
        }
    }

    private enum BlockPhase
    {
        None,
        Initiating,
        Data,
        AwaitingEnd,
    }

    private sealed class SdoTransfer
    {
        // Segments of the current block round, by sequence number; only an ack
        // commits them, so retransmitted rounds overwrite instead of duplicating.
        private readonly byte[]?[] _round = new byte[128][];
        private int _lastSequence;

        public SdoTransfer(bool isDownload)
        {
            IsDownload = isDownload;
        }

        public bool IsDownload { get; }

        public ushort Index { get; init; }

        public byte Subindex { get; init; }

        public byte[]? Expedited { get; init; }

        public bool LastSegmentSeen { get; set; }

        public bool BlockEndSeen { get; set; }

        public BlockPhase Phase { get; set; }

        public List<byte> Data { get; } = [];

        /// <summary>The direction the payload travels in: requests for downloads.</summary>
        public SdoDirection CarrierDirection =>
            IsDownload ? SdoDirection.Request : SdoDirection.Response;

        public void AddBlockSegment(byte[] frame)
        {
            var sequence = frame[0] & 0x7F;

            if (sequence == 0)
            {
                return;
            }

            _round[sequence] = frame.Length > 1 ? frame[1..Math.Min(8, frame.Length)] : [];

            if ((frame[0] & 0x80) != 0)
            {
                _lastSequence = sequence;
            }
        }

        public void AcknowledgeBlock(int acknowledged)
        {
            for (var sequence = 1; sequence <= acknowledged && sequence < _round.Length; sequence++)
            {
                if (_round[sequence] is { } segment)
                {
                    Data.AddRange(segment);
                }
            }

            // A partially acknowledged round is retransmitted from sequence 1.
            var finished = _lastSequence != 0 && acknowledged >= _lastSequence;
            Array.Clear(_round);
            _lastSequence = 0;

            if (finished)
            {
                Phase = BlockPhase.AwaitingEnd;
            }
        }

        public void TrimBlockTail(int count)
        {
            if (count > 0 && Data.Count >= count)
            {
                Data.RemoveRange(Data.Count - count, count);
            }
        }
    }
}
