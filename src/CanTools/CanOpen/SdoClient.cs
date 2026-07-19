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
