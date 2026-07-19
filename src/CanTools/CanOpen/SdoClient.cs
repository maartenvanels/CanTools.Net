using CanTools.Transport;

namespace CanTools.CanOpen;

/// <summary>
/// An active CANopen SDO client: reads (upload) and writes (download) an entry on
/// a remote node over an <see cref="ICanChannel"/>. Uses the default CiA 301 SDO
/// COB-IDs (0x600 + node id for requests, 0x580 + node id for responses). One
/// transfer runs at a time per instance.
/// </summary>
public sealed class SdoClient
{
    private readonly ICanChannel _channel;
    private readonly byte _nodeId;
    private readonly SdoClientOptions _options;
    private readonly SemaphoreSlim _gate = new(1, 1);

    public SdoClient(ICanChannel channel, byte nodeId, SdoClientOptions? options = null)
    {
        _channel = channel ?? throw new ArgumentNullException(nameof(channel));
        _nodeId = nodeId;
        _options = options ?? new SdoClientOptions();
    }

    private uint RequestCobId => 0x600u + _nodeId;

    private uint ResponseCobId => 0x580u + _nodeId;

    /// <summary>Reads the raw bytes of an entry.</summary>
    public async Task<byte[]> UploadAsync(ushort index, byte subIndex, CancellationToken ct = default)
    {
        await _gate.WaitAsync(ct);
        try
        {
            if (_options.EnableBlockTransfer)
            {
                if (await TryBlockUploadAsync(index, subIndex, ct) is { } blockValue)
                {
                    return blockValue;
                }
            }

            await SendAsync(new SdoUploadRequest(index, subIndex).ToBytes(), ct);
            var response = await ReceiveResponseAsync(index, subIndex, ct);

            if (response is not SdoUploadResponse initiate)
            {
                throw new SdoProtocolException(
                    $"Expected an upload response for 0x{index:X4}sub{subIndex:X}, "
                    + $"got {response.GetType().Name}.");
            }

            if (initiate.IsExpedited)
            {
                return initiate.ExpeditedData!;
            }

            return await UploadSegmentedAsync(index, subIndex, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>Writes the raw bytes of an entry.</summary>
    public async Task DownloadAsync(
        ushort index, byte subIndex, byte[] data, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(data);

        await _gate.WaitAsync(ct);
        try
        {
            if (_options.EnableBlockTransfer && data.Length > 4)
            {
                if (await TryBlockDownloadAsync(index, subIndex, data, ct))
                {
                    return;
                }
            }

            if (data.Length <= 4)
            {
                await SendAsync(
                    new SdoDownloadRequest(index, subIndex, data).ToBytes(), ct);
                var response = await ReceiveResponseAsync(index, subIndex, ct);

                if (response is not SdoDownloadResponse)
                {
                    throw new SdoProtocolException(
                        $"Expected a download response for 0x{index:X4}sub{subIndex:X}, "
                        + $"got {response.GetType().Name}.");
                }

                return;
            }

            await DownloadSegmentedAsync(index, subIndex, data, ct);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<byte[]> UploadSegmentedAsync(ushort index, byte subIndex, CancellationToken ct)
    {
        var data = new List<byte>();
        var toggle = false;

        while (true)
        {
            await SendAsync(new SdoUploadSegmentRequest(toggle).ToBytes(), ct);
            var response = await ReceiveResponseAsync(index, subIndex, ct);

            if (response is not SdoUploadSegmentResponse segment)
            {
                throw new SdoProtocolException(
                    $"Expected an upload segment for 0x{index:X4}sub{subIndex:X}, "
                    + $"got {response.GetType().Name}.");
            }

            if (segment.Toggle != toggle)
            {
                throw new SdoProtocolException(
                    $"SDO toggle bit mismatch on 0x{index:X4}sub{subIndex:X}.");
            }

            data.AddRange(segment.Data);

            if (segment.IsLast)
            {
                return data.ToArray();
            }

            toggle = !toggle;
        }
    }

    private async Task DownloadSegmentedAsync(
        ushort index, byte subIndex, byte[] data, CancellationToken ct)
    {
        await SendAsync(
            new SdoDownloadRequest(index, subIndex) { SizeSpecified = true, Size = (uint)data.Length }.ToBytes(),
            ct);
        var initiate = await ReceiveResponseAsync(index, subIndex, ct);

        if (initiate is not SdoDownloadResponse)
        {
            throw new SdoProtocolException(
                $"Expected a download initiate ack for 0x{index:X4}sub{subIndex:X}, "
                + $"got {initiate.GetType().Name}.");
        }

        var toggle = false;

        for (var offset = 0; offset < data.Length; offset += 7)
        {
            var count = Math.Min(7, data.Length - offset);
            var isLast = offset + count >= data.Length;
            var payload = data[offset..(offset + count)];

            await SendAsync(
                new SdoDownloadSegmentRequest(toggle, payload, isLast).ToBytes(), ct);
            var ack = await ReceiveResponseAsync(index, subIndex, ct);

            if (ack is not SdoDownloadSegmentResponse segmentAck || segmentAck.Toggle != toggle)
            {
                throw new SdoProtocolException(
                    $"Bad download segment ack on 0x{index:X4}sub{subIndex:X}.");
            }

            toggle = !toggle;
        }
    }

    private async Task<bool> TryBlockDownloadAsync(
        ushort index, byte subIndex, byte[] data, CancellationToken ct)
    {
        // initiate: ccs=6, cs=0, s=1 (size), command 0xC2
        var initiate = SdoFrame.BuildBlockInitiate(0xC2, index, subIndex, (uint)data.Length);
        await SendAsync(initiate, ct);

        SdoFrame response;
        try
        {
            response = await ReceiveResponseAsync(index, subIndex, ct);
        }
        catch (SdoAbortException)
        {
            return false;   // server declined block transfer; caller falls back
        }

        if (response is not SdoBlockFrame)
        {
            throw new SdoProtocolException(
                $"Expected a block-download initiate ack for 0x{index:X4}sub{subIndex:X}.");
        }

        var sequence = 0;
        var lastSegmentCount = 0;

        for (var offset = 0; offset < data.Length; offset += 7)
        {
            var count = Math.Min(7, data.Length - offset);
            var isLast = offset + count >= data.Length;
            sequence++;
            lastSegmentCount = count;

            var frame = new byte[8];
            frame[0] = (byte)(sequence | (isLast ? 0x80 : 0));
            data.AsSpan(offset, count).CopyTo(frame.AsSpan(1));
            await SendAsync(frame, ct);

            // Ack after the block fills up (blksize) or after the last segment.
            if (isLast || sequence >= _options.BlockSize)
            {
                var ack = await ReceiveResponseAsync(index, subIndex, ct);
                if (ack is not SdoBlockFrame { SubCommand: 2 })
                {
                    throw new SdoProtocolException(
                        $"Expected a block segment ack for 0x{index:X4}sub{subIndex:X}.");
                }

                sequence = 0;
            }
        }

        // end: ccs=6, cs=1, padding = 7 - bytes in the last segment
        var padding = 7 - lastSegmentCount;
        await SendAsync([(byte)(0xC0 | (padding << 2) | 0x01), 0, 0, 0, 0, 0, 0, 0], ct);

        var endAck = await ReceiveResponseAsync(index, subIndex, ct);
        if (endAck is not SdoBlockFrame)
        {
            throw new SdoProtocolException(
                $"Expected a block-download end ack for 0x{index:X4}sub{subIndex:X}.");
        }

        return true;
    }

    private async Task<byte[]?> TryBlockUploadAsync(ushort index, byte subIndex, CancellationToken ct)
    {
        // initiate: ccs=5, cs=0, blksize in byte 4, command 0xA0
        var initiate = SdoFrame.BuildBlockInitiate(0xA0, index, subIndex, 0);
        initiate[4] = (byte)_options.BlockSize;
        await SendAsync(initiate, ct);

        SdoFrame response;
        try
        {
            response = await ReceiveResponseAsync(index, subIndex, ct);
        }
        catch (SdoAbortException)
        {
            return null;   // server declined; caller falls back to normal upload
        }

        if (response is not SdoBlockFrame)
        {
            throw new SdoProtocolException(
                $"Expected a block-upload initiate response for 0x{index:X4}sub{subIndex:X}.");
        }

        // start: ccs=5, cs=3, command 0xA3
        await SendAsync([0xA3, 0, 0, 0, 0, 0, 0, 0], ct);

        var reassembler = new SdoBlockReassembler();

        while (true)
        {
            var frame = await ReceiveRawAsync(ct);   // raw: data segments carry no command specifier
            reassembler.AddSegment(frame.Data);
            var lastInRound = (frame.Data[0] & 0x80) != 0;

            if (lastInRound)
            {
                var ackseq = frame.Data[0] & 0x7F;
                reassembler.Acknowledge(ackseq);
                // segment ack: ccs=5, cs=2, ackseq in byte 1, blksize in byte 2
                await SendAsync([0xA2, (byte)ackseq, (byte)_options.BlockSize, 0, 0, 0, 0, 0], ct);

                // end frame: carrier side, IsEnd
                var end = await ReceiveResponseAsync(index, subIndex, ct);
                if (end is SdoBlockFrame { IsEnd: true } endFrame)
                {
                    reassembler.TrimTail(endFrame.PaddingCount);
                    // end ack: ccs=5, cs=1, command 0xA1
                    await SendAsync([0xA1, 0, 0, 0, 0, 0, 0, 0], ct);
                    return reassembler.Data.ToArray();
                }

                throw new SdoProtocolException(
                    $"Expected a block-upload end frame for 0x{index:X4}sub{subIndex:X}.");
            }
        }
    }

    // Receives the next frame from our server without SDO command parsing (block
    // data segments have no command specifier). Applies the same id filter and timeout.
    private async Task<CanFrame> ReceiveRawAsync(CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(_options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        while (true)
        {
            CanFrame frame;
            try
            {
                frame = await _channel.ReceiveAsync(linked.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new SdoTimeoutException(0, 0);
            }

            if (frame.Id == ResponseCobId)
            {
                return frame;
            }
        }
    }

    private async Task SendAsync(byte[] payload, CancellationToken ct) =>
        await _channel.SendAsync(new CanFrame(RequestCobId, payload), ct);

    private async Task<SdoFrame> ReceiveResponseAsync(
        ushort index, byte subIndex, CancellationToken ct)
    {
        using var timeout = new CancellationTokenSource(_options.Timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);

        while (true)
        {
            CanFrame frame;
            try
            {
                frame = await _channel.ReceiveAsync(linked.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !ct.IsCancellationRequested)
            {
                throw new SdoTimeoutException(index, subIndex);
            }

            if (frame.Id != ResponseCobId)
            {
                continue;   // traffic for another node or service
            }

            var parsed = SdoFrame.Parse(SdoDirection.Response, frame.Data);

            if (Multiplexer(parsed) is { } mux && (mux.Index != index || mux.Subindex != subIndex))
            {
                continue;   // stale/unrelated response (e.g. a late reply to a timed-out transfer)
            }

            if (parsed is SdoAbort abort)
            {
                throw new SdoAbortException(abort.Index, abort.Subindex, abort.Code);
            }

            return parsed;
        }

        // Extracts the (index, subindex) multiplexer when the frame carries one.
        // Segment responses and block data frames have no multiplexer of their own
        // and must pass through untouched — they're correlated by protocol state,
        // not by index/subindex, in the segmented/block transfer code (Tasks 7-8).
        static (ushort Index, byte Subindex)? Multiplexer(SdoFrame frame) => frame switch
        {
            SdoUploadResponse r => (r.Index, r.Subindex),
            SdoDownloadResponse r => (r.Index, r.Subindex),
            SdoAbort a => (a.Index, a.Subindex),
            _ => null,
        };
    }
}
