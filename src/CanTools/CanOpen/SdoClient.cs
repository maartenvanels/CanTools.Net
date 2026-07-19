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

    // Filled in by Task 7.
    private Task<byte[]> UploadSegmentedAsync(ushort index, byte subIndex, CancellationToken ct) =>
        throw new NotSupportedException("Segmented upload is added in Task 7.");

    private Task DownloadSegmentedAsync(
        ushort index, byte subIndex, byte[] data, CancellationToken ct) =>
        throw new NotSupportedException("Segmented download is added in Task 7.");

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

            if (parsed is SdoAbort abort)
            {
                throw new SdoAbortException(abort.Index, abort.Subindex, abort.Code);
            }

            return parsed;
        }
    }
}
